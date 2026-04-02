#!/bin/bash
set -e

source /opt/sybase/SYBASE.sh

SA_PASSWORD="${SA_PASSWORD:-myPassword}"
SERVER_NAME="MYSYBASE"

echo "[1/4] Starting ASE server..."
startserver -f /opt/sybase/ASE-16_0/install/RUN_${SERVER_NAME} &

echo "[2/4] Waiting for ASE to become ready..."
RETRIES=30
until isql -Usa -P"${SA_PASSWORD}" -S"${SERVER_NAME}" -Q "select 1" > /dev/null 2>&1; do
  RETRIES=$((RETRIES - 1))
  if [ $RETRIES -eq 0 ]; then
    echo "ERROR: ASE did not start in time."
    exit 1
  fi
  sleep 3
done

echo "[3/4] ASE is up. Configuring TLS 1.2..."
isql -Usa -P"${SA_PASSWORD}" -S"${SERVER_NAME}" << ISQL
-- Register server certificate
sp_ssladmin addcert, "${SERVER_NAME}", "/opt/sybase/ASE-16_0/certificates/sybase.crt"
go

-- Enable SSL services
sp_configure "ssl services", 1
go

-- Enforce TLS 1.2 as minimum protocol (disables TLS 1.0 and 1.1)
sp_ssladmin setproto, "TLSv1.2"
go

-- Drop NULL (unencrypted) cipher suites
sp_ssladmin dropciphers, "NULL"
go
ISQL

echo "[4/4] TLS 1.2 configured. Restarting ASE to apply..."
isql -Usa -P"${SA_PASSWORD}" -S"${SERVER_NAME}" << ISQL
shutdown with nowait
go
ISQL

sleep 5

startserver -f /opt/sybase/ASE-16_0/install/RUN_${SERVER_NAME}

echo "ASE is running with TLS 1.2 enforced on port 5000."
tail -f /opt/sybase/ASE-16_0/install/${SERVER_NAME}.log
