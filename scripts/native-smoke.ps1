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
function SetEditableChoice([string]$Id, [string]$Value) { $element = Find $Id; $edit = $element.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit))); ($edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue($Value) }
function SelectNav([string]$Name) { $element = Find $Name -ByName; $pattern = $element.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern); $pattern.Select(); Start-Sleep -Milliseconds 400 }
function SelectChoice([string]$Id, [string]$Name) { $box = Find $Id; ($box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand(); $item = Find $Name -ByName; ($item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
function SelectGame([string]$Name) {
    $games = @('Snake','Tic-Tac-Toe','2048','Minesweeper','Connect Four','Breakout','Memory Match','Pong','Lights Out','Simon','Odd Color','Orbit','Reversi','Sudoku','Stack','Color Gauntlet','Maze Chase','Flap','Darts','Jigsaw')
    $index = [Array]::IndexOf($games, $Name)
    if ($index -lt 0) { throw ('Unknown fixture game: ' + $Name) }
    Invoke 'GameLibraryBack'
    Invoke ('GameTile' + $index)
}
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
    foreach ($appearance in @('light','dark','halloween','system')) { SelectChoice 'Appearance' $appearance; if ($appearance -ne 'system') { Capture ('native-theme-' + $appearance + '.png') } }
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
    $deadline = (Get-Date).AddSeconds(10)
    while (!(Find 'Send' -ByName).Current.IsEnabled -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    SetValue 'Composer' 'NATIVE_COMMAND_PROOF'
    Invoke 'Send' -ByName
    Find 'Allow once' -ByName | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-command-proof.txt')) { throw 'Agent command ran before approval' }
    Invoke 'Review details' -ByName
    $details = (Find 'ToolApprovalDetails').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($details -notmatch '6\*7') { throw 'Command approval did not show its full script' }
    Capture 'native-command-approval.png'
    Invoke 'Close details' -ByName
    Invoke 'Allow once' -ByName
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 100; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while (!$assertions.commandRan -and (Get-Date) -lt $deadline)
    if (!$assertions.commandRan) { throw 'Approved agent command did not write its marker and return 42 to the model' }
    SelectNav 'Settings'
    SelectChoice 'Provider' 'Ollama'
    SetValue 'ProviderEndpoint' ($Fixture + '/local/v1')
    Invoke 'Save endpoint' -ByName
    Invoke 'Test connection' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $connected = (Find 'ProviderConnectionStatus').Current.Name } while ($connected -notmatch '^Connected' -and (Get-Date) -lt $deadline)
    if ($connected -notmatch '^Connected') { throw 'Native local endpoint connection test failed' }
    Invoke 'Load available models' -ByName
    Start-Sleep -Milliseconds 500
    SelectChoice 'ModelSelector' 'local-fixture-model'
    Invoke 'Use model' -ByName
    Capture 'native-local-model.png'
    SelectNav 'Chats'
    SetValue 'Composer' 'Reply using the local endpoint, without tools'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while (!$assertions.localNoTools -and (Get-Date) -lt $deadline)
    if (!$assertions.localNoTools -or !$assertions.localKeyIsolated) { throw 'Native local endpoint failed tool omission or credential isolation' }
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        $reply = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name.Trim() -eq 'Native local endpoint reply.' }
    } while (!$reply -and (Get-Date) -lt $deadline)
    if (!$reply) { throw 'Local endpoint response was not rendered in the native transcript' }
    $deadline = (Get-Date).AddSeconds(5)
    do { Start-Sleep -Milliseconds 100; $storedReply = (Invoke-RestMethod ($Fixture + '/local-reply-proof')).count } while ($storedReply -ne 1 -and (Get-Date) -lt $deadline)
    if ($storedReply -ne 1) { throw 'Final streamed fragments were not persisted together' }
    SelectNav 'Settings'
    $toggle = (Find 'ProviderTools').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    $toggle.Toggle()
    SetEditableChoice 'ModelSelector' 'basic-fixture-model'
    Invoke 'Use model' -ByName
    if ((Find 'ProviderTools').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::Off) { throw 'Tool capability leaked from model A to model B' }
    SelectNav 'Chats'
    SetValue 'Composer' 'Native manual model B without tool support'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 100; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while ((!$assertions.localModelBNoTools -or !(Find 'Send' -ByName).Current.IsEnabled) -and (Get-Date) -lt $deadline)
    if (!$assertions.localModelBNoTools) { throw 'Model B received tools despite its disabled capability' }
    SelectNav 'Settings'
    SetEditableChoice 'ModelSelector' 'local-fixture-model'
    Invoke 'Use model' -ByName
    $toggle = (Find 'ProviderTools').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [Windows.Automation.ToggleState]::On) { throw 'Model A lost its explicit tool capability' }
    $toggle.Toggle()
    SelectChoice 'Provider' 'OpenAI'
    $model = Find 'ModelSelector'
    $edit = $model.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
    if (($edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).Current.Value -ne 'native-fixture-model') { throw 'Provider switching lost its model selection' }
    foreach ($cloudProfile in @(@{Id='azure';Name='Azure OpenAI'},@{Id='xai';Name='xAI'},@{Id='groq';Name='Groq'},@{Id='mistral';Name='Mistral'},@{Id='openrouter';Name='OpenRouter'})) {
        SelectNav 'Settings'
        SelectChoice 'Provider' $cloudProfile.Name
        SetValue 'ProviderEndpoint' ($Fixture + '/cloud/' + $cloudProfile.Id + '/v1')
        Invoke 'Save endpoint' -ByName
        SetValue 'ProviderKey' ('fixture-' + $cloudProfile.Id + '-key')
        Invoke 'Save key' -ByName
        Invoke 'Load available models' -ByName
        Start-Sleep -Milliseconds 300
        SelectChoice 'ModelSelector' ($cloudProfile.Id + '-fixture')
        Invoke 'Use model' -ByName
        SelectNav 'Chats'
        SelectChoice 'Mode' 'chat'
        SetValue 'Composer' ('Native cloud profile ' + $cloudProfile.Id)
        Invoke 'Send' -ByName
        $deadline = (Get-Date).AddSeconds(10)
        do { Start-Sleep -Milliseconds 100; $cloudProof = Invoke-RestMethod ($Fixture + '/cloud-proof') } while (($cloudProof.accepted -notcontains $cloudProfile.Id -or !(Find 'Send' -ByName).Current.IsEnabled) -and (Get-Date) -lt $deadline)
        if ($cloudProof.accepted -notcontains $cloudProfile.Id) { throw ('Native cloud profile did not authenticate/stream: ' + $cloudProfile.Id) }
    }
    SelectNav 'Settings'
    SelectChoice 'Provider' 'OpenAI'
    SelectNav 'Chats'
    SelectChoice 'Mode' 'chat'
    SetValue 'Composer' 'NATIVE_PARENT_WAIT'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 100; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while (!$proof.parentHeld -and (Get-Date) -lt $deadline)
    if (!$proof.parentHeld) { throw 'Parent fixture stream did not start' }
    Invoke 'Side chat' -ByName
    Find 'SideComposer' | Out-Null
    $proof = Invoke-RestMethod ($Fixture + '/side-proof')
    if (!$proof.snapshot -or $proof.sideCount -ne 1) { throw 'Side chat did not copy its parent context' }
    $resumedSide = $proof.activeSide
    Invoke 'Close side chat' -ByName
    Invoke 'Side chat' -ByName
    Find 'SideComposer' | Out-Null
    $proof = Invoke-RestMethod ($Fixture + '/side-proof')
    if ($proof.activeSide -ne $resumedSide -or $proof.sideCount -ne 1) { throw 'Reopening a side chat created a duplicate instead of resuming it' }
    SetValue 'SideComposer' 'NATIVE_SIDE_QUESTION'
    Invoke 'SideSend'
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $sideState = (Find 'SideStatus').Current.Name } while ($sideState -ne 'Done' -and (Get-Date) -lt $deadline)
    $proof = Invoke-RestMethod ($Fixture + '/side-proof')
    if (!$proof.sideNoTools -or !$proof.parentHeld -or $sideState -ne 'Done') { throw 'Independent side conversation did not complete with tools omitted' }
    Capture 'native-side-chat.png'
    Invoke 'Transfer latest reply…' -ByName
    Find 'SideTransferPreview' | Out-Null
    Invoke 'Transfer' -ByName
    $deadline = (Get-Date).AddSeconds(5)
    do { Start-Sleep -Milliseconds 100; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while ($proof.queued -eq 0 -and $proof.transferred -eq 0 -and (Get-Date) -lt $deadline)
    if ($proof.queued -ne 1 -or $proof.transferred -ne 0 -or !$proof.parentHeld) { Capture 'native-side-transfer-failed.png'; throw 'Side transfer interrupted its parent instead of queuing' }
    Invoke 'Close side chat' -ByName
    SelectNav 'Games'
    Invoke 'GameTile1'
    Invoke 'GameCell0'
    if ((Find 'GameCell0').Current.Name -ne 'Cell 1: X') { throw 'Game was not playable while the parent agent waited' }
    Find 'Agent running' -ByName | Out-Null
    Invoke-RestMethod -Method Post ($Fixture + '/release-parent') | Out-Null
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while ($proof.queued -gt 0 -and (Get-Date) -lt $deadline)
    if ($proof.queued -ne 0 -or $proof.transferred -ne 1) { throw 'Side transfer did not append after the safe boundary' }
    Find 'Response ready' -ByName | Out-Null
    Invoke 'GameCell1'
    if ((Find 'GameCell1').Current.Name -ne 'Cell 2: X') { throw 'Agent completion interrupted or reset the game' }
    Capture 'native-game-agent-ready.png'
    Invoke 'Open chat' -ByName
    Invoke 'Side chat' -ByName
    Invoke 'Keep as independent chat' -ByName
    $proof = Invoke-RestMethod ($Fixture + '/side-proof')
    if (!$proof.kept -or $proof.sideCount -ne 0) { throw 'Keep did not preserve the independent chat or remove its expiry' }
    Invoke 'Side chat from this message' -ByName
    Find 'SideComposer' | Out-Null
    $proof = Invoke-RestMethod ($Fixture + '/side-proof')
    if ($proof.sideMessageCount -ne 1) { throw 'Message branch included later conversation messages' }
    SetValue 'SideComposer' 'NATIVE_SIDE_WAIT'
    Invoke 'SideSend'
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 100; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while (!$proof.sideStreamActive -and (Get-Date) -lt $deadline)
    if (!$proof.sideStreamActive) { throw 'Side expiry fixture did not start an active stream' }
    Invoke-RestMethod -Method Post ($Fixture + '/expire-side') | Out-Null
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while ((!$proof.sideCancelled -or $proof.sideCount -gt 0) -and (Get-Date) -lt $deadline)
    if (!$proof.sideCancelled -or $proof.sideCount -ne 0 -or !$proof.kept -or (Find 'SideComposer').Current.IsEnabled) { throw 'Expiry did not cancel/delete the temporary chat and retain the kept one' }
    Invoke 'Close side chat' -ByName
    SelectNav 'Games'
    if ((Find 'GameStatus').Current.Name -notmatch '^Paused') { throw 'Returning to chat did not pause the active game' }
    Invoke 'GameLibraryBack'
    (Find 'GameTile0').SetFocus()
    Capture 'native-game-library.png'
    Invoke 'GameTile0'
    Find 'Restart' -ByName | Out-Null
    foreach ($game in @('Tic-Tac-Toe','2048','Minesweeper','Connect Four','Breakout','Memory Match','Pong','Snake','Lights Out','Simon','Odd Color','Orbit','Reversi','Sudoku','Stack','Color Gauntlet','Maze Chase','Flap','Darts','Jigsaw')) {
        SelectGame $game
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
        if ($game -eq 'Lights Out') {
            $initial = @(0..24 | ForEach-Object { [int]((Find ('GameCell' + $_)).Current.Name -match ': on$') })
            $solution = $null
            # Chase each possible first row; the following row is then forced.
            for ($mask = 0; $mask -lt 32; $mask++) {
                $cells = [int[]]$initial.Clone(); $presses = New-Object 'System.Collections.Generic.List[int]'
                for ($row = 0; $row -lt 5; $row++) { for ($column = 0; $column -lt 5; $column++) {
                    $index = $row * 5 + $column
                    $press = if ($row -eq 0) { ($mask -band (1 -shl $column)) -ne 0 } else { $cells[$index - 5] -eq 1 }
                    if ($press) {
                        $presses.Add($index)
                        foreach ($offset in @(0,-5,5,-1,1)) { $other = $index + $offset; if ($other -ge 0 -and $other -lt 25 -and (($offset -ne -1 -and $offset -ne 1) -or [math]::Floor($other / 5) -eq $row)) { $cells[$other] = $cells[$other] -bxor 1 } }
                    }
                } }
                if (($cells | Measure-Object -Sum).Sum -eq 0) { $solution = $presses; break }
            }
            if ($null -eq $solution) { throw 'Lights Out generated an unsolvable board' }
            foreach ($index in $solution) { Invoke ('GameCell' + $index) }
            if ((Find 'GameStatus').Current.Name -notmatch '^All lights out') { throw 'Lights Out did not recognize its solved board' }
            Capture 'native-lights-out.png'
        }
        if ($game -eq 'Simon') {
            # Catch the next playback from its start, including a deliberate ignored early click.
            Invoke 'Restart' -ByName
            $deadline = (Get-Date).AddSeconds(5); $lit = -1
            do { for ($i=0; $i -lt 4; $i++) { if ((Find ('GameCell' + $i)).Current.Name -match ': lit$') { $lit = $i; break } }; if ($lit -lt 0) { Start-Sleep -Milliseconds 30 } } while ($lit -lt 0 -and (Get-Date) -lt $deadline)
            if ($lit -lt 0) { throw 'Simon did not show its first cue' }
            Invoke ('GameCell' + (($lit + 1) % 4))
            do { Start-Sleep -Milliseconds 50; $state = (Find 'GameStatus').Current.Name } while ($state -notmatch '^Your turn' -and (Get-Date) -lt $deadline)
            if ($state -notmatch '^Your turn') { throw 'Simon never enabled pattern input' }
            Invoke ('GameCell' + $lit)
            if ((Find 'GameStatus').Current.Name -notmatch 'Score 10') { throw 'Simon did not accept the remembered pattern' }
        }
        if ($game -eq 'Odd Color') {
            $tiles = @(0..15 | ForEach-Object { [pscustomobject]@{ Index = $_; Shade = ((Find ('GameCell' + $_)).Current.Name -split ': ')[1] } })
            $odd = $tiles | Group-Object Shade | Where-Object Count -eq 1
            if (!$odd) { throw 'Odd Color did not generate exactly one different tile' }
            Invoke ('GameCell' + $odd.Group[0].Index)
            if ((Find 'GameStatus').Current.Name -notmatch '^Shade 2/20 · Score 100') { throw 'Odd Color did not advance or score the correct choice' }
            Capture 'native-odd-color.png'
            $tiles = @(0..15 | ForEach-Object { [pscustomobject]@{ Index = $_; Shade = ((Find ('GameCell' + $_)).Current.Name -split ': ')[1] } })
            $common = $tiles | Group-Object Shade | Where-Object Count -gt 1
            Invoke ('GameCell' + $common.Group[0].Index)
            if ((Find 'GameStatus').Current.Name -notmatch '^Different shade missed') { throw 'Odd Color did not end after a wrong choice' }
        }
        if ($game -eq 'Orbit') {
            Capture 'native-orbit.png'
            Invoke 'Tap orbit' -ByName
            if ((Find 'GameStatus').Current.Name -eq 'Score 0') { throw 'Orbit did not register its timing input' }
        }
        if ($game -eq 'Reversi') {
            if ((Find 'GameCell19').Current.Name -notmatch 'playable$') { throw 'Reversi did not show a legal opening move' }
            Invoke 'GameCell0'
            if ((Find 'GameCell0').Current.Name -notmatch 'empty$') { throw 'Reversi accepted an illegal corner' }
            Invoke 'GameCell19'
            if ((Find 'GameCell19').Current.Name -notmatch 'coral$') { throw 'Reversi did not apply the opening move' }
            Capture 'native-reversi.png'
        }
        if ($game -eq 'Sudoku') {
            $sudoku = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Sudoku').state
            $square = (0..80 | Where-Object { !$sudoku.Clues[$_] -and @($sudoku.Sudoku[([int][Math]::Floor($_ / 9) * 9)..([int][Math]::Floor($_ / 9) * 9 + 8)] | Where-Object { $_ -gt 0 }).Count -gt 0 } | Select-Object -First 1)
            $row = [int][Math]::Floor($square / 9)
            $conflict = $sudoku.Sudoku[($row * 9)..($row * 9 + 8)] | Where-Object { $_ -gt 0 } | Select-Object -First 1
            Invoke ('GameCell' + $square)
            Invoke ('SudokuDigit' + $conflict)
            if ((Find 'GameStatus').Current.Name -notmatch 'conflicts') { throw 'Sudoku accepted a conflicting row digit' }
            $column = $square % 9
            $used = @(0..80 | Where-Object { $_ % 9 -eq $column -or [Math]::Floor($_ / 9) -eq $row -or ([Math]::Floor(($_ % 9) / 3) -eq [Math]::Floor($column / 3) -and [Math]::Floor($_ / 27) -eq [Math]::Floor($square / 27)) } | ForEach-Object { $sudoku.Sudoku[$_] })
            $valid = 1..9 | Where-Object { $_ -notin $used } | Select-Object -First 1
            Invoke ('SudokuDigit' + $valid)
            if ((Find ('GameCell' + $square)).Current.Name -ne ('Square ' + ($square + 1) + ': ' + $valid)) { throw 'Sudoku rejected a legal digit' }
            Capture 'native-sudoku.png'
        }
        if ($game -eq 'Stack') {
            Invoke 'Place block' -ByName
            if ((Find 'GameStatus').Current.Name -notmatch '^Level 1') { throw 'Stack did not place its first overlapping block' }
            Capture 'native-stack.png'
        }
        if ($game -eq 'Color Gauntlet') {
            if ((Find 'Use color' -ByName).Current.IsEnabled) { throw 'Gauntlet accepted input during memorization' }
            Capture 'native-gauntlet.png'
            $deadline = (Get-Date).AddSeconds(15)
            do { Start-Sleep -Milliseconds 100; $ready = (Find 'Use color' -ByName).Current.IsEnabled } while (!$ready -and (Get-Date) -lt $deadline)
            if (!$ready) { throw 'Gauntlet did not begin the reconstruction phase' }
            Invoke 'Use color' -ByName
            Invoke 'Use color' -ByName
            if ((Find 'GameStatus').Current.Name -notmatch '^Recreate color 3/5') { throw 'Gauntlet did not advance after two guesses' }
        }
        if ($game -eq 'Maze Chase') {
            Invoke 'Move left' -ByName
            Start-Sleep -Milliseconds 400
            Invoke 'Pause / resume' -ByName
            $maze = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Maze Chase').state
            if ($maze.Arcade.MazePlayer -eq 202 -or $maze.Score -le 0) { throw 'Maze Chase did not move and collect a dot' }
            Capture 'native-maze-chase.png'
        }
        if ($game -eq 'Flap') {
            Invoke 'ArcadeAction'
            Start-Sleep -Milliseconds 300
            Invoke 'Pause / resume' -ByName
            $flight = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Flap').state
            if (!$flight.Arcade.FlapStarted -or $flight.Arcade.BirdY -ge 235) { throw 'Flap did not lift the bird' }
            Capture 'native-flap.png'
        }
        if ($game -eq 'Darts') {
            Invoke 'ArcadeAction'
            if ((Find 'GameStatus').Current.Name -notmatch 'Release') { throw 'Darts did not lock horizontal aim' }
            Invoke 'ArcadeAction'
            Invoke 'ArcadeAction'
            if ((Find 'GameStatus').Current.Name -notmatch '^Dart 2/9 · Release') { throw 'Darts did not register the first throw' }
            Capture 'native-darts.png'
        }
        if ($game -eq 'Jigsaw') {
            $firstPiece = ((Find 'GameCell0').Current.Name -split 'Piece ')[1]
            $secondPiece = ((Find 'GameCell1').Current.Name -split 'Piece ')[1]
            Invoke 'GameCell0'
            Invoke 'GameCell1'
            if ((Find 'GameCell0').Current.Name -ne ('Tile 1 · Piece ' + $secondPiece) -or (Find 'GameCell1').Current.Name -ne ('Tile 2 · Piece ' + $firstPiece)) { throw 'Jigsaw did not exchange pieces' }
            Capture 'native-jigsaw.png'
        }
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
    SelectNav 'Settings'
    SetValue 'TextFontFamily' 'Space Grotesk'
    SetValue 'CodeFontFamily' 'Rubik'
    Invoke 'Save typography' -ByName
    SelectNav 'Chats'
    SetValue 'Composer' 'Typography sample'
    $font = (Find 'Composer').GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::FontNameAttribute)
    if ($font -notmatch 'Space Grotesk') { throw ('Cached chat did not update its text font: ' + $font) }
    SetValue 'Composer' ''
    SelectNav 'Terminal'
    $font = (Find 'TerminalOutput').GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::FontNameAttribute)
    if ($font -notmatch 'Rubik') { throw ('Cached terminal did not update its independent code font: ' + $font) }
    Capture 'native-typography-roles.png'
    SelectNav 'Settings'
    SetValue 'TextFontFamily' 'Rubik'
    SetValue 'CodeFontFamily' 'JetBrains Mono'
    Invoke 'Save typography' -ByName
    SelectNav 'Chats'
    Invoke 'Side chat' -ByName
    Find 'SideComposer' | Out-Null
    Invoke 'Close side chat' -ByName
    CloseApp
    Invoke-RestMethod -Method Post ($Fixture + '/expire-side') | Out-Null
    Invoke-RestMethod -Method Post ($Fixture + '/poison-font-sizes') | Out-Null
    Launch
    Find 'Sign out' -ByName | Out-Null
    $proof = Invoke-RestMethod ($Fixture + '/side-proof')
    if ($proof.sideCount -ne 0 -or !$proof.kept) { throw 'Startup did not purge expired side chats while preserving kept history' }
    SelectNav 'Settings'
    Find 'TextFontSize' | Out-Null
    Invoke 'Save typography' -ByName
    $fontProof = Invoke-RestMethod ($Fixture + '/font-proof')
    if ($fontProof.text -ne '14' -or $fontProof.code -ne '14') { throw 'Non-finite font sizes prevented recovery to valid defaults' }
    $savedGames = (Invoke-RestMethod ($Fixture + '/game-proof')).saves
    if ($savedGames.Count -ne 20) { throw 'Not all twenty games were persisted' }
    SelectNav 'Games'
    Invoke 'GameTile1'
    if ((Find 'GameStatus').Current.Name -notmatch '^Paused') { throw 'Restored game started without resume consent' }
    if ((Find 'GameCell0').Current.Name -ne 'Cell 1: X') { throw 'Tic-tac-toe move was lost across process restart' }
    Invoke 'GameCell1'
    if ((Find 'GameCell1').Current.Name -eq 'Cell 2: X') { throw 'Paused game accepted a move' }
    Invoke 'Pause / resume' -ByName
    $empty = 1..8 | Where-Object { (Find ('GameCell' + $_)).Current.Name -match 'empty$' } | Select-Object -First 1
    Invoke ('GameCell' + $empty)
    if ((Find ('GameCell' + $empty)).Current.Name -ne ('Cell ' + ($empty + 1) + ': X')) { throw 'Resumed game did not accept input' }
    foreach ($game in @('2048','Minesweeper','Memory Match','Lights Out','Simon','Odd Color','Orbit')) {
        SelectGame $game
        $saved = ($savedGames | Where-Object game -eq $game).state
        if ($game -in @('2048','Memory Match')) {
            foreach ($i in 0..15) {
                $expected = if ($game -eq '2048') { if ($saved.Board[$i] -gt 0) { [string]$saved.Board[$i] } else { 'empty' } } else { if ($saved.Revealed[$i]) { [string]$saved.Board[$i] } else { '?' } }
                if ((Find ('GameCell' + $i)).Current.Name -ne ('Cell ' + ($i + 1) + ': ' + $expected)) { throw ('Restored board mismatch: ' + $game + ' cell ' + $i) }
            }
        }
        if ($game -eq 'Lights Out' -and (Find 'GameStatus').Current.Name -notmatch '^All lights out') { throw 'Completed game result was lost on restart' }
        if ($game -eq 'Odd Color' -and (Find 'GameStatus').Current.Name -notmatch '^Different shade missed') { throw 'Ended color game was not retained' }
        if ($game -eq 'Simon') {
            if ((Find 'GameStatus').Current.Name -notmatch '^Paused') { throw 'Simon replay advanced without resume' }
            Invoke 'Pause / resume' -ByName
            $deadline = (Get-Date).AddSeconds(5)
            do { Start-Sleep -Milliseconds 100; $state = (Find 'GameStatus').Current.Name } while ($state -notmatch '^Your turn' -and (Get-Date) -lt $deadline)
            if ($state -notmatch '^Your turn') { throw 'Restored Simon timer did not resume' }
            $start = $saved.SimonInput
            for ($i = $start; $i -lt $saved.Sequence.Count; $i++) { Invoke ('GameCell' + $saved.Sequence[$i]) }
            if ((Find 'GameStatus').Current.Name -notmatch 'Score 30') { throw 'Restored Simon sequence did not retain its next round' }
        }
    }
    SelectGame 'Reversi'
    if ((Find 'GameCell19').Current.Name -notmatch 'coral$') { throw 'Reversi move was lost across restart' }
    SelectGame 'Sudoku'
    $savedSudoku = ($savedGames | Where-Object game -eq 'Sudoku').state
    $selected = $savedSudoku.SelectedSquare
    if ((Find ('GameCell' + $selected)).Current.Name -ne ('Square ' + ($selected + 1) + ': ' + $savedSudoku.Sudoku[$selected])) { throw 'Sudoku entered digit was lost across restart' }
    SelectGame 'Stack'
    $savedStack = ($savedGames | Where-Object game -eq 'Stack').state
    if ((Find 'GameStatus').Current.Name -ne ('Paused · Score ' + $savedStack.Score)) { throw 'Stack score was lost across restart' }
    SelectGame 'Color Gauntlet'
    if ((Find 'GameStatus').Current.Name -notmatch '^Paused') { throw 'Gauntlet did not restore paused' }
    Invoke 'Pause / resume' -ByName
    if ((Find 'GameStatus').Current.Name -notmatch '^Recreate color 3/5') { throw 'Gauntlet did not restore the next color' }
    1..3 | ForEach-Object { Invoke 'Use color' -ByName }
    if ((Find 'GameStatus').Current.Name -notmatch '^Gauntlet complete') { throw 'Gauntlet did not finish all five colors' }
    SelectGame 'Maze Chase'
    $savedMaze = ($savedGames | Where-Object game -eq 'Maze Chase').state
    if ((Find 'GameStatus').Current.Name -ne ('Paused · Score ' + $savedMaze.Score)) { throw 'Maze Chase score was lost on restart' }
    SelectGame 'Flap'
    if ((Find 'GameStatus').Current.Name -notmatch '^Paused') { throw 'Flap did not restore paused' }
    Invoke 'Pause / resume' -ByName
    $deadline = (Get-Date).AddSeconds(8)
    do { Start-Sleep -Milliseconds 100; $state = (Find 'GameStatus').Current.Name } while ($state -notmatch '^(Flight ended|Wing clipped)' -and (Get-Date) -lt $deadline)
    if ($state -notmatch '^(Flight ended|Wing clipped)') { throw 'Resumed Flap physics never reached a collision' }
    SelectGame 'Darts'
    Invoke 'Pause / resume' -ByName
    if ((Find 'GameStatus').Current.Name -notmatch '^Dart 2/9 · Release') { throw 'Darts lost the locked axis on restart' }
    1..15 | ForEach-Object { Invoke 'ArcadeAction' }
    if ((Find 'GameStatus').Current.Name -notmatch '^Nine darts thrown') { throw 'Darts did not complete nine throws' }
    SelectGame 'Jigsaw'
    $pieces = [int[]](($savedGames | Where-Object game -eq 'Jigsaw').state.Board[0..15])
    if ((Find 'GameCell0').Current.Name -ne ('Tile 1 · Piece ' + ($pieces[0] + 1))) { throw 'Jigsaw board was lost across restart' }
    Invoke 'Pause / resume' -ByName
    for ($i = 0; $i -lt 16; $i++) {
        if ($pieces[$i] -eq $i) { continue }
        $other = [Array]::IndexOf($pieces, $i)
        Invoke ('GameCell' + $i)
        Invoke ('GameCell' + $other)
        $pieces[$other] = $pieces[$i]; $pieces[$i] = $i
    }
    if ((Find 'GameStatus').Current.Name -notmatch '^Picture complete') { throw 'Jigsaw did not recognize the solved picture' }
    SelectGame 'Snake'
    Invoke 'Restart' -ByName
    $windowPattern = $script:window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    $windowPattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Minimized)
    Start-Sleep -Milliseconds 400
    $windowPattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
    if ((Find 'GameStatus').Current.Name -notmatch '^Paused') { throw 'Losing window activation did not pause the game' }
    SelectNav 'Chats'
    $beforeSnake = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake').state
    Start-Sleep -Seconds 2
    $afterSnake = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake').state
    if ($beforeSnake.Tick -ne $afterSnake.Tick) { throw 'Hidden moving game continued ticking' }
    Invoke-RestMethod -Method Post ($Fixture + '/corrupt-game') | Out-Null
    CloseApp
    Launch
    Find 'Sign out' -ByName | Out-Null
    SelectNav 'Games'
    Invoke 'GameTile0'
    $repaired = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake').state
    if ($repaired.Version -ne 1 -or $repaired.Snake.Count -ne 3) { throw 'Invalid game save did not recover safely' }
    Capture 'native-restored-game.png'
    SelectNav 'Sign out'
    Find 'LoginButton' | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin')) { throw 'Logout retained local session' }
    SetValue 'EmailInput' 'native-fixture@example.com'
    SetValue 'PasswordInput' 'fixture-password'
    Invoke 'LoginButton'
    Find 'Sign out' -ByName | Out-Null
    Find 'Composer' | Out-Null
    $before = (Invoke-RestMethod ($Fixture + '/assertions')).completions
    if (!(Find 'Chats' -ByName).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Re-login left the navigation on Sign out' }
    SetValue 'Composer' 'Native after manual login'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 100; $after = (Invoke-RestMethod ($Fixture + '/assertions')).completions } while ($after -le $before -and (Get-Date) -lt $deadline)
    if ($after -le $before) { throw 'Chat no longer worked after manual logout and login' }
    Invoke-RestMethod -Method Post ($Fixture + '/revoke') | Out-Null
    CloseApp
    Launch
    Find 'LoginButton' | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin')) { throw 'Revoked session was retained after restart' }
    Write-Output 'NATIVE_SMOKE_PASS: native auth, scoped memory, cloud/local profiles, complete stream persistence, command approval/PowerShell 42, side snapshots/queue/expiry, twenty durable games, PTY, WebView2, restart, re-login, revocation'
} finally { CloseApp }
