using System;
using MiningHub.Core.Configuration;

namespace MiningHub.Core.Persistence.Model
{
    public class BalanceChange
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public CoinType Coin { get; set; }
        public string Address { get; set; }

        /// <summary>
        /// Amount owed in pool-base-currency
        /// </summary>
        public decimal Amount { get; set; }

        public string Usage { get; set; }

        public DateTime Created { get; set; }
    }
}
