using DnnManager.Application.Abstractions;
using DnnManager.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DnnManager.Infrastructure.Sql;

/// <summary>
/// Admin operations against a remote/Azure SQL Server used when overwriting a production database.
/// Connects through [master] so it can both read database sizing and issue DROP DATABASE - neither
/// of which a contained/database-scoped user can do.
/// </summary>
public sealed class RemoteSqlAdminService : IRemoteSqlAdminService
{
    private readonly ILogger<RemoteSqlAdminService> _log;

    public RemoteSqlAdminService(ILogger<RemoteSqlAdminService> log) => _log = log;

    private static SqlConnection OpenableMaster(SiteSqlConnection target) =>
        new(new SqlConnectionStringBuilder
        {
            DataSource = target.Server,
            InitialCatalog = "master",
            UserID = target.User,
            Password = target.Password,
            Encrypt = true,                  // Azure SQL requires TLS.
            TrustServerCertificate = true,
            ConnectTimeout = 30,
            CommandTimeout = 0
        }.ConnectionString);

    public async Task<Result<RemoteDbInfo>> InspectAsync(SiteSqlConnection target, CancellationToken ct)
    {
        try
        {
            using var conn = OpenableMaster(target);
            await conn.OpenAsync(ct);

            int edition;
            using (var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)", conn))
                edition = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
            var isAzure = edition == 5; // 5 = Azure SQL Database.

            bool exists;
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n", conn))
            {
                cmd.Parameters.AddWithValue("@n", target.Database);
                exists = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0) > 0;
            }

            string? dbEdition = null, objective = null;
            if (exists && isAzure)
            {
                // Read the live tier so a recreated database can keep it (otherwise SqlPackage
                // creates the new DB at the subscription default).
                using var cmd = new SqlCommand(
                    "SELECT CAST(DATABASEPROPERTYEX(@n, 'Edition') AS nvarchar(128)), " +
                    "CAST(DATABASEPROPERTYEX(@n, 'ServiceObjective') AS nvarchar(128))", conn);
                cmd.Parameters.AddWithValue("@n", target.Database);
                using var rdr = await cmd.ExecuteReaderAsync(ct);
                if (await rdr.ReadAsync(ct))
                {
                    dbEdition = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                    objective = rdr.IsDBNull(1) ? null : rdr.GetString(1);
                }
            }

            return Result<RemoteDbInfo>.Ok(new RemoteDbInfo(isAzure, exists, dbEdition, objective));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Remote inspect failed");
            return Result<RemoteDbInfo>.Fail(ex.Message);
        }
    }

    public async Task<Result> DropDatabaseAsync(SiteSqlConnection target, bool isAzure, IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            using var conn = OpenableMaster(target);
            await conn.OpenAsync(ct);

            var bracket = target.Database.Replace("]", "]]");
            var literal = target.Database.Replace("'", "''");

            reporter.Step($"Dropping existing database [{target.Database}] on {target.Server}");

            // Azure SQL Database does not support SET SINGLE_USER; DROP DATABASE drops active
            // sessions itself. On-prem we force-close connections first so the drop can't block.
            var sql = isAzure
                ? $"DROP DATABASE IF EXISTS [{bracket}];"
                : $"IF DB_ID(N'{literal}') IS NOT NULL BEGIN " +
                  $"ALTER DATABASE [{bracket}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                  $"DROP DATABASE [{bracket}]; END";

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);

            reporter.Success("Existing database dropped.");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Remote drop failed");
            return Result.Fail(ex.Message);
        }
    }

    public async Task<Result> RenameDatabaseAsync(SiteSqlConnection target, string fromName, string toName,
        IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            using var conn = OpenableMaster(target);
            await conn.OpenAsync(ct);

            var from = fromName.Replace("]", "]]");
            var to = toName.Replace("]", "]]");

            reporter.Step($"Renaming [{fromName}] → [{toName}]");
            using var cmd = new SqlCommand($"ALTER DATABASE [{from}] MODIFY NAME = [{to}];", conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);

            reporter.Success($"Renamed to [{toName}].");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Remote rename failed");
            return Result.Fail(ex.Message);
        }
    }
}
