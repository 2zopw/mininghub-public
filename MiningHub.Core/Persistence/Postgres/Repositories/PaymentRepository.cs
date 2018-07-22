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
    public class PaymentRepository : IPaymentRepository
    {
        public PaymentRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public void Insert(IDbConnection con, IDbTransaction tx, Payment payment)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Payment>(payment);

            var query = "INSERT INTO payments(poolid, coin, address, amount, transactionconfirmationdata, created) " +
                "VALUES(@poolid, @coin, @address, @amount, @transactionconfirmationdata, @created)";

            con.Execute(query, mapped, tx);
        }

        public Payment[] PagePayments(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM payments WHERE poolid = @poolid ";

            if (!string.IsNullOrEmpty(address))
                query += " AND address = @address ";

            query += "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.Payment>(query, new { poolId, address, offset = page * pageSize, pageSize })
                .Select(mapper.Map<Payment>)
                .ToArray();
        }

        public BalanceChange[] PageBalanceChanges(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM balance_changes WHERE poolid = @poolid " +
                        "AND address = @address " +
                        "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.BalanceChange>(query, new { poolId, address, offset = page * pageSize, pageSize })
                .Select(mapper.Map<BalanceChange>)
                .ToArray();
        }
    }
}
