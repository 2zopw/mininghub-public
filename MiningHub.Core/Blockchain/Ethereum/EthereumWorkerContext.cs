using MiningHub.Core.Mining;

namespace MiningHub.Core.Blockchain.Ethereum
{
    public class EthereumWorkerContext : WorkerContextBase
    {
        public string MinerName { get; set; }
        public string WorkerName { get; set; }
        public bool IsInitialWorkSent { get; set; } = false;
        public bool IsNiceHashClient { get; set; }
        public string ExtraNonce1 { get; set; }
    }
}
