#!/bin/bash
# Run the .NET TLS connectivity app against a corporate Sybase server.
#
# Prerequisites:
#   1. Edit appsettings.Production.json with your Sybase server details
#   2. Place your CA certificate at: certs/corporate-ca.crt
#
# Usage:
#   bash run-corp.sh pull     # Pull base images (use if corp proxy blocks TLS)
#   bash run-corp.sh up       # Build and start
#   bash run-corp.sh down     # Stop
#   bash run-corp.sh logs     # View logs
#   bash run-corp.sh status   # Check if running
set -e

# ── Detect runtime ───────────────────────────────────────────────────────────
if command -v docker &> /dev/null && docker info &> /dev/null 2>&1; then
  RUNTIME="docker"
  COMPOSE_CMD="docker compose"
  if ! docker compose version &> /dev/null 2>&1; then
    COMPOSE_CMD="docker-compose"
  fi
elif command -v podman &> /dev/null; then
  RUNTIME="podman"
  COMPOSE_CMD="podman-compose"
else
  echo "ERROR: Neither Docker nor Podman found."
  exit 1
fi
echo "Runtime: $COMPOSE_CMD"

# ── Validate prerequisites ───────────────────────────────────────────────────
if [ ! -f "certs/corporate-ca.crt" ]; then
  echo ""
  echo "ERROR: certs/corporate-ca.crt not found."
  echo ""
  echo "  Copy your corporate CA certificate:"
  echo "    cp /path/to/your/ca-cert.crt certs/corporate-ca.crt"
  echo ""
  exit 1
fi

if grep -q "YOUR_SYBASE_HOST" SybaseTlsClient/appsettings.Production.json 2>/dev/null; then
  echo ""
  echo "ERROR: appsettings.Production.json still has placeholder values."
  echo ""
  echo "  Edit SybaseTlsClient/appsettings.Production.json and replace:"
  echo "    YOUR_SYBASE_HOST  -> your Sybase server hostname"
  echo "    YOUR_PORT         -> your Sybase TLS port"
  echo "    YOUR_DB           -> your database name"
  echo "    YOUR_USER         -> your database user"
  echo "    YOUR_PASSWORD     -> your database password"
  echo ""
  exit 1
fi

# ── Run ──────────────────────────────────────────────────────────────────────
ACTION="${1:-up}"

case "$ACTION" in
  pull)
    echo "Pulling base images (skipping TLS verify for corp proxy)..."
    if [ "$RUNTIME" = "podman" ]; then
      podman pull --tls-verify=false mcr.microsoft.com/dotnet/sdk:10.0
      podman pull --tls-verify=false mcr.microsoft.com/dotnet/aspnet:10.0
    else
      echo "For Docker, add mcr.microsoft.com to insecure-registries in Docker settings, then re-run."
      echo "Or pull on a machine with internet and use: docker save / docker load"
      docker pull mcr.microsoft.com/dotnet/sdk:10.0
      docker pull mcr.microsoft.com/dotnet/aspnet:10.0
    fi
    echo "Done. Now run: bash run-corp.sh up"
    ;;
  up)
    echo "Building and starting..."
    $COMPOSE_CMD -f compose.corp.yml up --build -d
    echo ""
    echo "Dashboard: http://localhost:8080"
    echo "API:       http://localhost:8080/api/tls-check"
    ;;
  down)
    $COMPOSE_CMD -f compose.corp.yml down
    ;;
  logs)
    $COMPOSE_CMD -f compose.corp.yml logs -f
    ;;
  status)
    $COMPOSE_CMD -f compose.corp.yml ps
    ;;
  *)
    echo "Usage: $0 {pull|up|down|logs|status}"
    exit 1
    ;;
esac
