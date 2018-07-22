using System;
using MiningHub.Core.Configuration;

namespace MiningHub.Core.Persistence.Model
{
    public class Payment
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public CoinType Coin { get; set; }
        public string Address { get; set; }
        public decimal Amount { get; set; }
        public string TransactionConfirmationData { get; set; }
        public DateTime Created { get; set; }
    }
}
