param(
    [string]$TargetUrl = "https://localhost:5001/account/login",
    [int]$TimeoutSeconds = 45
)

Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds(2)

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    try {
        $response = $client.GetAsync($TargetUrl).Result
        if ($response.IsSuccessStatusCode) {
            Start-Process $TargetUrl
            exit 0
        }
    } catch {
        Start-Sleep -Milliseconds 400
    }
}

Start-Process $TargetUrl
