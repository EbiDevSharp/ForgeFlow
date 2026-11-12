$repoRoot = Split-Path -Parent $PSScriptRoot
$dllPath  = Join-Path $repoRoot 'src\ForgeFlow.Web\bin\Release\net10.0\ForgeFlow.Web.dll'

$env:ASPNETCORE_ENVIRONMENT       = 'Development'
$env:ASPNETCORE_URLS              = 'https://localhost:5001'
$env:ConnectionStrings__ForgeFlow = 'Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet $dllPath
