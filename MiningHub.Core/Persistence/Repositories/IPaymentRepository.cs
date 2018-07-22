using System.Data;
using MiningHub.Core.Persistence.Model;

namespace MiningHub.Core.Persistence.Repositories
{
    public interface IPaymentRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Payment payment);

        Payment[] PagePayments(IDbConnection con, string poolId, string address, int page, int pageSize);
        BalanceChange[] PageBalanceChanges(IDbConnection con, string poolId, string address, int page, int pageSize);
    }
}
