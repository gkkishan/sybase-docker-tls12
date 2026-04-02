# sybase-docker-tls12

Sybase ASE 16 running in Docker/Podman with **TLS 1.2 strictly enforced**.

## Prerequisites
- Docker Desktop or Podman
- OpenSSL (for cert generation)

## Quick Start

### 1. Generate self-signed certificates
```bash
bash certs/gen-certs.sh
```

### 2. Build and run
```bash
# Works with both Docker and Podman
bash run.sh up
```

### 3. Stop
```bash
bash run.sh down
```

### 4. Verify TLS 1.2 is active
```bash
bash run.sh verify
```

## Configuration

| Setting         | Value            |
|-----------------|------------------|
| Port            | 5000             |
| Server Name     | MYSYBASE         |
| Default SA Pass | myPassword       |
| TLS Version     | 1.2 (minimum)    |
| Certificate     | Self-signed (CN=sybase-server) |

## Environment Variables

| Variable      | Default      | Description       |
|---------------|--------------|-------------------|
| `SA_PASSWORD` | `myPassword` | SA user password  |

Override in compose.yml:
```yaml
environment:
  - SA_PASSWORD=YourStrongPassword
```

## Docker vs Podman Differences

| Feature              | Docker              | Podman                        |
|----------------------|---------------------|-------------------------------|
| Compose              | `docker compose`    | `podman-compose`              |
| Privileged mode      | Supported           | Requires rootful or host IPC  |
| Volume SELinux label | Not needed          | `:Z` flag applied             |
| shmmax               | Set inside container| Set on host before running    |

## .NET Connection String (SAP ADO.NET SDK)
```
Data Source=localhost;Port=5000;Database=yourdb;
User ID=sa;Password=myPassword;
Encrypt=true;TrustServerCertificate=true;
```

## Notes
- Requires ASE 16.0 SP02 PL04+ for TLS 1.2 support
- Certificates are excluded from git via `.gitignore` — always generate them locally
- `run.sh` auto-detects Docker or Podman at runtime
