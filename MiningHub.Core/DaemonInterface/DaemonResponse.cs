using MiningHub.Core.Configuration;
using MiningHub.Core.JsonRpc;

namespace MiningHub.Core.DaemonInterface
{
    public class DaemonResponse<T>
    {
        public JsonRpcException Error { get; set; }
        public T Response { get; set; }
        public AuthenticatedNetworkEndpointConfig Instance { get; set; }
    }
}
