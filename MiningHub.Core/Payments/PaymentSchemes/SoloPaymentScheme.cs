using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using MiningHub.Core.Configuration;
using MiningHub.Core.Persistence;
using MiningHub.Core.Persistence.Model;
using MiningHub.Core.Persistence.Repositories;
using NLog;
using Contract = MiningHub.Core.Contracts.Contract;

namespace MiningHub.Core.Payments.PaymentSchemes
{
    /// <summary>
    /// PPLNS payout scheme implementation
    /// </summary>
    public class SoloPaymentScheme : IPayoutScheme
    {
        public SoloPaymentScheme(IConnectionFactory cf,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));

            this.cf = cf;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IShareRepository shareRepo;
        private static readonly ILogger logger = LogManager.GetLogger("Solo Payment", typeof(SoloPaymentScheme));

        private class Config
        {
            public decimal Factor { get; set; }
        }

        #region IPayoutScheme

        public Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig,
            IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            // calculate rewards
            var rewards = new Dictionary<string, decimal>();
            var shareCutOffDate = CalculateRewards(poolConfig, block, blockReward, rewards);

            // update balances
            foreach(var address in rewards.Keys)
            {
                var amount = rewards[address];

                if (amount > 0)
                {
                    logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for block {block.BlockHeight}");
                    balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount, $"Reward for block {block.BlockHeight}");
                }
            }

            // delete discarded shares
            if (shareCutOffDate.HasValue)
            {
                var cutOffCount = shareRepo.CountSharesBeforeCreated(con, tx, poolConfig.Id, shareCutOffDate.Value);

                if (cutOffCount > 0)
                {
#if !DEBUG
                    logger.Info(() => $"Deleting {cutOffCount} discarded shares before {shareCutOffDate.Value:O}");
                    shareRepo.DeleteSharesBeforeCreated(con, tx, poolConfig.Id, shareCutOffDate.Value);
#endif
                }
            }

            return Task.FromResult(true);
        }

        #endregion // IPayoutScheme

        private DateTime? CalculateRewards(PoolConfig poolConfig, Block block, decimal blockReward, Dictionary<string, decimal> rewards)
        {
            var recipients = poolConfig.RewardRecipients
                .Where(x => x.Type?.ToLower() == "solo")
                .Select(x=> x.Address)
                .ToArray();

            if (recipients.Length == 0)
                throw new Exception("No reward-recipients of type = 'solo' configured");

            // split reward evenly between configured solo-recipients
            foreach (var address in recipients)
            {
                var reward = blockReward / recipients.Length;
                rewards[address] = reward;
            }

            return block.Created;
        }
    }
}
