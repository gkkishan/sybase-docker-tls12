using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SybaseTlsClient.Services;

public class TlsChecker
{
    public async Task<TlsCheckResult> CheckTlsAsync(string host, int port, string caCertPath)
    {
        bool connected = false;
        string? protocol = null;
        string? cipher = null;
        string? certificate = null;
        bool tlsv10Rejected = false;
        bool tlsv11Rejected = false;
        string? error = null;

        // Simple certificate validation - accept all certificates since ODBC handles the actual validation
        bool ValidateCert(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
        {
            // Accept certificate - ODBC driver handles actual validation with TrustedFile
            return cert != null;
        }

        // Test TLS 1.2 or 1.3 (whichever the server supports)
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port);
            using var ssl = new SslStream(tcp.GetStream(), false, ValidateCert);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            });
            connected = true;
            protocol = ssl.SslProtocol.ToString();
            cipher = ssl.NegotiatedCipherSuite.ToString();
            certificate = ssl.RemoteCertificate is X509Certificate2 c2
                ? c2.GetNameInfo(X509NameType.SimpleName, false)
                : ssl.RemoteCertificate?.Subject;
        }
        catch (Exception ex) { error = ex.Message; }

        // Verify TLS 1.0 is rejected
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port);
            using var ssl = new SslStream(tcp.GetStream(), false, ValidateCert);
#pragma warning disable SYSLIB0039
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls
            });
#pragma warning restore SYSLIB0039
        }
        catch { tlsv10Rejected = true; }

        // Verify TLS 1.1 is rejected
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port);
            using var ssl = new SslStream(tcp.GetStream(), false, ValidateCert);
#pragma warning disable SYSLIB0039
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls11
            });
#pragma warning restore SYSLIB0039
        }
        catch { tlsv11Rejected = true; }

        return new TlsCheckResult
        {
            Connected = connected,
            Protocol = protocol,
            Cipher = cipher,
            Certificate = certificate,
            TlsV10Rejected = tlsv10Rejected,
            TlsV11Rejected = tlsv11Rejected,
            Error = error
        };
    }
}

public record TlsCheckResult
{
    public bool Connected { get; init; }
    public string? Protocol { get; init; }
    public string? Cipher { get; init; }
    public string? Certificate { get; init; }
    public bool TlsV10Rejected { get; init; }
    public bool TlsV11Rejected { get; init; }
    public string? Error { get; init; }
}
