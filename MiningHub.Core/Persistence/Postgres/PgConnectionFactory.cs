using System.Data;
using Npgsql;

namespace MiningHub.Core.Persistence.Postgres
{
    public class PgConnectionFactory : IConnectionFactory
    {
        public PgConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private readonly string connectionString;

        /// <summary>
        /// This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public IDbConnection OpenConnection()
        {
            var con = new NpgsqlConnection(connectionString);
            con.Open();
            return con;
        }
    }
}
