using System;
using System.Collections.Generic;
using System.Text;

namespace MiningHub.Core.Persistence.Postgres.Entities
{
    public class MinerSetting
    {
        public string PoolId { get; set; }
        public string Miner { get; set; }
        public decimal MinimumPayout { get; set; }
        public int PayoutTimespan { get; set; }
        public string EmailAddress { get; set; }
        public bool SendWorkerOfflineNotifications { get; set; }
    }
}
