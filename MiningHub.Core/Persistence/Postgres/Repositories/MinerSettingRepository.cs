using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using MiningHub.Core.Extensions;
using MiningHub.Core.Persistence.Model;
using MiningHub.Core.Persistence.Repositories;
using MiningHub.Core.Util;
using NLog;

namespace MiningHub.Core.Persistence.Postgres.Repositories
{
    public class MinerSettingRepository : IMinerSettingRepository
    {
        public MinerSettingRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public void Insert(IDbConnection con, IDbTransaction tx, MinerSetting minerSetting)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.MinerSetting>(minerSetting);

            var query =
                "INSERT INTO minerSettings(poolid, minerSettingheight, networkdifficulty, status, type, transactionconfirmationdata, miner, reward, effort, confirmationprogress, source, hash, created) " +
                "VALUES(@poolid, @minerSettingheight, @networkdifficulty, @status, @type, @transactionconfirmationdata, @miner, @reward, @effort, @confirmationprogress, @source, @hash, @created)";

            con.Execute(query, mapped, tx);
        }

        public void DeleteMinerSetting(IDbConnection con, IDbTransaction tx, MinerSetting minerSetting)
        {
            logger.LogInvoke();

            var query = "DELETE FROM minerSettings WHERE miner = @miner AND poolid = @poolId";
            con.Execute(query, minerSetting, tx);
        }

        public void UpdateMinerSetting(IDbConnection con, IDbTransaction tx, MinerSetting minerSetting)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.MinerSetting>(minerSetting);

            var query = "UPDATE minerSettings SET minerSettingheight = @minerSettingheight, status = @status, type = @type, reward = @reward, effort = @effort, confirmationprogress = @confirmationprogress WHERE miner = @miner AND poolid = @poolId";
            con.Execute(query, mapped, tx);
        }

        public string GetIpAddress(IDbConnection con, string poolId, string miner)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT ipaddress AS ipaddress FROM shares " +
                        "WHERE poolid = @poolId AND miner = @miner " +
                        "limit 1";

            return con.Query<string>(query, new { poolId, miner }).FirstOrDefault();
        }
    }
}
