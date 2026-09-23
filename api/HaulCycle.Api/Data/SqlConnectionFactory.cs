using System.Data;
using Microsoft.Data.SqlClient;

namespace HaulCycle.Api.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

/// <summary>Creates a fresh SqlConnection per call from the connection string resolved
/// at startup (HAUL_API_DB_CONN). Dapper opens/closes it around each query.</summary>
public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection CreateConnection() => new SqlConnection(connectionString);
}
