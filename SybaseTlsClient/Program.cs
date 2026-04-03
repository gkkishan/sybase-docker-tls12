using SybaseTlsClient.Services;
using SybaseTlsClient.UI;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Load configuration
var config = app.Configuration.GetSection("Sybase");
var connectionString = config["ConnectionString"]
    ?? throw new InvalidOperationException("Sybase:ConnectionString is not configured");
var caCertPath = config["CaCertPath"]
    ?? throw new InvalidOperationException("Sybase:CaCertPath is not configured");

// Parse server and port from connection string
var connParts = connectionString.Split(';')
    .Select(p => p.Trim().Split('=', 2))
    .Where(p => p.Length == 2)
    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

var sybaseHost = connParts.GetValueOrDefault("Server")
    ?? throw new InvalidOperationException("Server not found in ConnectionString");
var sybasePort = int.Parse(connParts.GetValueOrDefault("Port") ?? "5000");

// Initialize services
var tlsChecker = new TlsChecker();
var databaseService = new DatabaseService(connectionString);

// Routes
app.MapGet("/", () => Results.Content(DashboardHtml.GetHtml(), "text/html"));

app.MapGet("/api/tls-check", async () =>
{
    var tlsResult = await tlsChecker.CheckTlsAsync(sybaseHost, sybasePort, caCertPath);
    var dbResult = databaseService.CheckDatabase();
    return Results.Json(new { tls = tlsResult, db = dbResult });
});

app.MapGet("/api/query", (string table, int? maxRows) =>
{
    var result = databaseService.ExecuteQuery(table, maxRows ?? 50);
    return Results.Json(result);
});

app.Run();
