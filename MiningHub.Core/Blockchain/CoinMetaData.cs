using System;
using System.Collections.Generic;
using MiningHub.Core.Blockchain.Ethereum;
using MiningHub.Core.Configuration;

namespace MiningHub.Core.Blockchain
{
    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";

        public static readonly Dictionary<CoinType, Dictionary<string, string>> BlockInfoLinks = new Dictionary<CoinType, Dictionary<string, string>>
        {
            { CoinType.UBQ, new Dictionary<string, string> { { string.Empty, $"https://ubiqexplorer.com/block/{BlockHeightPH}" }}},
            { CoinType.ETH, new Dictionary<string, string>
            {
                { string.Empty, $"https://etherscan.io/block/{BlockHeightPH}" },
                { EthereumConstants.BlockTypeUncle, $"https://etherscan.io/uncle/{BlockHeightPH}" },
            }},

            { CoinType.ETC, new Dictionary<string, string>
            {
                { string.Empty, $"https://gastracker.io/block/{BlockHeightPH}" },
                { EthereumConstants.BlockTypeUncle, $"https://gastracker.io/uncle/{BlockHeightPH}" }
            }},

            { CoinType.ELLA, new Dictionary<string, string> { { string.Empty, $"https://explorer.ellaism.org/block/{BlockHeightPH}" }}},
            { CoinType.EXP, new Dictionary<string, string> { { string.Empty, $"http://www.gander.tech/blocks/{BlockHeightPH}" }}},
        };

        public static readonly Dictionary<CoinType, string> TxInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.UBQ, "https://ubiqexplorer.com/transaction/{0}" },
            { CoinType.ETH, "https://etherscan.io/tx/{0}" },
            { CoinType.ETC, "https://gastracker.io/tx/{0}" },
            { CoinType.ELLA, "https://explorer.ellaism.org/tx/{0}" },
            { CoinType.EXP, "http://www.gander.tech/tx/{0}" },
        };

        public static readonly Dictionary<CoinType, string> AddressInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.UBQ, "https://ubiqexplorer.com/account/{0}" },
            { CoinType.ETH, "https://etherscan.io/address/{0}" },
            { CoinType.ETC, "https://gastracker.io/addr/{0}" },
            { CoinType.ELLA, "https://explorer.ellaism.org/addr/{0}" },
            { CoinType.EXP, "http://www.gander.tech/address/{0}" },
        };

        private const string Ethash = "Ethash";
        private const string Cryptonight = "Cryptonight";
        private const string CryptonightLight = "Cryptonight-Light";

        public static readonly Dictionary<CoinType, Func<CoinType, string, string>> CoinAlgorithm = new Dictionary<CoinType, Func<CoinType, string, string>>
        {
            { CoinType.UBQ, (coin, alg)=> Ethash },
            { CoinType.ETH, (coin, alg)=> Ethash },
            { CoinType.ETC, (coin, alg)=> Ethash },
            { CoinType.ELLA, (coin, alg)=> Ethash },
            { CoinType.EXP, (coin, alg)=> Ethash }
        };
    }
}
