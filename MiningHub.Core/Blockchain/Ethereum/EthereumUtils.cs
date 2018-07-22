using System;
using System.Linq;
using System.Numerics;

namespace MiningHub.Core.Blockchain.Ethereum
{
    public class EthereumUtils
    {
        public static void DetectNetworkAndChain(string netVersionResponse, out EthereumNetworkType networkType, out ChainType chainType)
        {
            // convert network
            if (int.TryParse(netVersionResponse, out var netWorkTypeInt))
            {
                networkType = (EthereumNetworkType)netWorkTypeInt;

                if (!Enum.IsDefined(typeof(EthereumNetworkType), networkType))
                    networkType = EthereumNetworkType.Unknown;
            }

            else
                networkType = EthereumNetworkType.Unknown;

            chainType = ChainType.Ubiq;
        }

        public static string GetTargetHex(BigInteger difficulty)
        {
            // 2^256 / difficulty.
            var target = BigInteger.Divide(BigInteger.Pow(2, 256), difficulty);
            var hex = target.ToString("X16").ToLower();
            return $"0x{string.Concat(Enumerable.Repeat("0", 64 - hex.Length))}{hex}";
        }
    }
}
