using System.Data.Odbc;

namespace SybaseTlsClient.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DatabaseCheckResult CheckDatabase()
    {
        try
        {
            using var connection = new OdbcConnection(_connectionString);
            connection.Open();

            string? serverName = null, version = null, database = null;
            var databases = new List<string>();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT @@servername AS ServerName, @@version AS Version, db_name() AS CurrentDB";
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    serverName = reader["ServerName"]?.ToString();
                    version = reader["Version"]?.ToString()?.Split('\n')[0]?.Trim();
                    database = reader["CurrentDB"]?.ToString();
                }
            }

            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "SELECT name FROM master..sysdatabases ORDER BY name";
            using (var reader2 = cmd2.ExecuteReader())
            {
                while (reader2.Read())
                    databases.Add(reader2["name"]?.ToString() ?? "");
            }

            return new DatabaseCheckResult
            {
                Connected = true,
                Driver = "SAP Sybase ODBC (native TLS)",
                ServerName = serverName,
                Version = version,
                Database = database,
                Databases = databases,
                Error = null
            };
        }
        catch (Exception ex)
        {
            return new DatabaseCheckResult
            {
                Connected = false,
                Driver = "SAP Sybase ODBC (native TLS)",
                Error = ex.Message
            };
        }
    }

    public QueryResult ExecuteQuery(string tableName, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return new QueryResult { Error = "table parameter is required" };

        // Allow owner.table, db..table, db.owner.table formats
        if (!System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[\w]+([.][\w]*)*$"))
            return new QueryResult { Error = "Invalid table name" };

        var limit = Math.Clamp(maxRows, 1, 500);

        try
        {
            using var connection = new OdbcConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET ROWCOUNT {limit} SELECT * FROM {tableName}";

            using var reader = cmd.ExecuteReader();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            var rows = new List<Dictionary<string, object?>>();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var val = reader[col];
                    row[col] = val == DBNull.Value ? null : val?.ToString();
                }
                rows.Add(row);
            }

            return new QueryResult
            {
                Columns = columns,
                Rows = rows,
                RowCount = rows.Count,
                Error = null
            };
        }
        catch (Exception ex)
        {
            return new QueryResult { Error = ex.Message };
        }
    }
}

public record DatabaseCheckResult
{
    public bool Connected { get; init; }
    public string Driver { get; init; } = string.Empty;
    public string? ServerName { get; init; }
    public string? Version { get; init; }
    public string? Database { get; init; }
    public List<string>? Databases { get; init; }
    public string? Error { get; init; }
}

public record QueryResult
{
    public List<string>? Columns { get; init; }
    public List<Dictionary<string, object?>>? Rows { get; init; }
    public int RowCount { get; init; }
    public string? Error { get; init; }
}
