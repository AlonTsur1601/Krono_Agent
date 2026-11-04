param([string]$Executable, [string]$Fixture, [string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
$script:process = $null
$script:window = $null
function Launch {
    $script:process = Start-Process -FilePath $Executable -PassThru
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 200
        $script:process.Refresh()
        if ($script:process.HasExited) { throw 'Native process exited before window appeared' }
    } while ($script:process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($script:process.MainWindowHandle -eq 0) { throw 'No native top-level window' }
    $script:window = [Windows.Automation.AutomationElement]::FromHandle($script:process.MainWindowHandle)
}
function Find([string]$Value, [switch]$ByName) {
    $property = [Windows.Automation.AutomationElement]::AutomationIdProperty
    if ($ByName) { $property = [Windows.Automation.AutomationElement]::NameProperty }
    $condition = New-Object Windows.Automation.PropertyCondition($property, $Value)
    $deadline = (Get-Date).AddSeconds(20)
    do { $element = $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition); if ($element) { return $element }; Start-Sleep -Milliseconds 200 } while ((Get-Date) -lt $deadline)
    $snapshot = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
    $snapshot | Set-Content -LiteralPath (Join-Path $ReportDirectory 'failed-controls.txt')
    throw "Native control not found: $Value. See failed-controls.txt."
}
function Invoke([string]$Value, [switch]$ByName) { $element = Find $Value -ByName:$ByName; $pattern = $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern); $pattern.Invoke() }
function SetValue([string]$Id, [string]$Value) { $element = Find $Id; $pattern = $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern); $pattern.SetValue($Value) }
function SelectNav([string]$Name) { $element = Find $Name -ByName; $pattern = $element.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern); $pattern.Select(); Start-Sleep -Milliseconds 400 }
function SelectChoice([string]$Id, [string]$Name) { $box = Find $Id; ($box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand(); $item = Find $Name -ByName; ($item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
function CloseApp { if ($script:process -and !$script:process.HasExited) { $script:process.CloseMainWindow() | Out-Null; if (!$script:process.WaitForExit(5000)) { Stop-Process -Id $script:process.Id } } }
function Capture([string]$Name) {
    Start-Sleep -Milliseconds 200
    $bounds = $script:window.Current.BoundingRectangle
    $bitmap = New-Object Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $ReportDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
try {
    Launch
    SetValue 'EmailInput' 'native-fixture@example.com'
    SetValue 'PasswordInput' 'fixture-password'
    Invoke 'LoginButton'
    Find 'Sign out' -ByName | Out-Null
    Invoke '+ New chat' -ByName
    Find 'Send' -ByName | Out-Null
    SelectNav 'Projects & memory'
    Find 'Save project' -ByName | Out-Null
    SetValue 'ProjectName' 'Native fixture project'
    SetValue 'ProjectFolders' $ReportDirectory
    SetValue 'ProjectMemory' 'Native project memory fixture'
    Invoke 'Save project' -ByName
    SetValue 'GlobalMemory' 'Native global memory fixture'
    Invoke 'Save global memory' -ByName
    SelectNav 'Settings'
    Find 'Save key' -ByName | Out-Null
    SetValue 'ProviderKey' 'fixture-provider-key'
    Invoke 'Save key' -ByName
    $model = Find 'ModelSelector'
    $edit = $model.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
    ($edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue('native-fixture-model')
    Invoke 'Use model' -ByName
    SelectNav 'Chats'
    SetValue 'Composer' 'Native test message'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 300; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while ($assertions.completions -lt 1 -and (Get-Date) -lt $deadline)
    if (!$assertions.chatHasNoTools) { throw 'Native chat did not reach provider with tools omitted' }
    if (!$assertions.globalOnly) { throw 'Native unassigned chat memory scope is incorrect' }
    Start-Sleep -Milliseconds 500
    Invoke 'Project…' -ByName
    SelectChoice 'ChatProjectSelector' 'Native fixture project'
    Invoke 'Move' -ByName
    SelectChoice 'Mode' 'agent'
    SetValue 'Composer' 'Write the fixture file with approval'
    Invoke 'Send' -ByName
    Find 'Allow once' -ByName | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-tool-proof.txt')) { throw 'File was written before approval' }
    Invoke 'Allow once' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while (!$assertions.toolWrite -and (Get-Date) -lt $deadline)
    if (!$assertions.toolWrite) { throw 'Approved native action did not write its file' }
    if (!$assertions.projectScope) { throw 'Native project chat did not receive its project memory' }
    (Find 'Composer').SetFocus()
    Start-Sleep -Milliseconds 300
    $bounds = $script:window.Current.BoundingRectangle
    $bitmap = New-Object Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $ReportDirectory 'native-workspace.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    SelectNav 'Games'
    (Find 'GameTile0').SetFocus()
    Capture 'native-game-library.png'
    Invoke 'GameTile0'
    Find 'Restart' -ByName | Out-Null
    foreach ($game in @('Tic-Tac-Toe','2048','Minesweeper','Connect Four','Breakout','Memory Match','Pong','Snake')) {
        SelectChoice 'GameSelector' $game
        Invoke 'Restart' -ByName
        if ($game -eq 'Tic-Tac-Toe') {
            Invoke 'GameCell0'
            if ((Find 'GameCell0').Current.Name -ne 'Cell 1: X') { throw 'Native tic-tac-toe did not apply the move' }
        }
        if ($game -eq 'Minesweeper') {
            Invoke 'GameCell0'
            if ((Find 'GameStatus').Current.Name -match '^Score 0$') { throw 'Native minesweeper did not reveal the first cell' }
        }
        if ($game -eq 'Connect Four') {
            Invoke 'GameCell35'
            if ((Find 'GameCell35').Current.Name -eq 'Cell 36: empty') { throw 'Native connect four did not drop a piece' }
        }
        if ($game -eq 'Memory Match') {
            Invoke 'GameCell0'
            if ((Find 'GameCell0').Current.Name -eq 'Cell 1: ?') { throw 'Native memory game did not reveal a card' }
        }
        if ($game -eq '2048') { Invoke 'Move left' -ByName }
        if ($game -eq 'Breakout') { Capture 'native-breakout.png' }
    }
    SelectNav 'Terminal'
    Start-Sleep -Seconds 2
    Find 'PowerShell' -ByName | Out-Null
    SetValue 'TerminalInput' 'Write-Output (6*7)'
    Invoke 'Run input' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $output = Find 'TerminalOutput'; $text = ($output.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).Current.Value } while ($text -notmatch '42' -and (Get-Date) -lt $deadline)
    if ($text -notmatch '42') { throw 'Native PTY did not produce 42' }
    SelectNav 'Browser'
    Start-Sleep -Seconds 3
    Find 'Address or search' -ByName | Out-Null
    $deadline = (Get-Date).AddSeconds(30)
    do { Start-Sleep -Milliseconds 200; $browserAddress = Find 'BrowserAddress' } while (!$browserAddress.Current.IsEnabled -and (Get-Date) -lt $deadline)
    if (!$browserAddress.Current.IsEnabled) { throw 'Browser initialization did not finish' }
    SetValue 'BrowserAddress' ($Fixture + '/probe')
    Invoke 'Go' -ByName
    Find 'Native browser fixture' -ByName | Out-Null
    $deadline = (Get-Date).AddSeconds(5)
    do { Start-Sleep -Milliseconds 100; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while (!$assertions.browserIsolated -and (Get-Date) -lt $deadline)
    if (!$assertions.browserIsolated) { throw 'Browser fixture did not prove host API / Node isolation' }
    if (!(Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin'))) { throw 'Encrypted session missing' }
    CloseApp
    Launch
    Find 'Sign out' -ByName | Out-Null
    SelectNav 'Sign out'
    Find 'LoginButton' | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin')) { throw 'Logout retained local session' }
    SetValue 'EmailInput' 'native-fixture@example.com'
    SetValue 'PasswordInput' 'fixture-password'
    Invoke 'LoginButton'
    Find 'Sign out' -ByName | Out-Null
    Invoke-RestMethod -Method Post ($Fixture + '/revoke') | Out-Null
    CloseApp
    Launch
    Find 'LoginButton' | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin')) { throw 'Revoked session was retained after restart' }
    Write-Output 'NATIVE_SMOKE_PASS: native auth, scoped projects/memory, streamed model, approved file write, eight game initializations and board moves, PTY 42, WebView2 navigation, restart, logout, remote-revocation rejection'
} finally { CloseApp }
