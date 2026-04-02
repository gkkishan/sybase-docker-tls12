#!/bin/bash
set -e

source /opt/sap/SYBASE.sh

SA_PASSWORD="sybase"
SERVER_NAME="DB_TEST"
CERT_DIR="$SYBASE/$SYBASE_ASE/certificates"

# ── Step 0: Initialize data if first run ──────────────────────────────────────
if [ ! -f "/data/master.dat" ]; then
  echo "[0/4] First run — extracting database files..."
  cd /
  tar -xzf /tmp/data.tar.gz --no-same-owner
  cd -
fi

# ── Step 1: Ensure certificates are in place ──────────────────────────────────
echo "[1/4] Setting up TLS certificates..."
mkdir -p "$CERT_DIR"
if [ -f "$CERT_DIR/server.crt" ] && [ -f "$CERT_DIR/server.key" ]; then
  echo "  Certificates found."
else
  echo "  ERROR: Missing certificate files in $CERT_DIR"
  ls -la "$CERT_DIR/" 2>/dev/null
  exit 1
fi
chmod 600 "$CERT_DIR/server.key"
chmod 644 "$CERT_DIR/server.crt"

# ── Step 2: Install and configure stunnel for TLS 1.2 ─────────────────────────
echo "[2/4] Installing stunnel for TLS 1.2 proxy..."
apt-get update -qq > /dev/null 2>&1 && apt-get install -y -qq stunnel4 > /dev/null 2>&1

# Create stunnel config: TLS 1.2 on port 5000, proxy to ASE on port 5100
cat > /etc/stunnel/stunnel.conf << STCFG
pid = /var/run/stunnel.pid
foreground = no
debug = 5

[sybase-tls]
accept = 0.0.0.0:5000
connect = 127.0.0.1:5100
cert = ${CERT_DIR}/server.crt
key = ${CERT_DIR}/server.key
sslVersionMin = TLSv1.2
sslVersionMax = TLSv1.2
STCFG

# ── Step 3: Update ASE to listen on port 5100 (internal) ─────────────────────
echo "[3/4] Configuring ASE to listen on internal port 5100..."

# Update interfaces to use port 5100
cat > /opt/sap/interfaces << IFACE
DB_TEST
	master tcp ether 0.0.0.0 5100
	query tcp ether 0.0.0.0 5100
IFACE

# Update the RUN file to use port 5100 (interfaces dir is /opt/sap)
# The RUN file already points to /opt/sap for interfaces

# ── Step 4: Start ASE + stunnel ───────────────────────────────────────────────
echo "[4/4] Starting ASE server on port 5100 + stunnel TLS proxy on port 5000..."

startserver -f $SYBASE/$SYBASE_ASE/install/RUN_${SERVER_NAME} > /dev/null &

# Wait for ASE on port 5100
RETRIES=90
until isql -Usa -P"${SA_PASSWORD}" -S"${SERVER_NAME}" -Q "select 1" > /dev/null 2>&1; do
  RETRIES=$((RETRIES - 1))
  if [ $RETRIES -eq 0 ]; then
    echo "  ERROR: ASE did not start in time."
    cat $SYBASE/$SYBASE_ASE/install/${SERVER_NAME}.log 2>/dev/null | tail -30
    exit 1
  fi
  sleep 5
done
echo "  ASE is running on internal port 5100."

# Start stunnel
stunnel /etc/stunnel/stunnel.conf
echo "  stunnel TLS 1.2 proxy is running on port 5000."

echo ""
echo "============================================"
echo " Sybase ASE with TLS 1.2 (via stunnel)"
echo " TLS port: 5000 (external)"
echo " ASE port: 5100 (internal, no TLS)"
echo " SA pass:  sybase"
echo "============================================"

# Graceful shutdown handler
shutdown_handler() {
  echo "Received SIGTERM, shutting down..."
  kill $(cat /var/run/stunnel.pid) 2>/dev/null
  isql -Usa -P"${SA_PASSWORD}" -S"${SERVER_NAME}" -Q "shutdown with nowait" > /dev/null 2>&1
  exit 0
}
trap 'shutdown_handler' SIGTERM

tail -f $SYBASE/$SYBASE_ASE/install/${SERVER_NAME}.log &
wait $!
