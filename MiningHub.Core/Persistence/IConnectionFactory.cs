using System.Data;

namespace MiningHub.Core.Persistence
{
    public interface IConnectionFactory
    {
        IDbConnection OpenConnection();
    }
}
