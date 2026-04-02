# Technical Architecture: .NET 10 to Sybase ASE over TLS

## Problem

Connect a .NET 10 containerized app to Sybase ASE with TLS 1.2/1.3 enforced, using the official SAP ODBC driver, with proper certificate validation.

## Architecture

### Production

```
  +------------------+   TLS 1.2/1.3    +------------------+
  | .NET 10 App      |----------------->| Sybase ASE 16.x  |
  | (Linux container)|                  | (DBA-managed)    |
  |                  |                  |                  |
  |  System.Data.Odbc|                  |  Native TLS      |
  |       |          |                  |  (ASE_ASM license)|
  |  libsybdrvodb.so |                  +------------------+
  |  libsapcrypto.so |
  +------------------+
```

No proxies or sidecars. The SAP ODBC driver handles TLS natively.

## SAP ODBC Driver Stack

Bundled in the container at `/opt/sap/DataAccess64/ODBC/`:

| Library | Role |
|---------|------|
| `libsybdrvodb.so` | ODBC driver — TDS 5.0 protocol, delegates TLS to libsapcrypto |
| `libsapcrypto.so` | TLS handshake, certificate validation, cipher negotiation |
| `libsapssfs.so` | Secure credential storage |
| `libslcryptokernel.so` | Low-level crypto primitives |

Registered in `/etc/odbcinst.ini`. Runtime linker finds dependencies via `LD_LIBRARY_PATH`.

## Connection String

```
Driver={Sybase ODBC Driver};Server=host;Port=5000;Database=mydb;
UID=user;PWD=pass;Encryption=ssl;TrustedFile=/opt/sap/certs/ca.crt;
```

| Parameter | Purpose |
|-----------|---------|
| `Encryption=ssl` | Enables TLS in the ODBC driver |
| `TrustedFile` | CA certificate (PEM) for server cert validation |

## TLS Handshake Flow

```
.NET App                  SAP ODBC Driver              Sybase ASE
   |                           |                           |
   | OdbcConnection.Open()     |                           |
   |-------------------------->|                           |
   |                           | TCP connect               |
   |                           |-------------------------->|
   |                           | ClientHello (TLS 1.2/1.3) |
   |                           |-------------------------->|
   |                           | ServerHello + Certificate  |
   |                           |<--------------------------|
   |                           | Validate cert against      |
   |                           | TrustedFile CA cert        |
   |                           | Key Exchange + Finished    |
   |                           |<------------------------->|
   |                           | TDS Login (encrypted)     |
   |                           |-------------------------->|
   |                           | TDS Login Ack (encrypted) |
   |                           |<--------------------------|
   | Connection established    |                           |
   |<--------------------------|                           |
```

TLS is handled entirely by `libsapcrypto.so`. The .NET runtime is unaware of it — `System.Data.Odbc` delegates to the native driver via P/Invoke.

## Certificate Validation

Two independent validation paths in the app:

**1. ODBC Driver (database connection)** — `libsapcrypto.so` validates server cert against `TrustedFile`. Happens in native code.

**2. SslStream (diagnostic endpoint)** — `CheckTls()` opens a raw TCP+TLS connection using `System.Net.Security.SslStream` with `X509ChainTrustMode.CustomRootTrust`. Only the explicitly provided CA cert is trusted. Used by `/api/tls-check` only.

Both paths use the same CA certificate. Neither skips validation.

## Container Image

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build    # Build stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0           # Runtime stage
  + unixodbc (apt)
  + SAP ODBC .so files
  + odbcinst.ini registration
  + Published .NET app
```

## Configuration

```
appsettings.json                  # Base defaults
appsettings.Production.json       # Corp config (edit this)
```

`ASPNETCORE_ENVIRONMENT=Production` selects the right file. `Server` and `Port` for TLS diagnostics are parsed from the connection string — no duplication.

## API Endpoints

`GET /` — Dashboard UI

`GET /api/tls-check` — Returns JSON:
- TLS protocol and cipher negotiated
- Certificate CN and validation status
- TLS 1.0/1.1 rejection confirmation
- Database connection status and server info

## Why This Approach

| Alternative | Why not |
|-------------|---------|
| `AdoNetCore.AseClient` | No TLS support. Unmaintained since Feb 2021. |
| `Sybase.AdoNet45.AseClient.dll` | .NET Framework 4.5 only. Windows native code. Fails on .NET 10 / Linux. |
| stunnel sidecar | Extra process. Obscures TLS endpoint in monitoring. |
| FreeTDS + unixODBC | Inconsistent Sybase compatibility. Not the supported path. |
