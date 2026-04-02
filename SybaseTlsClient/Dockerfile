FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Skip SSL verification for NuGet restore behind corp proxy
ENV DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0
ENV NUGET_CERT_REVOCATION_MODE=off
ENV DOTNET_NUGET_SIGNATURE_VERIFICATION=false

WORKDIR /src
COPY SybaseTlsClient.csproj nuget.config ./
RUN dotnet restore --no-cache
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN apt-get update -qq && apt-get install -y -qq unixodbc && rm -rf /var/lib/apt/lists/*

COPY sap-odbc/lib/ /opt/sap/DataAccess64/ODBC/lib/
COPY sap-odbc/dm-lib/ /opt/sap/DataAccess64/ODBC/dm/lib64/

RUN printf '[Sybase ODBC Driver]\nDescription=SAP Sybase ODBC Driver\nDriver=/opt/sap/DataAccess64/ODBC/lib/libsybdrvodb.so\nFileUsage=1\n' > /etc/odbcinst.ini

ENV LD_LIBRARY_PATH=/opt/sap/DataAccess64/ODBC/lib:/opt/sap/DataAccess64/ODBC/dm/lib64
ENV SYBASE=/opt/sap

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "SybaseTlsClient.dll", "--urls", "http://0.0.0.0:8080"]
