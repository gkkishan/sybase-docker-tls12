FROM blieusong/ase-server:latest

USER root

# Copy TLS certificates
COPY certs/server.crt /opt/sap/ASE-16_0/certificates/sybase.crt
COPY certs/server.key /opt/sap/ASE-16_0/certificates/sybase.key

# Copy interfaces file with SSL entry
COPY config/interfaces /opt/sap/interfaces

# Copy startup script
COPY scripts/enable-tls.sh /opt/enable-tls.sh
RUN chmod +x /opt/enable-tls.sh

EXPOSE 5000

ENTRYPOINT ["/opt/enable-tls.sh"]
