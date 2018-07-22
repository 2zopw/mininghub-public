using System;

namespace MiningHub.Core.Mining
{
    public class PoolStartupAbortException : Exception
    {
        public PoolStartupAbortException(string msg) : base(msg)
        {
        }

        public PoolStartupAbortException()
        {
        }
    }
}
