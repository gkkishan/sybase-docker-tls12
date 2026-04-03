namespace SybaseTlsClient.UI;

public static class DashboardHtml
{
    public static string GetHtml() => """
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
        .input-group { margin-bottom: 1rem; }
        .input-group label { display: block; margin-bottom: 0.5rem; color: #94a3b8; font-size: 0.9rem; }
        .input-group input { width: 100%; padding: 0.75rem; background: #0f172a; border: 1px solid #334155; border-radius: 8px; color: #e2e8f0; font-size: 1rem; }
        .input-group input:focus { outline: none; border-color: #3b82f6; }
        table { width: 100%; border-collapse: collapse; margin-top: 1rem; }
        th { background: #0f172a; padding: 0.75rem; text-align: left; border-bottom: 2px solid #334155; color: #94a3b8; font-size: 0.85rem; text-transform: uppercase; }
        td { padding: 0.75rem; border-bottom: 1px solid #334155; color: #cbd5e1; font-size: 0.9rem; }
        tr:hover { background: #0f172a; }
        .table-container { overflow-x: auto; max-height: 500px; overflow-y: auto; }
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
            <div class="input-group">
                <label for="tableName">Table Name (e.g., master..sysdatabases, sysusers)</label>
                <input type="text" id="tableName" placeholder="Enter table name">
            </div>
            <div class="input-group">
                <label for="maxRows">Max Rows (1-500)</label>
                <input type="number" id="maxRows" value="50" min="1" max="500">
            </div>
            <button class="btn" onclick="queryTable()">Execute Query</button>
            <div id="queryResult"></div>
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
                html += rowInfo('TLS 1.0', data.tls.tlsv10Rejected ? 'Blocked' : 'Allowed');
                html += rowInfo('TLS 1.1', data.tls.tlsv11Rejected ? 'Blocked' : 'Allowed');
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
                const allGood = data.tls.connected && tlsOk && data.db.connected && data.tls.certValid;
                html += `<div class="card"><div class="status ${allGood ? 'pass' : 'fail'}"><div class="dot"></div><span class="label">Overall Result</span><span class="value">${allGood ? 'TLS end-to-end connectivity VERIFIED' : 'Issues detected - review details above'}</span></div></div>`;
                content.innerHTML = html;
                document.getElementById('ts').textContent = 'Last checked: ' + new Date().toLocaleString();
            } catch (e) {
                content.innerHTML = `<div class="card"><div class="status fail"><div class="dot"></div><span class="value">Error: ${e.message}</span></div></div>`;
            }
            btn.disabled = false;
            btn.textContent = 'Run TLS & Database Check';
        }

        async function queryTable() {
            const tableName = document.getElementById('tableName').value;
            const maxRows = document.getElementById('maxRows').value;
            const resultDiv = document.getElementById('queryResult');
            
            if (!tableName) {
                resultDiv.innerHTML = '<div class="status fail" style="margin-top:1rem"><div class="dot"></div><span class="value">Please enter a table name</span></div>';
                return;
            }

            resultDiv.innerHTML = '<div class="loading">Executing query...</div>';
            
            try {
                const res = await fetch(`/api/query?table=${encodeURIComponent(tableName)}&maxRows=${maxRows}`);
                const data = await res.json();
                
                if (data.error) {
                    resultDiv.innerHTML = `<div class="status fail" style="margin-top:1rem"><div class="dot"></div><span class="value">Error: ${data.error}</span></div>`;
                    return;
                }

                let html = `<div class="status pass" style="margin-top:1rem"><div class="dot"></div><span class="label">Query Success</span><span class="value">${data.rowCount} rows returned</span></div>`;
                
                if (data.columns && data.rows && data.rows.length > 0) {
                    html += '<div class="table-container"><table><thead><tr>';
                    data.columns.forEach(col => html += `<th>${col}</th>`);
                    html += '</tr></thead><tbody>';
                    data.rows.forEach(row => {
                        html += '<tr>';
                        data.columns.forEach(col => html += `<td>${row[col] ?? 'NULL'}</td>`);
                        html += '</tr>';
                    });
                    html += '</tbody></table></div>';
                } else {
                    html += '<div class="status info" style="margin-top:0.5rem"><div class="dot"></div><span class="value">No rows returned</span></div>';
                }
                
                resultDiv.innerHTML = html;
            } catch (e) {
                resultDiv.innerHTML = `<div class="status fail" style="margin-top:1rem"><div class="dot"></div><span class="value">Error: ${e.message}</span></div>`;
            }
        }

        function row(ok, label, value) {
            return `<div class="status ${ok ? 'pass' : 'fail'}"><div class="dot"></div><span class="label">${label}</span><span class="value">${value}</span></div>`;
        }
        function rowInfo(label, value) {
            return `<div class="status info"><div class="dot"></div><span class="label">${label}</span><span class="value">${value}</span></div>`;
        }
    </script>
</body>
</html>
""";
}
