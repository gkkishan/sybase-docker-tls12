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

---

## Corporate / Production Setup

### Prerequisites
- Podman or Docker
- Your corporate Sybase server hostname and port
- Your corporate CA certificate file (`.crt` or `.pem`)
- Database credentials

### Steps

**1. Place your corporate CA certificate**

```bash
cp /path/to/your/corporate-ca.crt certs/corporate-ca.crt
```

**2. Edit `compose.corp.yml`** — replace these 5 values:

```yaml
environment:
  - SYBASE_CONNECTION_STRING=Driver={Sybase ODBC Driver};Server=YOUR_SYBASE_HOST;Port=YOUR_PORT;Database=YOUR_DB;UID=YOUR_USER;PWD=YOUR_PASSWORD;Encryption=ssl;TrustedFile=/opt/sap/certs/ca.crt;
  - SYBASE_TLS_HOST=YOUR_SYBASE_HOST
  - SYBASE_TLS_PORT=YOUR_PORT
```

| Placeholder | Example | Description |
|-------------|---------|-------------|
| `YOUR_SYBASE_HOST` | `sybase-prod.corp.local` | Sybase server hostname |
| `YOUR_PORT` | `5000` | Sybase TLS port |
| `YOUR_DB` | `framework` | Target database name |
| `YOUR_USER` | `sa` | Database user |
| `YOUR_PASSWORD` | `SecurePass123` | Database password |

**3. Build and run**

```bash
# Docker
docker compose -f compose.corp.yml up --build -d

# Podman
podman-compose -f compose.corp.yml up --build -d
```

**4. Open the dashboard**

http://localhost:8080

Click **"Run TLS & Database Check"** to verify connectivity.

**5. API endpoint**

```
GET http://localhost:8080/api/tls-check
```

Returns JSON with TLS protocol, cipher, cert validation, and database status.

---

## What the Dashboard Checks

| Check | What it verifies |
|-------|-----------------|
| TLS Handshake | Can the app establish a TLS connection to the server |
| Protocol | TLS 1.2 or TLS 1.3 is negotiated |
| Cipher Suite | Which cipher was selected |
| Cert Validated | Server certificate is trusted against your CA cert |
| TLS 1.0 Blocked | Server rejects TLS 1.0 connections |
| TLS 1.1 Blocked | Server rejects TLS 1.1 connections |
| ODBC Connection | Can the app query the database over TLS |

---

## Environment Variables Reference

| Variable | Required | Description |
|----------|----------|-------------|
| `SYBASE_CONNECTION_STRING` | Yes | ODBC connection string with `Encryption=ssl;TrustedFile=<path>;` |
| `SYBASE_TLS_HOST` | Yes | Sybase server hostname (for TLS handshake verification) |
| `SYBASE_TLS_PORT` | No | Sybase TLS port (default: `5000`) |
| `SYBASE_CA_CERT` | Yes | Path to CA certificate inside container (for cert validation) |

---

## File Structure

```
SybaseTlsClient/
  Program.cs            # .NET 10 web app (API + dashboard UI)
  Dockerfile            # Container build (aspnet + SAP ODBC libs)
  SybaseTlsClient.csproj
  sap-odbc/
    lib/                # SAP ODBC driver + crypto libs
      libsybdrvodb.so     # SAP Sybase ODBC driver
      libsapcrypto.so     # SAP crypto (handles TLS)
      libsapssfs.so       # SAP secure storage
      libslcryptokernel.so
      locales/            # Driver locale files
    dm-lib/             # ODBC driver manager libs
compose.corp.yml        # Corporate use (point to your Sybase server)
compose.yml             # Local dev (includes local Sybase container)
certs/                  # Place your CA cert here
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

- The SAP ODBC `.so` files are x86_64 Linux binaries. On Apple Silicon (M1/M2/M3), add `--platform=linux/amd64` to the Dockerfile `FROM` lines. On GKE/x86 servers this is not needed.
- The `Encryption=ssl` in the connection string enables TLS at the ODBC driver level.
- The `TrustedFile` parameter points to the CA certificate used to validate the server.
- `SYBASE_CA_CERT` is used independently by the dashboard's TLS verification (separate from the ODBC connection).
