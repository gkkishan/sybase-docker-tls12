#!/bin/bash
# Run this once locally before building the image
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

openssl req -x509 -newkey rsa:2048 \
  -keyout "$SCRIPT_DIR/server.key" \
  -out "$SCRIPT_DIR/server.crt" \
  -days 365 -nodes \
  -subj "/CN=sybase-server"

chmod 600 "$SCRIPT_DIR/server.key"
chmod 644 "$SCRIPT_DIR/server.crt"

echo "Done: server.crt and server.key generated in $SCRIPT_DIR"
