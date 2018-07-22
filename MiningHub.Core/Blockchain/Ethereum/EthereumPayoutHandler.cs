using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningHub.Core.Blockchain.Ethereum.Configuration;
using MiningHub.Core.Blockchain.Ethereum.DaemonRequests;
using MiningHub.Core.Blockchain.Ethereum.DaemonResponses;
using MiningHub.Core.Configuration;
using MiningHub.Core.DaemonInterface;
using MiningHub.Core.Extensions;
using MiningHub.Core.Notifications;
using MiningHub.Core.Payments;
using MiningHub.Core.Persistence;
using MiningHub.Core.Persistence.Model;
using MiningHub.Core.Persistence.Repositories;
using MiningHub.Core.Time;
using MiningHub.Core.Util;
using Newtonsoft.Json;
using Block = MiningHub.Core.Persistence.Model.Block;
using Contract = MiningHub.Core.Contracts.Contract;

namespace MiningHub.Core.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH, CoinType.ETC, CoinType.EXP, CoinType.ELLA, CoinType.UBQ)]
    public class EthereumPayoutHandler : PayoutHandlerBase, IPayoutHandler
    {
        public EthereumPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            NotificationService notificationService) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, notificationService)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.ctx = ctx;
        }

        private readonly IComponentContext ctx;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;
        private ChainType chainType;
        private const int BlockSearchOffset = 50;
        private EthereumPoolPaymentProcessingConfigExtra extraConfig;

        protected override string LogCategory => "Ethereum Payout Handler";

        #region IPayoutHandler

        public async Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            extraConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();

            logger = LogUtil.GetPoolScopedLogger(typeof(EthereumPayoutHandler), poolConfig);

            // configure standard daemon
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            var daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints);

            await DetectChainAsync();
        }

        public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            var pageSize = 100;
            var pageCount = (int)Math.Ceiling(blocks.Length / (double)pageSize);
            var blockCache = new Dictionary<long, DaemonResponses.Block>();
            var result = new List<Block>();

            for (var i = 0; i < pageCount; i++)
            {
                // get a page full of blocks
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                // get latest block
                var latestBlockResponses = await daemon.ExecuteCmdAllAsync<DaemonResponses.Block>(EthCommands.GetBlockByNumber, new[] { (object)"latest", true });
                var latestBlockHeight = latestBlockResponses.First(x => x.Error == null && x.Response?.Height != null).Response.Height.Value;

                // execute batch
                var blockInfos = await FetchBlocks(blockCache, page.Select(block => (long)block.BlockHeight).ToArray());

                for (var j = 0; j < blockInfos.Length; j++)
                {
                    var blockInfo = blockInfos[j];
                    var block = page[j];

                    // extract confirmation data from stored block
                    var mixHash = block.TransactionConfirmationData.Split(":").First();
                    var nonce = block.TransactionConfirmationData.Split(":").Last();

                    // update progress
                    block.ConfirmationProgress = Math.Min(1.0d, (double)(latestBlockHeight - block.BlockHeight) / EthereumConstants.MinConfimations);
                    result.Add(block);

                    // is it block mined by us?
                    if (blockInfo.Miner == poolConfig.Address)
                    {
                        // mature?
                        if (latestBlockHeight - block.BlockHeight >= EthereumConstants.MinConfimations)
                        {
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.Reward = GetBaseBlockReward(chainType, block.BlockHeight); // base reward

                            if (extraConfig?.KeepUncles == false)
                                block.Reward += blockInfo.Uncles.Length * (block.Reward / 32); // uncle rewards

                            if (extraConfig?.KeepTransactionFees == false && blockInfo.Transactions?.Length > 0)
                                block.Reward += await GetTxRewardAsync(blockInfo); // tx fees

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");
                        }

                        continue;
                    }

                    // search for a block containing our block as an uncle by checking N blocks in either direction
                    var heightMin = block.BlockHeight - BlockSearchOffset;
                    var heightMax = Math.Min(block.BlockHeight + BlockSearchOffset, latestBlockHeight);
                    var range = new List<long>();

                    for (var k = heightMin; k < heightMax; k++)
                        range.Add((long)k);

                    // execute batch
                    var blockInfo2s = await FetchBlocks(blockCache, range.ToArray());

                    foreach (var blockInfo2 in blockInfo2s)
                    {
                        // don't give up yet, there might be an uncle
                        if (blockInfo2.Uncles.Length > 0)
                        {
                            // fetch all uncles in a single RPC batch request
                            var uncleBatch = blockInfo2.Uncles.Select((x, index) => new DaemonCmd(EthCommands.GetUncleByBlockNumberAndIndex,
                                    new[] { blockInfo2.Height.Value.ToStringHexWithPrefix(), index.ToStringHexWithPrefix() }))
                                .ToArray();

                            logger.Info(() => $"[{LogCategory}] Fetching {blockInfo2.Uncles.Length} uncles for block {blockInfo2.Height}");

                            var uncleResponses = await daemon.ExecuteBatchAnyAsync(uncleBatch);

                            logger.Info(() => $"[{LogCategory}] Fetched {uncleResponses.Count(x => x.Error == null && x.Response != null)} uncles for block {blockInfo2.Height}");

                            var uncle = uncleResponses.Where(x => x.Error == null && x.Response != null)
                                .Select(x => x.Response.ToObject<DaemonResponses.Block>())
                                .FirstOrDefault(x => x.Miner == poolConfig.Address);

                            if (uncle != null)
                            {
                                // mature?
                                if (latestBlockHeight - uncle.Height.Value >= EthereumConstants.MinConfimations)
                                {
                                    block.Status = BlockStatus.Confirmed;
                                    block.ConfirmationProgress = 1;
                                    block.Reward = GetUncleReward(chainType, uncle.Height.Value, blockInfo2.Height.Value);
                                    block.BlockHeight = uncle.Height.Value;
                                    block.Type = EthereumConstants.BlockTypeUncle;

                                    logger.Info(() => $"[{LogCategory}] Unlocked uncle for block {blockInfo2.Height.Value} at height {uncle.Height.Value} worth {FormatAmount(block.Reward)}");
                                }

                                else
                                    logger.Info(() => $"[{LogCategory}] Got immature matching uncle for block {blockInfo2.Height.Value}. Will try again.");

                                break;
                            }
                        }
                    }

                    if (block.Status == BlockStatus.Pending && block.ConfirmationProgress > 0.10)
                    {
                        // we've lost this one
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                    }
                }
            }

            return result.ToArray();
        }

        public Task CalculateBlockEffortAsync(Block block, double accumulatedBlockShareDiff) 
        {
            block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;
            return Task.FromResult(true);
        }

        public Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            if (block.Reward == 0)
                return Task.FromResult(0m);

            var blockRewardRemaining = block.Reward;

            // Distribute funds to configured reward recipients
            foreach (var recipient in poolConfig.RewardRecipients.Where(x => !x.Percentage.Equals(0)))
            {
                var amount = block.Reward * (recipient.Percentage / 100.0m);
                var address = recipient.Address;

                blockRewardRemaining -= amount;

                // skip transfers from pool wallet to pool wallet
                if (address != poolConfig.Address)
                {
                    logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
                    balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount, $"Reward for block {block.BlockHeight}");
                }
            }

            // Deduct static reserve for tx fees
            blockRewardRemaining -= EthereumConstants.StaticTransactionFeeReserve; //EVR

            return Task.FromResult(blockRewardRemaining);
        }

        public async Task PayoutAsync(Balance[] balances)
        {
            // ensure we have peers
            var infoResponse = await daemon.ExecuteCmdSingleAsync<string>(EthCommands.GetPeerCount);

            if (networkType == EthereumNetworkType.Mainnet &&
                (infoResponse.Error != null || string.IsNullOrEmpty(infoResponse.Response) ||
                infoResponse.Response.IntegralFromHex<int>() < EthereumConstants.MinPayoutPeerCount))
            {
                logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers ({EthereumConstants.MinPayoutPeerCount} required)");
                return;
            }

            var txHashes = new List<string>();

            foreach (var balance in balances)
            {
                try
                {
                    var txHash = await Payout(balance);
                    txHashes.Add(txHash);
                }

                catch (Exception ex)
                {
                    logger.Error(ex);

                    NotifyPayoutFailure(poolConfig.Id, new[] { balance }, ex.Message, null);
                }
            }

            if (txHashes.Any())
                NotifyPayoutSuccess(poolConfig.Id, balances, txHashes.ToArray(), null);
        }

        #endregion // IPayoutHandler

        private async Task<DaemonResponses.Block[]> FetchBlocks(Dictionary<long, DaemonResponses.Block> blockCache, params long[] blockHeights)
        {
            var cacheMisses = blockHeights.Where(x => !blockCache.ContainsKey(x)).ToArray();

            if (cacheMisses.Any())
            {
                var blockBatch = cacheMisses.Select(height => new DaemonCmd(EthCommands.GetBlockByNumber,
                    new[]
                    {
                        (object) height.ToStringHexWithPrefix(),
                        true
                    })).ToArray();

                var tmp = await daemon.ExecuteBatchAnyAsync(blockBatch);

                var transformed = tmp
                    .Where(x => x.Error == null && x.Response != null)
                    .Select(x => x.Response?.ToObject<DaemonResponses.Block>())
                    .Where(x => x != null)
                    .ToArray();

                foreach (var block in transformed)
                    blockCache[(long)block.Height.Value] = block;
            }

            return blockHeights.Select(x => blockCache[x]).ToArray();
        }

        internal static decimal GetBaseBlockReward(ChainType chainType, ulong height)
        {
            if (height > 2508545)
                return 1;
            else if (height > 2150181)
                return 2;
            else if (height > 1791818)
                return 3;
            else if (height > 1433454)
                return 4;
            else if (height > 1075090)
                return 5;
            else if (height > 716727)
                return 6;
            else if (height > 358363)
                return 7;
            else if (height > 0)
                return 8;

            // genesis
            return 0;
        }

        private async Task<decimal> GetTxRewardAsync(DaemonResponses.Block blockInfo)
        {
            // fetch all tx receipts in a single RPC batch request
            var batch = blockInfo.Transactions.Select(tx => new DaemonCmd(EthCommands.GetTxReceipt, new[] { tx.Hash })).ToArray();

            var results = await daemon.ExecuteBatchAnyAsync(batch);
            if (results.Any(x => x.Error != null))
                throw new Exception($"Error fetching tx receipts: {string.Join(", ", results.Where(x => x.Error != null).Select(y => y.Error.Message))}");

            // create lookup table
            var gasUsed = results.Select(x => x.Response.ToObject<TransactionReceipt>())
                .ToDictionary(x => x.TransactionHash, x => x.GasUsed);

            // accumulate
            var result = blockInfo.Transactions.Sum(x => (ulong)gasUsed[x.Hash] * ((decimal)x.GasPrice / EthereumConstants.Wei));

            return result;
        }

        internal static decimal GetUncleReward(ChainType chainType, ulong uheight, ulong height)
        {
            if ((height + 1) > uheight)
                return 0;

            switch (chainType)
            {
                case ChainType.Ubiq:
                    //uncle.reward = (((uncle.number + 2) - block.number) * block.reward) / 2
                    return (((uheight + 2) - height) * GetBaseBlockReward(chainType, height)) / 2m;

                default:
                    return 0;
            }
        }

        private async Task DetectChainAsync()
        {
            var commands = new[]
            {
                new DaemonCmd(EthCommands.GetNetVersion),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    throw new Exception($"Chain detection failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            // convert network
            var netVersion = results[0].Response.ToObject<string>();
            EthereumUtils.DetectNetworkAndChain(netVersion, out networkType, out chainType);
        }

        private async Task<string> Payout(Balance balance)
        {
            // create the request object before unlocking the account 
            // to minimize the time between unlocking and sending the tx (security)
            var request = new SendTransactionRequest
            {
                From = poolConfig.Address,
                To = balance.Address,
                Value = CreateHexTxValue(balance),
            };

            // unlock account
            if (extraConfig.WalletPassword != null)
            {
                var unlockResponse = await daemon.ExecuteCmdSingleAsync<object>(EthCommands.UnlockAccount, new[]
                {
                    poolConfig.Address,
                    extraConfig.WalletPassword,
                    null
                });

                if (unlockResponse.Error != null || unlockResponse.Response == null || (bool)unlockResponse.Response == false)
                    throw new Exception("Unable to unlock the account for sending the transaction");
            }

            // send transaction
            var response = await daemon.ExecuteCmdSingleAsync<string>(EthCommands.SendTx, new[] { request });
            if (response.Error != null)
                throw new Exception($"{EthCommands.SendTx} returned error: {response.Error.Message} code {response.Error.Code}");

            if (string.IsNullOrEmpty(response.Response) || EthereumConstants.ZeroHashPattern.IsMatch(response.Response))
                throw new Exception($"{EthCommands.SendTx} did not return a valid transaction hash");

            var txHash = response.Response;
            logger.Info(() => $"[{LogCategory}] Sent {FormatAmount(balance.Amount)} to {balance.Address}");
            logger.Info(() => $"[{LogCategory}] Payout transaction id: {txHash}");

            // update db
            PersistPayments(new[] { balance }, txHash);

            // done
            return txHash;
        }

        private static string CreateHexTxValue(Balance balance)
        {
            var value = (BigInteger)Math.Floor(balance.Amount * EthereumConstants.Wei);
            return $"0x{value.ToString("x").TrimStart('0')}";
        }
    }
}
