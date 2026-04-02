#!/bin/bash
# Unified run script for Docker and Podman
set -e

# ── Detect runtime ────────────────────────────────────────────────────────────
if command -v docker &> /dev/null && docker info &> /dev/null 2>&1; then
  RUNTIME="docker"
  COMPOSE_CMD="docker compose"
  # Fallback to docker-compose (v1) if docker compose plugin not available
  if ! docker compose version &> /dev/null 2>&1; then
    COMPOSE_CMD="docker-compose"
  fi
elif command -v podman &> /dev/null; then
  RUNTIME="podman"
  COMPOSE_CMD="podman-compose"
else
  echo "ERROR: Neither Docker nor Podman found. Please install one of them."
  exit 1
fi

echo "Detected runtime: $RUNTIME"

# ── Generate certs if missing ─────────────────────────────────────────────────
if [ ! -f "certs/server.crt" ] || [ ! -f "certs/server.key" ]; then
  echo "Certificates not found. Generating self-signed certs..."
  bash certs/gen-certs.sh
else
  echo "Certificates already exist. Skipping generation."
fi

# ── Podman-specific setup ─────────────────────────────────────────────────────
if [ "$RUNTIME" = "podman" ]; then
  echo "Applying Podman-specific kernel settings..."

  # Podman rootless doesn't support privileged shmmax via sysctl inside container.
  # Set it on the host instead.
  if [ "$(id -u)" -eq 0 ]; then
    sysctl -w kernel.shmmax=1067108864
    sysctl -w kernel.shmall=30000000000
  else
    echo "NOTE: Running rootless Podman. If Sybase fails to start due to shared memory,"
    echo "      run as root: sudo sysctl -w kernel.shmmax=1067108864"
  fi

  # Podman needs :Z for SELinux volume relabeling (already in compose.yml)
  # podman-compose or podman play kube can be used
  if ! command -v podman-compose &> /dev/null; then
    echo "podman-compose not found. Falling back to podman run directly..."
    _run_podman_direct
    exit 0
  fi
fi

# ── Parse CLI args ────────────────────────────────────────────────────────────
ACTION="${1:-up}"

case "$ACTION" in
  up)
    echo "Starting sybase-tls12 with $RUNTIME..."
    $COMPOSE_CMD -f compose.yml up --build -d
    echo ""
    echo "Container started. Logs:"
    sleep 3
    $COMPOSE_CMD -f compose.yml logs -f
    ;;
  down)
    echo "Stopping sybase-tls12..."
    $COMPOSE_CMD -f compose.yml down
    ;;
  logs)
    $COMPOSE_CMD -f compose.yml logs -f
    ;;
  verify)
    echo "Verifying TLS 1.2 ciphers inside container..."
    if [ "$RUNTIME" = "docker" ]; then
      docker exec -it sybase-tls12 bash -c \
        "source /opt/sybase/SYBASE.sh && isql -Usa -P\${SA_PASSWORD:-myPassword} -SMYSYBASE -Q 'sp_ssladmin lscipher'"
    else
      podman exec -it sybase-tls12 bash -c \
        "source /opt/sybase/SYBASE.sh && isql -Usa -P\${SA_PASSWORD:-myPassword} -SMYSYBASE -Q 'sp_ssladmin lscipher'"
    fi
    ;;
  *)
    echo "Usage: $0 {up|down|logs|verify}"
    exit 1
    ;;
esac

# ── Podman direct run fallback ────────────────────────────────────────────────
_run_podman_direct() {
  echo "Building image with podman..."
  podman build -t sybase-tls12 .

  echo "Running container with podman..."
  podman run -d \
    --name sybase-tls12 \
    --hostname sybase-server \
    -p 5000:5000 \
    --memory 2g \
    --privileged \
    --ipc=host \
    -v "$(pwd)/certs:/opt/sybase/ASE-16_0/certificates:Z" \
    -v "sybase-data:/opt/sybase/data:Z" \
    -e SYBASE=/opt/sybase \
    -e SA_PASSWORD=myPassword \
    sybase-tls12

  echo "Container started. Use: podman logs -f sybase-tls12"
}
