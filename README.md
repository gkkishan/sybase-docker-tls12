# Sybase ASE TLS Connectivity - .NET 10

.NET 10 web app that connects to Sybase ASE over TLS 1.2/1.3 using the SAP ODBC driver with native TLS. Includes a dashboard UI to verify TLS connectivity and database access.

## Architecture

```
.NET 10 App (SAP ODBC Driver) ---TLS 1.2/1.3---> Sybase ASE
        |                                            |
  libsybdrvodb.so                          DBA-managed server
  libsapcrypto.so                          (native TLS enabled)
```

No stunnel. No proxies. The SAP ODBC driver handles TLS natively.

For the full technical deep-dive, see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Corporate / Production Setup

### Prerequisites
- Podman or Docker
- Your corporate Sybase server hostname and port
- Your corporate CA certificate file (`.crt` or `.pem`)
- Database credentials

### Steps

**1. Clone the repo**

```bash
git clone https://github.com/gkkishan/sybase-docker-tls12.git
cd sybase-docker-tls12
```

**2. Get your CA certificate**

Ask your DBA/security team for the CA certificate that signed the Sybase server's TLS cert. Or grab it directly:

```bash
openssl s_client -connect YOUR_SYBASE_HOST:YOUR_PORT < /dev/null 2>/dev/null \
  | openssl x509 -outform PEM > certs/corporate-ca.crt
```

**3. Edit `SybaseTlsClient/appsettings.Production.json`**

Replace the placeholder values with your Sybase server details:

```json
{
  "Sybase": {
    "ConnectionString": "Driver={Sybase ODBC Driver};Server=YOUR_SYBASE_HOST;Port=YOUR_PORT;Database=YOUR_DB;UID=YOUR_USER;PWD=YOUR_PASSWORD;Encryption=ssl;TrustedFile=/opt/sap/certs/ca.crt;",
    "CaCertPath": "/opt/sap/certs/ca.crt"
  }
}
```

| Replace this | Example | Description |
|-------------|---------|-------------|
| `YOUR_SYBASE_HOST` | `rbm_sybase_dev01.silver01.aws.cloud.aim.local` | Sybase server hostname |
| `YOUR_PORT` | `5020` | Sybase TLS port |
| `YOUR_DB` | `framework` | Target database name |
| `YOUR_USER` | `sa` | Database user |
| `YOUR_PASSWORD` | `SecurePass123` | Database password |

Do **not** change `TrustedFile` or `CaCertPath` — those are paths inside the container, already wired up.

**4. Run**

```bash
bash run-corp.sh up
```

The script validates that `certs/corporate-ca.crt` exists and `appsettings.Production.json` has been edited before starting.

**5. Open the dashboard**

http://localhost:8080

Click **"Run TLS & Database Check"** to verify connectivity.

**6. Other commands**

```bash
bash run-corp.sh logs      # View logs
bash run-corp.sh status    # Check if running
bash run-corp.sh down      # Stop
```

---

## What the Dashboard Checks

| Check | What it verifies |
|-------|-----------------|
| TLS Handshake | App can establish a TLS connection to the server |
| Protocol | TLS 1.2 or TLS 1.3 is negotiated |
| Cipher Suite | Which cipher was selected |
| Cert Validated | Server certificate is trusted against your CA cert |
| TLS 1.0 Blocked | Server rejects TLS 1.0 connections |
| TLS 1.1 Blocked | Server rejects TLS 1.1 connections |
| ODBC Connection | App can query the database over TLS |

### API endpoint

```
GET http://localhost:8080/api/tls-check
```

Returns JSON with TLS protocol, cipher, cert validation, and database status.

---

## Configuration

All config is in `appsettings.{Environment}.json`. No env vars needed.

| Key | What it controls |
|-----|-----------------|
| `Sybase:ConnectionString` | ODBC connection string (Server, Port, Database, credentials, TLS settings) |
| `Sybase:CaCertPath` | Path to CA cert inside the container (don't change — already `/opt/sap/certs/ca.crt`) |

`Server` and `Port` for the TLS diagnostic endpoint are parsed from the connection string automatically — no duplication.

`ASPNETCORE_ENVIRONMENT` selects which appsettings file is loaded (`Development` for local, `Production` for corp).

---

## File Structure

```
SybaseTlsClient/
  Program.cs                      # .NET 10 web app (API + dashboard UI)
  Dockerfile                      # Container build (aspnet + SAP ODBC libs)
  SybaseTlsClient.csproj          # Project file
  appsettings.json                # Base config
  appsettings.Development.json    # Local dev config
  appsettings.Production.json     # Corporate config (edit this)
  sap-odbc/
    lib/                          # SAP ODBC driver + crypto libs
      libsybdrvodb.so               # SAP Sybase ODBC driver
      libsapcrypto.so               # SAP crypto (handles TLS)
      libsapssfs.so                 # SAP secure storage
      libslcryptokernel.so          # SSL crypto kernel
      locales/                      # Driver locale files
    dm-lib/                       # ODBC driver manager libs
certs/                            # Place corporate-ca.crt here
compose.corp.yml                  # Corporate deployment
compose.yml                       # Local dev (includes local Sybase container)
run-corp.sh                       # Corporate run script
ARCHITECTURE.md                   # Technical deep-dive
```

---

## Local Dev Setup (optional)

Spins up a local Sybase ASE container with stunnel TLS proxy for testing.

```bash
# Generate self-signed certs
bash certs/gen-certs.sh

# Start local Sybase + .NET app
docker compose up --build -d

# Dashboard at http://localhost:8080
```

---

## Notes

- The SAP ODBC `.so` files are x86_64 Linux binaries. On Apple Silicon (M1/M2/M3), `compose.override.yml` forces `linux/amd64` emulation. Delete it on x86_64 Linux / GKE.
- `Encryption=ssl` in the connection string enables TLS at the ODBC driver level.
- `TrustedFile` in the connection string and `CaCertPath` in appsettings must point to the same CA certificate. They are consumed by different components (SAP ODBC driver vs .NET SslStream diagnostic) but must agree on trust.
