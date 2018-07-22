using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using MiningHub.Core.Crypto.Hashing.Ethash;
using MiningHub.Core.Extensions;
using MiningHub.Core.Stratum;
using NBitcoin;
using NLog;

namespace MiningHub.Core.Blockchain.Ethereum
{
    public class EthereumJob
    {
        public EthereumJob(string id, EthereumBlockTemplate blockTemplate, ILogger logger)
        {
            Id = id;
            BlockTemplate = blockTemplate;
            this.logger = logger;

            var target = blockTemplate.Target;
            if (target.StartsWith("0x"))
                target = target.Substring(2);

            blockTarget = new uint256(target.HexToByteArray().ReverseArray());
        }

        public string Id { get; }
        public EthereumBlockTemplate BlockTemplate { get; }
        private readonly uint256 blockTarget;
        private readonly ILogger logger;
        private readonly Dictionary<StratumClient, HashSet<string>> workerNonces = new Dictionary<StratumClient, HashSet<string>>();
        private void RegisterNonce(StratumClient worker, string nonce)
        {
            var nonceLower = nonce.ToLower();
            if (!workerNonces.TryGetValue(worker, out var nonces))
            {
                nonces = new HashSet<string>(new[] { nonceLower });
                workerNonces[worker] = nonces;
            }
            else
            {
                if (nonces.Contains(nonceLower))
                    throw new StratumException(StratumError.DuplicateShare, "duplicate share");

                nonces.Add(nonceLower);
            }
        }

        public async Task<(Share Share, string FullNonceHex, string HeaderHash, string MixHash)> ProcessShareAsync(StratumClient worker, string nonce, EthashFull ethash)
        {
            // duplicate nonce?
            //lock (workerNonces)
            //{
            //    RegisterNonce(worker, nonce);
            //}

            // assemble full-nonce
            var context = worker.GetContextAs<EthereumWorkerContext>();
            var fullNonceHex = nonce.StartsWith("0x") ? nonce.Substring(2) : nonce;
            if (context.IsNiceHashClient && !string.IsNullOrEmpty(context.ExtraNonce1))
                fullNonceHex = context.ExtraNonce1 + fullNonceHex;

            if (!ulong.TryParse(fullNonceHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fullNonce))
                throw new StratumException(StratumError.Other, "bad nonce " + fullNonceHex);

            // get dag for block
            var dag = await ethash.GetDagAsync(BlockTemplate.Height, logger);

            // compute
            if (!dag.Compute(logger, BlockTemplate.Header.HexToByteArray(), fullNonce, out var mixDigest, out var resultBytes))
                throw new StratumException(StratumError.Other, "bad hash");

            resultBytes.ReverseArray();

            // test if share meets at least workers current difficulty
            var resultValue = new uint256(resultBytes);
            var resultValueBig = resultBytes.ToBigInteger();
            var shareDiff = (double)BigInteger.Divide(EthereumConstants.BigMaxValue, resultValueBig);
            var stratumDifficulty = context.Difficulty * EthereumConstants.StratumDiffFactor;
            var ratio = shareDiff / stratumDifficulty;
            var isBlockCandidate = resultValue <= blockTarget; 

            if (!isBlockCandidate && ratio < 0.98)
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

            // create share
            var share = new Share
            {
                BlockHeight = (long)BlockTemplate.Height,
                IpAddress = worker.RemoteEndpoint?.Address?.ToString(),
                Miner = context.MinerName,
                Worker = context.WorkerName,
                UserAgent = context.UserAgent,
                IsBlockCandidate = isBlockCandidate,
                Difficulty = stratumDifficulty,
                BlockHash = mixDigest.ToHexString(true)
            };

            if (share.IsBlockCandidate)
            {
                var headerHash = BlockTemplate.Header;
                var mixHash = mixDigest.ToHexString(true);

                share.TransactionConfirmationData = $"{mixDigest.ToHexString(true)}:{nonce}";

                return (share, fullNonceHex, headerHash, mixHash);
            }

            return (share, null, null, null);
        }
    }
}
