# Technical Architecture: .NET 10 to Sybase ASE over TLS 1.2/1.3

## 1. Problem Statement

Connect a .NET 10 application running in a Linux container to a SAP Sybase ASE database server that enforces TLS 1.2 or TLS 1.3. The solution must:

- Use the official SAP ODBC driver (not open-source alternatives) for TDS protocol and native TLS
- Validate the server certificate against a trusted CA — no certificate bypass
- Reject connections on deprecated protocols (TLS 1.0, TLS 1.1)
- Provide a runtime diagnostic endpoint to verify TLS status
- Run on Kubernetes (GKE) with Podman/Docker locally

## 2. Architecture Overview

### Production (Corporate Network)

```
                         TLS 1.2/1.3
  +------------------+   (encrypted)    +------------------+
  | .NET 10 App      |----------------->| Sybase ASE 16.x  |
  | (Linux container)|   Port 5000      | (DBA-managed)    |
  |                  |                  |                  |
  |  System.Data.Odbc|                  |  ASE_ASM license |
  |       |          |                  |  Native TLS      |
  |  libsybdrvodb.so |                  +------------------+
  |  libsapcrypto.so |
  +------------------+
```

There are no TLS proxies, sidecars, or middleware. The SAP ODBC driver (`libsybdrvodb.so`) performs the TLS handshake directly using SAP's crypto library (`libsapcrypto.so`).

### Local Development

```
  +------------------+   TLS 1.2        +----------------------------+
  | .NET 10 App      |----------------->| stunnel (TLS termination)  |
  | (Linux container)|   Port 5000      |   Port 5000 -> 5100       |
  |                  |                  +----------------------------+
  |  libsybdrvodb.so |                         |
  |  libsapcrypto.so |                    Raw TDS (unencrypted)
  +------------------+                         |
                                        +----------------------------+
                                        | Sybase ASE 16.0 SP03 (XE)  |
                                        |   Port 5100                |
                                        |   No ASE_ASM license       |
                                        +----------------------------+
```

The ASE Express Edition lacks the `ASE_ASM` license required for native TLS. stunnel provides TLS termination on the server side. The .NET app uses the same SAP ODBC driver and connection string in both environments — it connects to a TLS-enabled port regardless of what terminates the TLS on the server side.

## 3. Component Details

### 3.1 SAP ODBC Driver Stack

The container image bundles these SAP-proprietary Linux shared libraries:

```
/opt/sap/DataAccess64/ODBC/
  lib/
    libsybdrvodb.so        # SAP Sybase ODBC driver (3.4 MB)
                           #   - Implements TDS 5.0 protocol
                           #   - Handles ODBC API surface
                           #   - Delegates TLS to libsapcrypto
    libsapcrypto.so        # SAP CommonCryptoLib (5.4 MB)
                           #   - TLS 1.2 and TLS 1.3 handshake
                           #   - Certificate validation
                           #   - Cipher suite negotiation
    libsapssfs.so          # SAP Secure File Storage (5.2 MB)
                           #   - Secure credential storage
    libslcryptokernel.so   # SSL crypto kernel (488 KB)
                           #   - Low-level crypto primitives
    locales/               # Locale files for error messages
  dm/lib64/
    libdbodm16.so          # SAP ODBC Driver Manager
    libdbicu16.so          # ICU unicode library
    libdbicudt16.so        # ICU data
    libdbtasks16.so        # Task management
    libodbc.so             # ODBC interface
```

These are x86_64 ELF binaries compiled for Linux. On Apple Silicon (ARM64), the container must run under `platform: linux/amd64` emulation. On GKE (x86_64 nodes), they run natively.

### 3.2 ODBC Registration

The driver is registered at build time in `/etc/odbcinst.ini`:

```ini
[Sybase ODBC Driver]
Description=SAP Sybase ODBC Driver
Driver=/opt/sap/DataAccess64/ODBC/lib/libsybdrvodb.so
FileUsage=1
```

The runtime linker finds dependent `.so` files via `LD_LIBRARY_PATH`.

### 3.3 Connection String Anatomy

```
Driver={Sybase ODBC Driver};
Server=sybase-host;            # Sybase server hostname
Port=5000;                     # TLS-enabled port
Database=master;               # Target database
UID=sa;                        # Database user
PWD=password;                  # Database password
Encryption=ssl;                # << Enables TLS in the ODBC driver
TrustedFile=/opt/sap/certs/ca.crt;  # << CA cert for server validation
```

Key parameters for TLS:

| Parameter | Purpose |
|-----------|---------|
| `Encryption=ssl` | Tells `libsybdrvodb.so` to perform a TLS handshake before sending TDS packets. Without this, the driver connects in plaintext. |
| `TrustedFile=<path>` | Path to the CA certificate (PEM format) used to validate the server's certificate. The driver passes this to `libsapcrypto.so` which performs X.509 chain validation. |

### 3.4 TLS Handshake Flow

```
.NET App                  SAP ODBC Driver              Sybase ASE
   |                           |                           |
   | OdbcConnection.Open()     |                           |
   |-------------------------->|                           |
   |                           | TCP connect               |
   |                           |-------------------------->|
   |                           |                           |
   |                           | ClientHello (TLS 1.2/1.3) |
   |                           |-------------------------->|
   |                           |                           |
   |                           | ServerHello + Certificate  |
   |                           |<--------------------------|
   |                           |                           |
   |                           | Validate cert against      |
   |                           | TrustedFile CA cert        |
   |                           |                           |
   |                           | Key Exchange + Finished    |
   |                           |<------------------------->|
   |                           |                           |
   |                           | TDS Login (encrypted)     |
   |                           |-------------------------->|
   |                           |                           |
   |                           | TDS Login Ack (encrypted) |
   |                           |<--------------------------|
   |                           |                           |
   | Connection established    |                           |
   |<--------------------------|                           |
```

The TLS negotiation is handled entirely by `libsapcrypto.so` inside `libsybdrvodb.so`. The .NET runtime (`System.Data.Odbc`) is unaware of TLS — it delegates to the native ODBC driver via P/Invoke.

### 3.5 Certificate Validation (Two Independent Paths)

The application validates certificates in two separate places:

**Path 1: ODBC Driver (database connection)**
- `libsapcrypto.so` validates the server cert against `TrustedFile`
- This is the actual database connection
- Happens inside native code, opaque to .NET

**Path 2: .NET SslStream (diagnostic endpoint)**
- `Program.cs:CheckTls()` opens a raw TCP+TLS connection using `System.Net.Security.SslStream`
- Validates the server cert against the CA cert loaded via `X509CertificateLoader.LoadCertificateFromFile()`
- Uses `X509ChainTrustMode.CustomRootTrust` — only the explicitly provided CA cert is trusted
- This is the `/api/tls-check` endpoint, used for diagnostics only

```csharp
// Custom root trust — does NOT use system certificate store
CertificateChainPolicy = new X509ChainPolicy
{
    TrustMode = X509ChainTrustMode.CustomRootTrust,
    CustomTrustStore = { caCert }  // Only this CA is trusted
}
```

Both paths require the same CA certificate. Neither path skips validation.

## 4. Container Image Layers

```dockerfile
# Build stage: .NET SDK compiles the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
  -> dotnet restore + publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
  -> apt-get install unixodbc          # ODBC driver manager (system)
  -> COPY sap-odbc/lib/               # SAP native TLS + ODBC libs
  -> COPY sap-odbc/dm-lib/            # SAP ODBC driver manager libs
  -> Register driver in odbcinst.ini
  -> Set LD_LIBRARY_PATH
  -> COPY published .NET app
```

Final image size: ~280 MB (aspnet base ~220 MB + SAP libs ~20 MB + app ~40 MB).

## 5. Configuration

All Sybase connection settings are in `appsettings.json` with per-environment overrides:

```
appsettings.json                 # Base config (defaults)
appsettings.Development.json     # Local dev overrides
appsettings.Production.json      # Production overrides (edit this)
```

### Configuration Keys

| Key | Location | Description |
|-----|----------|-------------|
| `Sybase:ConnectionString` | appsettings.json | ODBC connection string with `Encryption=ssl` and `TrustedFile` |
| `Sybase:CaCertPath` | appsettings.json | Path to CA certificate inside the container |

`Server` and `Port` for the TLS diagnostic endpoint are parsed directly from `ConnectionString` — no duplication.

### Override Precedence

```
1. appsettings.{Environment}.json (Sybase:ConnectionString)   <-- highest
2. appsettings.json (Sybase:ConnectionString)                  <-- lowest
```

`ASPNETCORE_ENVIRONMENT` controls which file is loaded (`Development`, `Production`, etc.).

Note: `Sybase:CaCertPath` and `TrustedFile` in the connection string should point to the same CA certificate file. They are consumed by different components (SslStream vs libsapcrypto) but must agree on trust.

## 6. API Endpoints

### `GET /`

Serves the TLS Connectivity Dashboard (single-page HTML/JS app). No server-side rendering.

### `GET /api/tls-check`

Performs live TLS and database diagnostics. Returns:

```json
{
  "tls": {
    "connected": true,
    "protocol": "Tls12",
    "cipher": "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
    "certificate": "sybase-server",
    "certValid": true,
    "tlsv10Rejected": true,
    "tlsv11Rejected": true,
    "error": null
  },
  "db": {
    "connected": true,
    "driver": "SAP Sybase ODBC (native TLS)",
    "serverName": "DB_TEST",
    "version": "Adaptive Server Enterprise/16.0 SP03 ...",
    "database": "master",
    "databases": ["master", "model", "sybsystemdb", "sybsystemprocs", "tempdb"],
    "error": null
  }
}
```

The TLS check performs three separate `SslStream` connections:
1. TLS 1.2 + 1.3 (should succeed) — reports protocol, cipher, cert CN
2. TLS 1.0 only (should fail) — confirms deprecated protocol is blocked
3. TLS 1.1 only (should fail) — confirms deprecated protocol is blocked

## 7. Deployment

### 7.1 Corporate / GKE

```bash
# 1. Place your CA cert
cp /path/to/corporate-ca.crt certs/corporate-ca.crt

# 2. Edit compose.corp.yml with your Sybase connection details

# 3. Run
podman-compose -f compose.corp.yml up --build -d

# 4. Verify
curl http://localhost:8080/api/tls-check
```

### 7.2 Local Development (Apple Silicon)

```bash
# compose.override.yml forces linux/amd64 for SAP's x86_64 .so files
docker compose up --build -d

# Delete compose.override.yml on x86_64 Linux
```

## 8. Why Not Other Approaches

| Approach | Why not |
|----------|---------|
| `AdoNetCore.AseClient` (open source) | No TLS support. Last release Feb 2021. Unmaintained. |
| `Sybase.AdoNet45.AseClient.dll` (SAP managed DLL) | .NET Framework 4.5 only. Embeds Windows-only native code (`sybdrvado20.dll`). Fails with `TypeInitializationException` on .NET 10 / Linux. |
| stunnel as app sidecar | Adds operational complexity. Extra process to manage. Connection string still points to localhost — obscures the actual TLS endpoint in monitoring. |
| ASE native TLS (local dev) | ASE Express Edition lacks `ASE_ASM` license. `sp_configure "enable ssl", 1` is accepted but ignored at startup: `No license! Cannot enable SSL feature.` |
| System.Data.Odbc + unixODBC + FreeTDS | FreeTDS supports TLS but has inconsistent Sybase compatibility. SAP driver is the supported path. |

## 9. Security Considerations

- No certificate validation bypass anywhere in the codebase
- CA certificate is mounted read-only (`:ro`) into the container
- Database credentials are passed via environment variables (use Kubernetes Secrets in GKE)
- The diagnostic endpoint (`/api/tls-check`) exposes server version and database names — restrict access in production via network policy or authentication
- SAP ODBC `.so` files are proprietary — do not publish to public registries. Use a private container registry.

## 10. Verified Configuration

Tested and confirmed working:

| Component | Version |
|-----------|---------|
| .NET | 10.0 |
| ASP.NET Core | 10.0 |
| SAP ASE | 16.0 SP03 PL02 |
| SAP ODBC Driver | libsybdrvodb.so (from SDK Suite) |
| System.Data.Odbc | 9.0.4 (NuGet) |
| Container base | mcr.microsoft.com/dotnet/aspnet:10.0 |
| unixODBC | System package (Debian) |
| TLS Protocol | TLS 1.2 (TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384) |
| TLS 1.0 | Rejected |
| TLS 1.1 | Rejected |
