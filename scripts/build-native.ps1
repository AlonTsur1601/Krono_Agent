param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (!$dotnet) {
    $dotnetPath = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnetPath)) { throw 'Install .NET 10 SDK to build the native Windows app.' }
    $env:PATH = (Split-Path $dotnetPath) + ';' + $env:PATH
} else { $dotnetPath = $dotnet.Source }
node scripts/build-engine.cjs
if ($LASTEXITCODE -ne 0) { throw 'Agent engine build failed' }
if ($Publish) {
    & $dotnetPath publish native/Krono.Agent/Krono.Agent.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishReadyToRun=false -o release/Krono-Agent
} else { & $dotnetPath build native/Krono.Agent/Krono.Agent.csproj -p:Platform=x64 }
if ($LASTEXITCODE -ne 0) { throw 'Native Windows build failed' }
