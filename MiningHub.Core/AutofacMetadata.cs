using System;
using System.Collections.Generic;
using MiningHub.Core.Configuration;
using MiningHub.Core.Notifications;

namespace MiningHub.Core
{
    public class CoinMetadataAttribute : Attribute
    {
        public CoinMetadataAttribute(IDictionary<string, object> values)
        {
            if (values.ContainsKey(nameof(SupportedCoins)))
                SupportedCoins = (CoinType[]) values[nameof(SupportedCoins)];
        }

        public CoinMetadataAttribute(params CoinType[] supportedCoins)
        {
            SupportedCoins = supportedCoins;
        }

        public CoinType[] SupportedCoins { get; }
    }
}
