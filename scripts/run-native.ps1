$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot '../native/Krono.Agent/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/KronoAgent.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Build Krono first using npm run build.' }
Start-Process -FilePath $executable
