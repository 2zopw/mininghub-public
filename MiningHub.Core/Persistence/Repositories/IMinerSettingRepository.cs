using System;
using System.Data;
using MiningHub.Core.Persistence.Model;

namespace MiningHub.Core.Persistence.Repositories
{
    public interface IMinerSettingRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, MinerSetting minerSetting);
        void DeleteMinerSetting(IDbConnection con, IDbTransaction tx, MinerSetting minerSetting);
        void UpdateMinerSetting(IDbConnection con, IDbTransaction tx, MinerSetting minerSetting);
    }
}
