using System;
using System.Data;
using MiningHub.Core.Persistence.Model;
using MiningHub.Core.Persistence.Model.Projections;

namespace MiningHub.Core.Persistence.Repositories
{
    public interface IShareRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Share share);
        Share[] ReadSharesBeforeCreated(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize);
        Share[] ReadSharesBeforeAndAfterCreated(IDbConnection con, string poolId, DateTime before, DateTime after, bool inclusive, int pageSize);
        Share[] PageSharesBetweenCreated(IDbConnection con, string poolId, DateTime start, DateTime end, int page, int pageSize);

        long CountSharesBeforeCreated(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
        void DeleteSharesBeforeCreated(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);

        long CountSharesBetweenCreated(IDbConnection con, string poolId, string miner, DateTime? start, DateTime? end);
        double? GetAccumulatedShareDifficultyBetweenCreated(IDbConnection con, string poolId, DateTime start, DateTime end);
        MinerWorkerHashes[] GetAccumulatedShareDifficultyTotal(IDbConnection con, string poolId);
        MinerWorkerHashes[] GetHashAccumulationBetweenCreated(IDbConnection con, string poolId, DateTime start, DateTime end);
        DateTime GetLastShare(IDbConnection con, string poolId, string miner, string worker);
    }
}
