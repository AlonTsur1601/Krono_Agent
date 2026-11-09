param([string]$PayloadUrl, [switch]$ReusePayload, [switch]$Offline)

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
$payloadName = 'Krono-Agent-Payload-x64.zip'
$payload = Join-Path $root "release/$payloadName"
if (!$Offline -and !$ReusePayload) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload }
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'release/Krono-Agent'), $payload)
}
Add-Type -AssemblyName System.Drawing
$art = [Drawing.Bitmap]::new(328, 628)
$canvas = [Drawing.Graphics]::FromImage($art)
$ink = [Drawing.Pen]::new([Drawing.Color]::FromArgb(43, 81, 73), 2)
$iconBytes = [IO.File]::ReadAllBytes((Join-Path $root 'native/Krono.Agent/Assets/AppIcon.ico'))
# AppIcon.ico stores its largest PNG frame in the last directory entry.
$entry = 6 + 16 * ([BitConverter]::ToUInt16($iconBytes, 4) - 1)
$iconStream = [IO.MemoryStream]::new($iconBytes, [BitConverter]::ToInt32($iconBytes, ($entry + 12)), [BitConverter]::ToInt32($iconBytes, ($entry + 8)))
try {
    $canvas.Clear([Drawing.Color]::FromArgb(16, 21, 29))
    $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $marks = @(
        @(35,38,0), @(131,52,1), @(252,36,2), @(75,111,3), @(208,114,4),
        @(298,144,1), @(28,178,4), @(145,161,2), @(262,208,3), @(37,264,1),
        @(286,303,0), @(27,365,3), @(288,392,4), @(74,433,0), @(237,459,2),
        @(34,509,2), @(143,499,3), @(301,520,1), @(94,567,4), @(205,570,0),
        @(29,597,1), @(275,600,3)
    )
    foreach ($mark in $marks) {
        $canvas.TranslateTransform([single]$mark[0], [single]$mark[1])
        switch ($mark[2]) {
            0 {
                $canvas.DrawRectangle($ink, 0, 0, 7, 7)
                $canvas.DrawRectangle($ink, -7, 7, 7, 7)
                $canvas.DrawRectangle($ink, 0, 7, 7, 7)
                $canvas.DrawRectangle($ink, 7, 7, 7, 7)
            }
            1 {
                $canvas.DrawArc($ink, -8, -8, 16, 16, 40, 280)
                $canvas.DrawLine($ink, 6, 5, 0, 0)
                $canvas.DrawLine($ink, 0, 0, 6, -5)
                $canvas.DrawEllipse($ink, 12, -1, 2, 2)
            }
            2 {
                $canvas.DrawEllipse($ink, -9, -9, 18, 18)
                $canvas.DrawEllipse($ink, -4, -4, 8, 8)
                $canvas.DrawLine($ink, 0, 0, 13, -13)
            }
            3 {
                $canvas.DrawLine($ink, -5, -9, -5, 9)
                $canvas.DrawLine($ink, -5, -9, 7, -5)
                $canvas.DrawLine($ink, 7, -5, -5, -1)
                $canvas.DrawLine($ink, -10, 9, 1, 9)
            }
            4 {
                $canvas.DrawRectangle($ink, -6, -9, 12, 18)
                $canvas.DrawLine($ink, 0, -4, 3, 0)
                $canvas.DrawLine($ink, 3, 0, 0, 4)
                $canvas.DrawLine($ink, 0, 4, -3, 0)
                $canvas.DrawLine($ink, -3, 0, 0, -4)
            }
        }
        $canvas.ResetTransform()
    }
    foreach ($diameter in 172, 218, 264) {
        $canvas.DrawEllipse($ink, [int]((328 - $diameter) / 2), [int]((628 - $diameter) / 2), $diameter, $diameter)
    }
    $logo = [Drawing.Image]::FromStream($iconStream)
    try { $canvas.DrawImage($logo, [Drawing.Rectangle]::new(100, 250, 128, 128)) }
    finally { $logo.Dispose() }
    $art.Save((Join-Path $root 'release/Krono-Setup-Art.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
} finally {
    $iconStream.Dispose()
    $ink.Dispose()
    $canvas.Dispose()
    $art.Dispose()
}
$defines = @("/DKronoVersion=$version")
if ($Offline) {
    $defines += '/DKronoOffline=1'
    $appDirectory = Join-Path $root 'release/Krono-Agent'
    $installedFiles = Get-ChildItem -LiteralPath $appDirectory -File -Recurse | Where-Object {
        $_.Extension -notin '.pdb', '.map' -and $_.Name -notlike '*.test.js'
    } | ForEach-Object { $_.FullName.Substring($appDirectory.Length + 1) }
} else {
    if (!$PayloadUrl) { $PayloadUrl = "https://github.com/AlonTsur1601/Krono_Agent/releases/download/v$version/$payloadName" }
    $payloadSize = (Get-Item -LiteralPath $payload).Length
    $payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
    $defines += "/DKronoPayloadUrl=$PayloadUrl", "/DKronoPayloadSize=$payloadSize", "/DKronoPayloadHash=$payloadHash"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($payload)
    try {
        $installedFiles = @($archive.Entries | Where-Object Name | ForEach-Object { $_.FullName.Replace('/', '\') })
    } finally { $archive.Dispose() }
}
[IO.File]::WriteAllLines((Join-Path $root 'release/Krono-InstalledFiles.txt'), [string[]]$installedFiles, [Text.UTF8Encoding]::new($true))
& $compiler @defines (Join-Path $PSScriptRoot 'krono-setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
