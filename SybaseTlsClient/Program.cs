using System.Data;
using System.Data.Odbc;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Config precedence: environment variable > appsettings.{Environment}.json > appsettings.json
var config = app.Configuration.GetSection("Sybase");

var connectionString = config["ConnectionString"]
    ?? throw new InvalidOperationException("Sybase:ConnectionString is not configured");

var caCertPath = config["CaCertPath"]
    ?? throw new InvalidOperationException("Sybase:CaCertPath is not configured");

// Parse Server and Port from the connection string so they aren't duplicated
var connParts = connectionString.Split(';')
    .Select(p => p.Trim().Split('=', 2))
    .Where(p => p.Length == 2)
    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

var sybaseHost = connParts.GetValueOrDefault("Server")
    ?? throw new InvalidOperationException("Server not found in ConnectionString");

var sybasePort = int.Parse(connParts.GetValueOrDefault("Port") ?? "5000");

app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html>
<head>
    <title>Sybase TLS Connectivity Dashboard</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #e2e8f0; min-height: 100vh; padding: 2rem; }
        h1 { text-align: center; font-size: 1.8rem; margin-bottom: 0.5rem; color: #38bdf8; }
        .subtitle { text-align: center; color: #64748b; margin-bottom: 2rem; font-size: 0.9rem; }
        .container { max-width: 900px; margin: 0 auto; }
        .card { background: #1e293b; border-radius: 12px; padding: 1.5rem; margin-bottom: 1.5rem; border: 1px solid #334155; }
        .card h2 { font-size: 1.1rem; color: #94a3b8; margin-bottom: 1rem; text-transform: uppercase; letter-spacing: 0.05em; }
        .status { display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem 1rem; border-radius: 8px; margin-bottom: 0.5rem; }
        .status.pass { background: #064e3b; border: 1px solid #059669; }
        .status.fail { background: #7f1d1d; border: 1px solid #dc2626; }
        .status.info { background: #1e3a5f; border: 1px solid #3b82f6; }
        .dot { width: 12px; height: 12px; border-radius: 50%; flex-shrink: 0; }
        .pass .dot { background: #10b981; box-shadow: 0 0 8px #10b981; }
        .fail .dot { background: #ef4444; box-shadow: 0 0 8px #ef4444; }
        .info .dot { background: #3b82f6; box-shadow: 0 0 8px #3b82f6; }
        .label { font-weight: 600; min-width: 160px; }
        .value { color: #cbd5e1; font-family: 'SF Mono', 'Fira Code', monospace; font-size: 0.9rem; word-break: break-all; }
        .btn { background: #2563eb; color: white; border: none; padding: 0.75rem 2rem; border-radius: 8px; font-size: 1rem; cursor: pointer; display: block; margin: 1.5rem auto 0; transition: background 0.2s; }
        .btn:hover { background: #1d4ed8; }
        .btn:disabled { background: #475569; cursor: not-allowed; }
        .loading { text-align: center; color: #94a3b8; padding: 2rem; }
        .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; }
        @media (max-width: 700px) { .grid { grid-template-columns: 1fr; } }
        .timestamp { text-align: center; color: #64748b; font-size: 0.85rem; margin-top: 1rem; }
    </style>
</head>
<body>
    <div class="container">
        <h1>Sybase ASE - TLS Connectivity Dashboard</h1>
        <div class="subtitle">.NET 10 + SAP ODBC Driver (Native TLS)</div>
        <div id="content"><div class="loading">Click the button below to run diagnostics...</div></div>
        <button class="btn" id="runBtn" onclick="runCheck()">Run TLS & Database Check</button>
        <div class="timestamp" id="ts"></div>

        <div class="card" style="margin-top:2rem">
            <h2>Query Table</h2>
            <div style="display:flex;gap:0.5rem;margin-bottom:1rem">
                <input id="tbl" type="text" placeholder="e.g. master..sysobjects" style="flex:1;padding:0.6rem 1rem;border-radius:8px;border:1px solid #334155;background:#0f172a;color:#e2e8f0;font-family:monospace;font-size:0.95rem" />
                <input id="maxRows" type="number" value="50" min="1" max="500" style="width:80px;padding:0.6rem;border-radius:8px;border:1px solid #334155;background:#0f172a;color:#e2e8f0;text-align:center" title="Max rows" />
                <button class="btn" style="margin:0;padding:0.6rem 1.5rem" onclick="queryTable()">Query</button>
            </div>
            <div id="qresult"></div>
        </div>
    </div>
    <script>
        async function runCheck() {
            const btn = document.getElementById('runBtn');
            const content = document.getElementById('content');
            btn.disabled = true;
            btn.textContent = 'Running checks...';
            content.innerHTML = '<div class="loading">Connecting to Sybase ASE via TLS...</div>';
            try {
                const res = await fetch('/api/tls-check');
                const data = await res.json();
                let html = '<div class="grid">';
                html += '<div class="card"><h2>TLS Verification</h2>';
                html += row(data.tls.connected, 'TLS Handshake', data.tls.connected ? 'Success' : 'Failed');
                const tlsOk = data.tls.protocol === 'Tls12' || data.tls.protocol === 'Tls13';
                html += row(tlsOk, 'Protocol', data.tls.protocol || 'N/A');
                html += row(!!data.tls.cipher, 'Cipher Suite', data.tls.cipher || 'N/A');
                html += row(!!data.tls.certificate, 'Server Cert CN', data.tls.certificate || 'N/A');
                html += row(data.tls.certValid, 'Cert Validated', data.tls.certValid ? 'Trusted (against CA cert)' : 'NOT validated');
                html += row(data.tls.tlsv10Rejected, 'TLS 1.0', data.tls.tlsv10Rejected ? 'Blocked' : 'ALLOWED (!)');
                html += row(data.tls.tlsv11Rejected, 'TLS 1.1', data.tls.tlsv11Rejected ? 'Blocked' : 'ALLOWED (!)');
                if (data.tls.error) html += row(false, 'Error', data.tls.error);
                html += '</div>';
                html += '<div class="card"><h2>Database Connection</h2>';
                html += row(data.db.connected, 'ODBC Connection', data.db.connected ? 'Connected' : 'Failed');
                html += rowInfo('Driver', data.db.driver || 'N/A');
                html += rowInfo('Server Name', data.db.serverName || 'N/A');
                html += rowInfo('ASE Version', data.db.version || 'N/A');
                html += rowInfo('Database', data.db.database || 'N/A');
                if (data.db.databases) html += rowInfo('All Databases', data.db.databases.join(', '));
                if (data.db.error) html += row(false, 'Error', data.db.error);
                html += '</div></div>';
                const allGood = data.tls.connected && tlsOk && data.db.connected && data.tls.certValid && data.tls.tlsv10Rejected && data.tls.tlsv11Rejected;
                html += `<div class="card"><div class="status ${allGood ? 'pass' : 'fail'}"><div class="dot"></div><span class="label">Overall Result</span><span class="value">${allGood ? 'TLS end-to-end connectivity VERIFIED' : 'Issues detected - review details above'}</span></div></div>`;
                content.innerHTML = html;
                document.getElementById('ts').textContent = 'Last checked: ' + new Date().toLocaleString();
            } catch (e) {
                content.innerHTML = `<div class="card"><div class="status fail"><div class="dot"></div><span class="value">Error: ${e.message}</span></div></div>`;
            }
            btn.disabled = false;
            btn.textContent = 'Run TLS & Database Check';
        }
        function row(ok, label, value) {
            return `<div class="status ${ok ? 'pass' : 'fail'}"><div class="dot"></div><span class="label">${label}</span><span class="value">${value}</span></div>`;
        }
        function rowInfo(label, value) {
            return `<div class="status info"><div class="dot"></div><span class="label">${label}</span><span class="value">${value}</span></div>`;
        }
        async function queryTable() {
            const tbl = document.getElementById('tbl').value.trim();
            const max = document.getElementById('maxRows').value || 50;
            const el = document.getElementById('qresult');
            if (!tbl) { el.innerHTML = '<div class="status fail"><div class="dot"></div><span class="value">Enter a table name</span></div>'; return; }
            el.innerHTML = '<div class="loading">Querying...</div>';
            try {
                const res = await fetch(`/api/query?table=${encodeURIComponent(tbl)}&maxRows=${max}`);
                const data = await res.json();
                if (data.error) { el.innerHTML = `<div class="status fail"><div class="dot"></div><span class="value">${data.error}</span></div>`; return; }
                if (!data.rows || data.rows.length === 0) { el.innerHTML = '<div class="status info"><div class="dot"></div><span class="value">No rows returned</span></div>'; return; }
                let h = `<div style="color:#64748b;font-size:0.85rem;margin-bottom:0.5rem">${data.rowCount} row(s) returned</div>`;
                h += '<div style="overflow-x:auto"><table style="width:100%;border-collapse:collapse;font-size:0.85rem;font-family:monospace">';
                h += '<tr>' + data.columns.map(c => `<th style="text-align:left;padding:0.5rem 0.75rem;border-bottom:2px solid #334155;color:#94a3b8;white-space:nowrap">${c}</th>`).join('') + '</tr>';
                data.rows.forEach((r, i) => {
                    const bg = i % 2 === 0 ? '#1e293b' : '#0f172a';
                    h += '<tr>' + data.columns.map(c => `<td style="padding:0.4rem 0.75rem;border-bottom:1px solid #1e293b;background:${bg};white-space:nowrap">${r[c] ?? ''}</td>`).join('') + '</tr>';
                });
                h += '</table></div>';
                el.innerHTML = h;
            } catch (e) { el.innerHTML = `<div class="status fail"><div class="dot"></div><span class="value">Error: ${e.message}</span></div>`; }
        }
    </script>
</body>
</html>
""", "text/html"));

app.MapGet("/api/tls-check", async () =>
{
    var tlsResult = await CheckTls(sybaseHost, sybasePort, caCertPath);
    var dbResult = CheckDatabase(connectionString);
    return Results.Json(new { tls = tlsResult, db = dbResult });
});

app.MapGet("/api/query", (string table, int? maxRows) =>
{
    if (string.IsNullOrWhiteSpace(table))
        return Results.Json(new { error = "table parameter is required" });

    // Basic validation — only allow alphanumeric, dots, underscores
    if (!System.Text.RegularExpressions.Regex.IsMatch(table, @"^[\w.]+$"))
        return Results.Json(new { error = "Invalid table name" });

    var limit = Math.Clamp(maxRows ?? 50, 1, 500);

    try
    {
        using var connection = new OdbcConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET ROWCOUNT {limit} SELECT * FROM {table}";

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

        return Results.Json(new { columns, rows, rowCount = rows.Count, error = (string?)null });
    }
    catch (Exception ex)
    {
        return Results.Json(new { columns = (List<string>?)null, rows = (List<Dictionary<string, object?>>?)null, rowCount = 0, error = ex.Message });
    }
});

app.Run();

static async Task<object> CheckTls(string host, int port, string caCertPath)
{
    bool connected = false;
    string? protocol = null;
    string? cipher = null;
    string? certificate = null;
    bool certValid = false;
    bool tlsv10Rejected = false;
    bool tlsv11Rejected = false;
    string? error = null;

    var caCert = X509CertificateLoader.LoadCertificateFromFile(caCertPath);

    bool ValidateCert(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (cert == null) return false;
        var cert2 = new X509Certificate2(cert);
        return cert2.Thumbprint == caCert.Thumbprint
            || (chain != null && chain.Build(cert2));
    }

    // Test TLS 1.2 or 1.3 (whichever the server supports)
    try
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);
        using var ssl = new SslStream(tcp.GetStream(), false, ValidateCert);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { caCert }
            }
        });
        connected = true;
        certValid = true;
        protocol = ssl.SslProtocol.ToString();
        cipher = ssl.NegotiatedCipherSuite.ToString();
        certificate = ssl.RemoteCertificate is X509Certificate2 c2
            ? c2.GetNameInfo(X509NameType.SimpleName, false)
            : ssl.RemoteCertificate?.Subject;
    }
    catch (Exception ex) { error = ex.Message; }

    // Verify TLS 1.0 is rejected
    try
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);
        using var ssl = new SslStream(tcp.GetStream(), false, ValidateCert);
#pragma warning disable SYSLIB0039
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host, EnabledSslProtocols = SslProtocols.Tls
        });
#pragma warning restore SYSLIB0039
    }
    catch { tlsv10Rejected = true; }

    // Verify TLS 1.1 is rejected
    try
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);
        using var ssl = new SslStream(tcp.GetStream(), false, ValidateCert);
#pragma warning disable SYSLIB0039
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host, EnabledSslProtocols = SslProtocols.Tls11
        });
#pragma warning restore SYSLIB0039
    }
    catch { tlsv11Rejected = true; }

    return new { connected, protocol, cipher, certificate, certValid, tlsv10Rejected, tlsv11Rejected, error };
}

static object CheckDatabase(string connStr)
{
    try
    {
        using var connection = new OdbcConnection(connStr);
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

        return new { connected = true, driver = "SAP Sybase ODBC (native TLS)", serverName, version, database, databases, error = (string?)null };
    }
    catch (Exception ex)
    {
        return new { connected = false, driver = "SAP Sybase ODBC (native TLS)", serverName = (string?)null, version = (string?)null, database = (string?)null, databases = (List<string>?)null, error = ex.Message };
    }
}
