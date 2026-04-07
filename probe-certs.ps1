$servers = @(
    # AWS DEV01
    @{Host="ngd_sybase_dev01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_NGD_DEV01"},
    @{Host="smt_sybase_dev01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_SMT_DEV01"},
    @{Host="srx_sybase_dev01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_SRX_DEV01"},
    @{Host="wlp_sybase_dev01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_WLP_DEV01"},
    @{Host="rbm_sybase_dev01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_RBM_DEV01"},
    @{Host="sld_sybase_dev01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_SLD_DEV01"},
    # AWS SIT01
    @{Host="ngd_sybase_sit01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_NGD_SIT01"},
    @{Host="smt_sybase_sit01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_SMT_SIT01"},
    @{Host="srx_sybase_sit01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_SRX_SIT01"},
    @{Host="wlp_sybase_sit01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_WLP_SIT01"},
    @{Host="rbm_sybase_sit01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_RBM_SIT01"},
    @{Host="sld_sybase_sit01.silver01.aws.cloud.aim.local"; Port=5020; Name="AWS_SLD_SIT01"},
    # QA
    @{Host="irbm-qa.aimspecialtyhealth.com"; Port=6120; Name="iRBM_QA"},
    @{Host="iwlp-qa.aimspecialtyhealth.com"; Port=6130; Name="iWLP_QA"},
    @{Host="ismt-qa.aimspecialtyhealth.com"; Port=6140; Name="iSMT_QA"},
    @{Host="isrx-qa.aimspecialtyhealth.com"; Port=6150; Name="iSRX_QA"},
    @{Host="iopd-qa.aimspecialtyhealth.com"; Port=6160; Name="OPD_QA"},
    @{Host="ingd-qa.aimspecialtyhealth.com"; Port=6170; Name="NGD_QA"},
    @{Host="isld-qa.aimspecialtyhealth.com"; Port=6180; Name="iSLD_QA"},
    # SIT2
    @{Host="irbm-sit2.aimspecialtyhealth.com"; Port=6820; Name="iRBM_SIT2"},
    @{Host="iwlp-sit2.aimspecialtyhealth.com"; Port=6830; Name="iWLP_SIT2"},
    @{Host="ismt-sit2.aimspecialtyhealth.com"; Port=6840; Name="iSMT_SIT2"},
    @{Host="isrx-sit2.aimspecialtyhealth.com"; Port=6850; Name="iSRX_SIT2"},
    @{Host="iopd-sit2.aimspecialtyhealth.com"; Port=6870; Name="OPD_SIT2"},
    @{Host="ingd-sit2.aimspecialtyhealth.com"; Port=6860; Name="NGD_SIT2"},
    @{Host="isld-sit2.aimspecialtyhealth.com"; Port=6880; Name="iSLD_SIT2"},
    @{Host="ismt-stgrep.aimspecialtyhealth.com"; Port=8290; Name="SMT_STAGINGREP"},
    @{Host="iwlp-stgrep.aimspecialtyhealth.com"; Port=8270; Name="WLP_STAGINGREP"}
)

$results = @()
$caCerts = @{}

foreach ($server in $servers) {
    Write-Host "Probing $($server.Name) ($($server.Host):$($server.Port))..." -ForegroundColor Cyan
    
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $tcpClient.Connect($server.Host, $server.Port)
        
        $sslStream = New-Object System.Net.Security.SslStream($tcpClient.GetStream(), $false, {$true})
        $sslStream.AuthenticateAsClient($server.Host)
        
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]$sslStream.RemoteCertificate
        
        # Get certificate chain
        $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
        $chain.Build($cert) | Out-Null
        
        $serverCert = $cert
        $issuerCert = $null
        $rootCert = $null
        
        if ($chain.ChainElements.Count -gt 1) {
            $issuerCert = $chain.ChainElements[1].Certificate
        }
        if ($chain.ChainElements.Count -gt 2) {
            $rootCert = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
        }
        
        $result = [PSCustomObject]@{
            Name = $server.Name
            Host = $server.Host
            Port = $server.Port
            Connected = $true
            ServerCN = $serverCert.Subject
            ServerThumbprint = $serverCert.Thumbprint
            IssuerCN = $serverCert.Issuer
            IssuerThumbprint = if ($issuerCert) { $issuerCert.Thumbprint } else { "N/A" }
            RootCN = if ($rootCert) { $rootCert.Subject } else { "N/A" }
            RootThumbprint = if ($rootCert) { $rootCert.Thumbprint } else { "N/A" }
            ChainLength = $chain.ChainElements.Count
        }
        
        # Collect unique CA certificates
        if ($issuerCert -and -not $caCerts.ContainsKey($issuerCert.Thumbprint)) {
            $caCerts[$issuerCert.Thumbprint] = @{
                Subject = $issuerCert.Subject
                Thumbprint = $issuerCert.Thumbprint
                Type = "Intermediate"
                Cert = $issuerCert
            }
        }
        if ($rootCert -and -not $caCerts.ContainsKey($rootCert.Thumbprint)) {
            $caCerts[$rootCert.Thumbprint] = @{
                Subject = $rootCert.Subject
                Thumbprint = $rootCert.Thumbprint
                Type = "Root"
                Cert = $rootCert
            }
        }
        
        $sslStream.Close()
        $tcpClient.Close()
        
        Write-Host "  ✓ Success - CN: $($serverCert.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false))" -ForegroundColor Green
    }
    catch {
        $result = [PSCustomObject]@{
            Name = $server.Name
            Host = $server.Host
            Port = $server.Port
            Connected = $false
            Error = $_.Exception.Message
        }
        Write-Host "  ✗ Failed - $($_.Exception.Message)" -ForegroundColor Red
    }
    
    $results += $result
}

Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host "CERTIFICATE ANALYSIS SUMMARY" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Yellow

# Group by issuer
$grouped = $results | Where-Object {$_.Connected} | Group-Object -Property IssuerThumbprint

Write-Host "Unique CA Certificates Found: $($caCerts.Count)`n" -ForegroundColor Cyan

foreach ($ca in $caCerts.Values) {
    Write-Host "[$($ca.Type)] $($ca.Subject)" -ForegroundColor Magenta
    Write-Host "  Thumbprint: $($ca.Thumbprint)" -ForegroundColor Gray
    $serversUsingThis = ($results | Where-Object {$_.IssuerThumbprint -eq $ca.Thumbprint -or $_.RootThumbprint -eq $ca.Thumbprint}).Count
    Write-Host "  Used by: $serversUsingThis servers`n" -ForegroundColor Gray
}

Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host "DETAILED RESULTS" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Yellow

$results | Format-Table -AutoSize Name, Host, Port, Connected, ServerCN, IssuerThumbprint, RootThumbprint

Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host "RECOMMENDATION" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Yellow

if ($caCerts.Count -eq 0) {
    Write-Host "No CA certificates found. All connections failed." -ForegroundColor Red
} elseif ($caCerts.Count -eq 1) {
    Write-Host "✓ All servers use the SAME CA certificate!" -ForegroundColor Green
    Write-Host "  You need only ONE certificate in corporate-ca.crt`n" -ForegroundColor Green
} else {
    Write-Host "⚠ Multiple CA certificates found ($($caCerts.Count) unique)" -ForegroundColor Yellow
    Write-Host "  You need to combine ALL of them into corporate-ca.crt`n" -ForegroundColor Yellow
}

# Export CA certificates
$exportDir = Join-Path $PSScriptRoot "certs\extracted"
if (-not (Test-Path $exportDir)) {
    New-Item -ItemType Directory -Path $exportDir | Out-Null
}

foreach ($ca in $caCerts.Values) {
    $filename = "$exportDir\$($ca.Type)-$($ca.Thumbprint.Substring(0,8)).crt"
    $certBytes = $ca.Cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    [System.IO.File]::WriteAllBytes($filename, $certBytes)
    Write-Host "Exported: $filename" -ForegroundColor Green
}

Write-Host "`nTo create corporate-ca.crt, run:" -ForegroundColor Cyan
Write-Host "  cat $exportDir\*.crt > certs\corporate-ca.crt" -ForegroundColor White
