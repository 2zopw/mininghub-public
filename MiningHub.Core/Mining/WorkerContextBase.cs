using System;
using MiningHub.Core.Configuration;
using MiningHub.Core.Time;

namespace MiningHub.Core.Mining
{
    public class ShareStats
    {
        public int ValidShares { get; set; }
        public int InvalidShares { get; set; }
    }

    public class WorkerContextBase
    {
        public ShareStats Stats { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsAuthorized { get; set; } = false;
        public bool IsSubscribed { get; set; }

        /// <summary>
        /// Difficulty assigned to this worker
        /// </summary>
        public double Difficulty { get; set; }

        /// <summary>
        /// UserAgent reported by Stratum
        /// </summary>
        public string UserAgent { get; set; }

        public void Init(PoolConfig poolConfig, double difficulty, VarDiffConfig varDiffConfig, IMasterClock clock)
        {
            Difficulty = difficulty;
            LastActivity = clock.Now;
            Stats = new ShareStats();
        }

        public void Dispose()
        {
        }
    }
}
