namespace MiningHub.Core.Blockchain.Ethereum.Configuration
{
    public class EthereumPoolConfigExtra
    {
        /// <summary>
        /// Base directory for generated DAGs
        /// </summary>
        public string DagDir { get; set; }

        /// <summary>
        /// If true connects to Websocket port of all daemons and subscribes to streaming job updates for reduced latency
        /// </summary>
        public bool? EnableDaemonWebsocketStreaming { get; set; }
    }
}
