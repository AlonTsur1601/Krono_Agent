$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (!$compiler) {
    $compiler = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$compiler) { throw 'Install Inno Setup 6 before building the installer.' }
if (!(Test-Path -LiteralPath (Join-Path $root 'release/Krono-Agent/KronoAgent.exe'))) {
    throw 'Run npm run package:win first.'
}
$version = (Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw | ConvertFrom-Json).version
Add-Type -AssemblyName System.Drawing
$art = [Drawing.Bitmap]::new(328, 628)
$canvas = [Drawing.Graphics]::FromImage($art)
$ink = [Drawing.Pen]::new([Drawing.Color]::FromArgb(43, 81, 73), 2)
$icon = [Drawing.Icon]::new((Join-Path $root 'native/Krono.Agent/Assets/AppIcon.ico'), 128, 128)
try {
    $canvas.Clear([Drawing.Color]::FromArgb(16, 21, 29))
    $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    foreach ($diameter in 172, 218, 264) {
        $canvas.DrawEllipse($ink, [int]((328 - $diameter) / 2), [int]((628 - $diameter) / 2), $diameter, $diameter)
    }
    $canvas.DrawIcon($icon, [Drawing.Rectangle]::new(100, 250, 128, 128))
    $canvas.DrawLine($ink, 142, 525, 186, 525)
    $art.Save((Join-Path $root 'release/Krono-Setup-Art.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
} finally {
    $icon.Dispose()
    $ink.Dispose()
    $canvas.Dispose()
    $art.Dispose()
}
& $compiler "/DKronoVersion=$version" (Join-Path $PSScriptRoot 'krono-setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
