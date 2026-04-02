# Sybase ASE TLS Connectivity - .NET 10

.NET 10 web app that connects to Sybase ASE over TLS 1.2/1.3 using the SAP ODBC driver. Includes a dashboard UI to verify TLS connectivity.

## Quick Start (Corporate)

```bash
git clone https://github.com/gkkishan/sybase-docker-tls12.git
cd sybase-docker-tls12
```

**1. Get your CA certificate and place it:**

```bash
# Grab from the server directly
openssl s_client -connect YOUR_SYBASE_HOST:YOUR_PORT < /dev/null 2>/dev/null \
  | openssl x509 -outform PEM > certs/corporate-ca.crt
```

**2. Edit `SybaseTlsClient/appsettings.Production.json`:**

```json
{
  "Sybase": {
    "ConnectionString": "Driver={Sybase ODBC Driver};Server=YOUR_SYBASE_HOST;Port=YOUR_PORT;Database=YOUR_DB;UID=YOUR_USER;PWD=YOUR_PASSWORD;Encryption=ssl;TrustedFile=/opt/sap/certs/ca.crt;",
    "CaCertPath": "/opt/sap/certs/ca.crt"
  }
}
```

Do not change `TrustedFile` or `CaCertPath` — those are container-internal paths.

**3. Run:**

```bash
# If corp proxy blocks image pulls:
bash run-corp.sh pull

# Start
bash run-corp.sh up

# Other commands
bash run-corp.sh down      # Stop
bash run-corp.sh logs      # View logs
bash run-corp.sh status    # Check status
```

**4. Open http://localhost:8080** and click "Run TLS & Database Check".

## Configuration

All config is in `SybaseTlsClient/appsettings.Production.json`. Two keys:

| Key | What to set |
|-----|-------------|
| `Sybase:ConnectionString` | Your Sybase server, port, database, credentials. `Encryption=ssl` enables TLS. |
| `Sybase:CaCertPath` | Don't change. Already set to `/opt/sap/certs/ca.crt`. |

## Local Dev (optional)

Runs a local Sybase ASE container for testing without a corporate server.

```bash
bash certs/gen-certs.sh
docker compose up --build -d
```

## Technical Details

See [ARCHITECTURE.md](ARCHITECTURE.md).
