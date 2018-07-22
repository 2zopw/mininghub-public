using System;

namespace MiningHub.Core.Time
{
    public interface IMasterClock
    {
        DateTime Now { get; }
    }
}
