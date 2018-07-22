using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MiningHub.Core.Extensions;

namespace MiningHub.Core.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider : ExtraNonceProviderBase
    {
        public EthereumExtraNonceProvider() : base(2)
        {
        }
    }
}
