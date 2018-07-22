using System;
using System.Data;

namespace MiningHub.Core.Persistence.Dummy
{
    public class DummyConnectionFactory : IConnectionFactory
    {
        public DummyConnectionFactory(string connectionString)
        {
        }

        /// <summary>
        /// This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public IDbConnection OpenConnection()
        {
            throw new NotImplementedException();
        }
    }
}
