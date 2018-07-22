using System.Data;
using MiningHub.Core.Configuration;
using MiningHub.Core.Persistence.Model;

namespace MiningHub.Core.Persistence.Repositories
{
    public interface IBalanceRepository
    {
        void AddAmount(IDbConnection con, IDbTransaction tx, string poolId, CoinType coin, string address, decimal amount, string usage);

        Balance[] GetPoolBalancesOverThreshold(IDbConnection con, string poolId, decimal minimum);
    }
}
