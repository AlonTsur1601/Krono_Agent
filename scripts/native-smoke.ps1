param([string]$Executable, [string]$Fixture, [string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class KronoWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputData {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, Input[] entries, int size);
    public static bool TypeText(string text) {
        var entries = new Input[text.Length * 2];
        for (int i = 0; i < text.Length; i++) {
            entries[i * 2] = new Input { Type = 1, Data = new InputData { Keyboard = new Keyboard { Scan = text[i], Flags = 4 } } };
            entries[i * 2 + 1] = new Input { Type = 1, Data = new InputData { Keyboard = new Keyboard { Scan = text[i], Flags = 6 } } };
        }
        return SendInput((uint)entries.Length, entries, Marshal.SizeOf(typeof(Input))) == entries.Length;
    }
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
"@
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
function PressEnterOn([string]$Id) {
    $shell = New-Object -ComObject WScript.Shell
    # Activating the main window can take keyboard focus away from the open dialog.
    if ($Id -notin @('AttachmentPath', 'CodeFontFamily', 'ScheduleFirstRun', 'ProviderEndpoint', 'ProviderKey', 'ModelSelector', 'TerminalInput', 'BrowserAddress', 'BrowserFindInput') -and !$shell.AppActivate($script:process.Id)) { throw 'Could not activate the app for keyboard input' }
    if ($Id -eq 'PasswordInput') {
        (Find 'EmailInput').SetFocus()
        Start-Sleep -Milliseconds 150
        $shell.SendKeys('{TAB}')
        Start-Sleep -Milliseconds 150
    } elseif ($Id -eq 'ModelSelector') {
        $picker = Find $Id
        $editType = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)
        $picker.FindFirst([Windows.Automation.TreeScope]::Descendants, $editType).SetFocus()
    } else {
        (Find $Id).SetFocus()
    }
    $shell.SendKeys('{ENTER}')
}
function SetValue([string]$Id, [string]$Value) { $element = Find $Id; $pattern = $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern); $pattern.SetValue($Value) }
function SetEditableChoice([string]$Id, [string]$Value) { $element = Find $Id; $edit = $element.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit))); ($edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue($Value) }
function SelectNav([string]$Name) {
    $element = Find $Name -ByName
    $pattern = $element.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pattern.Current.IsSelected) { return }
    $pattern.Select()
    Start-Sleep -Milliseconds 400
}
function SelectSettingsCategory([string]$Name) {
    $rail = Find 'SettingsCategories'
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Name)
    $item = $rail.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$item) { throw "Settings category not found: $Name" }
    ($item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select()
}
function SelectChoice([string]$Id, [string]$Name) { $box = Find $Id; ($box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand(); $item = Find $Name -ByName; ($item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
function FindOptional([string]$Id) {
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    try { $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition) } catch { if ($_.FullyQualifiedErrorId -notmatch 'ElementNotAvailableException') { throw } }
}
function OpenGameTile([int]$Index, [string]$Choice = 'Continue saved game', [switch]$RequireChoice) {
    Invoke ('GameTile' + $Index)
    $deadline = (Get-Date).AddSeconds(8)
    do {
        if (FindOptional 'GameSaveChoice') { Invoke $Choice -ByName; if ($Choice -ne 'Cancel') { Find 'GameStatus' | Out-Null }; return }
        if (FindOptional 'GameStatus') { if ($RequireChoice) { throw 'A saved game opened without offering a choice' }; return }
        Start-Sleep -Milliseconds 80
    } while ((Get-Date) -lt $deadline)
    throw ('Game did not open: ' + $Index)
}
function SelectGame([string]$Name) {
    $games = @('Snake','Tic-Tac-Toe','2048','Minesweeper','Connect Four','Breakout','Memory Match','Pong','Lights Out','Simon','Odd Color','Orbit','Reversi','Sudoku','Stack','Color Gauntlet','Maze Chase','Flap','Darts','Jigsaw','Block Fit','Falling Blocks','Sand Blocks','Solitaire')
    $index = [Array]::IndexOf($games, $Name)
    if ($index -lt 0) { throw ('Unknown fixture game: ' + $Name) }
    Invoke 'GameLibraryBack'
    OpenGameTile $index
}
function Toggle([string]$Id) { (Find $Id).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 150 }
function WaitColorInput([int]$Round) {
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 100; $ready = (Find 'Use color' -ByName).Current.IsEnabled } while (!$ready -and (Get-Date) -lt $deadline)
    if (!$ready -or (Find 'GameStatus').Current.Name -notmatch "^Recreate color $Round/5") { throw "Gauntlet did not begin reconstruction for round $Round" }
}
function GameCards {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            $ids = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.AutomationId -match '^GameTile[0-9]+$' } | ForEach-Object { $_.Current.AutomationId } | Select-Object -Unique
            return $ids
        } catch { if ($_.FullyQualifiedErrorId -notmatch 'ElementNotAvailableException') { throw }; Start-Sleep -Milliseconds 100 }
    }
}
function AssertGameCards([string[]]$Expected, [string]$Label) {
    $target = ($Expected | Sort-Object) -join ','; $deadline = (Get-Date).AddSeconds(4)
    do { $actual = ((GameCards) | Sort-Object) -join ','; if ($actual -eq $target) { return }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $deadline)
    throw ('Unexpected ' + $Label + ' cards: ' + $actual)
}
function AttachFixture([string]$Path) {
    Invoke 'Attach files…' -ByName
    SetValue 'AttachmentPath' $Path
    Invoke 'Attach path' -ByName
    Find ('Remove ' + [IO.Path]::GetFileName($Path)) -ByName | Out-Null
}
function CloseApp { if ($script:process -and !$script:process.HasExited) { $script:process.CloseMainWindow() | Out-Null; if (!$script:process.WaitForExit(5000)) { Stop-Process -Id $script:process.Id } } }
function Capture([string]$Name) {
    Start-Sleep -Milliseconds 200
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $script:process.Refresh()
        if ($script:process.HasExited) { throw "Krono exited before capturing $Name." }
        $bounds = $script:window.Current.BoundingRectangle
        if (!$bounds.IsEmpty -and $bounds.Width -ge 1 -and $bounds.Height -ge 1) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if ($bounds.IsEmpty -or $bounds.Width -lt 1 -or $bounds.Height -lt 1) {
        throw "Krono did not expose usable window bounds for $Name."
    }
    $bitmap = New-Object Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try {
        if (![KronoWindowCapture]::PrintWindow($script:process.MainWindowHandle, $dc, 2)) {
            throw 'Could not capture the Krono window.'
        }
    } finally { $graphics.ReleaseHdc($dc) }
    $bitmap.Save((Join-Path $ReportDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
function TestWorkspaceInputs {
    SelectNav 'Terminal'
    Start-Sleep -Seconds 2
    Find 'PowerShell' -ByName | Out-Null
    $terminal = Find 'TerminalOutput'
    $shell = New-Object -ComObject WScript.Shell
    if (!$shell.AppActivate($script:process.Id)) { throw 'Could not activate the terminal window' }
    $bounds = $terminal.Current.BoundingRectangle
    [KronoWindowCapture]::SetCursorPos([int]($bounds.Left + 175), [int]($bounds.Top + 18)) | Out-Null
    [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 180
    if (!$terminal.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.IsReadOnly) { throw 'Terminal output can be edited' }
    $shell.SendKeys('6*7{ENTER}')
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $output = Find 'TerminalOutput'; $text = ($output.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).Current.Value } while ($text -notmatch '42' -and (Get-Date) -lt $deadline)
    Capture 'native-terminal.png'
    if ($text -notmatch '42') { throw ('Native PTY did not produce 42. Terminal output: ' + $text) }
    $close = Find 'Close tab' -ByName
    $bounds = $close.Current.BoundingRectangle
    [KronoWindowCapture]::SetCursorPos([int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + $bounds.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 700
    $toolTip = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ToolTip)
    foreach ($tip in $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $toolTip)) {
        if ($tip.Current.Name -match 'Ctrl\s*\+\s*F4') { throw 'The terminal close shortcut hint is still showing' }
    }
    Capture 'native-terminal-close-hover.png'
    SelectNav 'Browser'
    Find 'Search Google or enter an address' -ByName | Out-Null
    Capture 'native-browser-new-tab.png'
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $browserAddress = Find 'BrowserAddress'
    } while (!$browserAddress.Current.IsEnabled -and (Get-Date) -lt $deadline)
    if (!$browserAddress.Current.IsEnabled) { throw 'Browser initialization did not finish' }
    SetValue 'BrowserAddress' ($Fixture + '/probe')
    PressEnterOn 'BrowserAddress'
    Find 'Native browser fixture' -ByName | Out-Null
    $deadline = (Get-Date).AddSeconds(5)
    do { Start-Sleep -Milliseconds 100; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while (!$assertions.browserIsolated -and (Get-Date) -lt $deadline)
    if (!$assertions.browserIsolated) { throw 'Browser fixture did not prove host API / Node isolation' }
    if ($env:KRONO_SMOKE_SCOPE -eq 'workspace-ui') {
        Invoke 'More options' -ByName
        Invoke 'Find on page' -ByName
        SetValue 'BrowserFindInput' 'WebView2'
        PressEnterOn 'BrowserFindInput'
        $deadline = (Get-Date).AddSeconds(5)
        do {
            $selection = (Invoke-RestMethod ($Fixture + '/browser-selection')).browserSelection
            if ($selection -eq 'WebView2') { break }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        if ($selection -ne 'WebView2') { throw 'Enter in Find on page did not select the requested text' }
        Invoke 'More options' -ByName
        Invoke 'History & bookmarks' -ByName
        Find 'BrowserHistory' | Out-Null
        $title = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Native browser fixture')
        $historyEntry = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $title) |
            Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button } | Select-Object -First 1
        if (!$historyEntry) { throw 'The history entry did not display its domain title' }
        Capture 'native-browser-history.png'
        Invoke 'WorkspaceAddTab'
        Find 'Search Google or enter an address' -ByName | Out-Null
        Start-Sleep -Milliseconds 600
        Capture 'native-browser-rounded-home.png'
    }
}
try {
    Launch
    $windowPattern = $script:window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    if ($windowPattern.Current.WindowVisualState -ne [Windows.Automation.WindowVisualState]::Maximized) { throw 'Krono did not open maximized' }
    Find 'EmailInput' | Out-Null
    if ($env:KRONO_SMOKE_SCOPE -in @('workspace-ui', 'arcade-ui', 'attachment-ui', 'menus-ui', 'settings-ui', 'visual-ui', 'navigation-ui', 'dropdown-ui')) {
        SetValue 'EmailInput' 'native-fixture@example.com'
        SetValue 'PasswordInput' 'fixture-password'
        Invoke 'LoginButton'
        Find 'Sign out' -ByName | Out-Null
        if ($env:KRONO_SMOKE_SCOPE -eq 'dropdown-ui') {
            foreach ($theme in @('light', 'dark', 'halloween')) {
                SelectNav 'Settings'
                SelectSettingsCategory 'Appearance'
                SelectChoice 'Appearance' $theme
                SelectNav 'Chats'
                $picker = (Find 'Mode').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
                $picker.Expand()
                Find 'plan' -ByName | Out-Null
                Capture ('native-input-dropdown-' + $theme + '.png')
                $picker.Collapse()
            }
            $mode = (Find 'Mode').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
            $mode.Expand()
            Find 'plan' -ByName | Out-Null
            $games = Find 'Games' -ByName
            $bounds = $games.Current.BoundingRectangle
            if (!(New-Object -ComObject WScript.Shell).AppActivate($script:process.Id)) { throw "Could not activate the dropdown test window" }
            [KronoWindowCapture]::SetCursorPos([int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + $bounds.Height / 2)) | Out-Null
            [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
            [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
            Find 'GameSearch' | Out-Null
            Capture 'native-games-library.png'
            SelectNav 'Browser'
            Find 'Search Google or enter an address' -ByName | Out-Null
            Find 'Add new tab' -ByName | Out-Null
            Capture 'native-browser-search.png'
            Write-Output 'NATIVE_SMOKE_PASS: a single outside click navigates and the browser opens its built-in search page'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'navigation-ui') {
            Find '+ New chat' -ByName | Out-Null
            PressEnterOn 'Nav-chats'
            Start-Sleep -Milliseconds 300
            if (FindOptional 'ContextRail') { throw 'Invoking Chats did not close its sidebar' }
            Capture 'closed-chat-sidebar.png'
            PressEnterOn 'Nav-settings'
            Find 'SettingsCategories' | Out-Null
            Capture 'joined-settings-sidebar.png'
            PressEnterOn 'Nav-settings'
            Start-Sleep -Milliseconds 300
            Find 'SettingsCategories' | Out-Null
            SelectSettingsCategory 'Updates'
            PressEnterOn 'Nav-chats'
            Find '+ New chat' -ByName | Out-Null
            Capture 'joined-chat-sidebar.png'
            PressEnterOn 'Nav-settings'
            $categories = Find 'SettingsCategories'
            $selection = $categories.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
            if ($selection.Count -ne 1 -or $selection[0].Current.Name -ne 'Account') { throw 'Settings did not reopen on Account' }
            Write-Output 'NATIVE_SMOKE_PASS: chat sidebar toggles, Settings keeps its sidebar and reopens on Account'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'visual-ui') {
            if ($env:KRONO_SMOKE_MAXIMIZED -ne '1') {
                $windowPattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
                if (![KronoWindowCapture]::SetWindowPos($script:process.MainWindowHandle, [IntPtr]::Zero, 30, 30, 960, 690, 0x14)) { throw 'Could not resize the test window' }
            }
            if ($env:KRONO_SMOKE_THEME -in @('light', 'dark', 'halloween')) {
                SelectNav 'Settings'
                SelectSettingsCategory 'Appearance'
                SelectChoice 'Appearance' $env:KRONO_SMOKE_THEME
                SelectNav 'Chats'
            }
            Capture 'compact-chat.png'
            Invoke 'Choose model' -ByName
            Find 'Model and context' -ByName | Out-Null
            Capture 'compact-model.png'
            Invoke 'Cancel' -ByName
            SelectNav 'Chats'
            Invoke 'Projects' -ByName
            Find 'ProjectName' | Out-Null
            Capture 'compact-projects.png'
            SelectNav 'Settings'
            SelectSettingsCategory 'Appearance'
            Capture 'compact-appearance.png'
            SelectNav 'Games'
            OpenGameTile 0
            Capture 'compact-snake.png'
            SelectGame 'Flap'
            Capture 'compact-flap.png'
            SelectGame 'Falling Blocks'
            Capture 'compact-falling-blocks.png'
            Write-Output 'NATIVE_SMOKE_PASS: compact layout captures'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'settings-ui') {
            SelectNav 'Settings'
            SelectSettingsCategory 'Appearance'
            Capture 'native-appearance-typography.png'
            foreach ($prefix in @('Text', 'Code')) {
                $family = (Find ($prefix + 'FontFamily')).Current.BoundingRectangle
                $size = (Find ($prefix + 'FontSize')).Current.BoundingRectangle
                if ($family.IsEmpty -or $size.IsEmpty -or [Math]::Abs($family.Top - $size.Top) -gt 2 -or $size.Left -lt $family.Right) {
                    throw "$prefix font and size did not share a row"
                }
            }
            SetValue 'CodeFontFamily' 'Rubik'
            PressEnterOn 'CodeFontFamily'
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $font = (Invoke-RestMethod ($Fixture + '/font-proof')).codeFamily
                if ($font -eq 'Rubik') { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($font -ne 'Rubik') { throw 'Enter did not save the typography settings' }
            SelectNav 'Scheduled tasks'
            Invoke '+ New task' -ByName
            SetValue 'ScheduleName' 'Schedule from Enter'
            SetValue 'SchedulePrompt' 'Keyboard submission fixture'
            (Find 'ScheduleModelOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            SelectChoice 'ScheduleProvider' 'OpenAI'
            SetValue 'ScheduleModel' 'native-fixture-model'
            SetValue 'ScheduleFirstRun' ((Get-Date).AddDays(1).ToString('yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture))
            PressEnterOn 'ScheduleFirstRun'
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $schedule = Invoke-RestMethod ($Fixture + '/schedule-proof')
                if ($schedule.tasks -contains 'Schedule from Enter') { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($schedule.tasks -notcontains 'Schedule from Enter' -or @($schedule.runs).Count -ne 0) {
                throw 'Enter did not save the future schedule without starting a run'
            }
            Find 'Edit task' -ByName | Out-Null
            if (FindOptional 'ScheduleName') { throw 'The schedule summary contains the editor' }
            Capture 'native-schedule-summary.png'
            Invoke 'Edit task' -ByName
            if ((Find 'ScheduleName').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'Schedule from Enter') { throw 'The task editor lost the saved task name' }
            Capture 'native-schedule-editor.png'
            SelectNav 'Settings'
            SelectSettingsCategory 'Models'
            SelectChoice 'Provider' 'Ollama'
            if (FindOptional 'ModelSelector') { throw 'Model settings are visible without a key' }
            SetValue 'ProviderEndpoint' ($Fixture + '/local/v1')
            PressEnterOn 'ProviderEndpoint'
            Find 'Endpoint saved.' -ByName | Out-Null
            SetValue 'ProviderKey' 'fixture-placeholder'
            PressEnterOn 'ProviderKey'
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $saved = Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-credentials/33333333-3333-4333-8333-333333333333/ollama.bin')
                if ($saved) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if (!$saved) { throw 'Enter did not save the fixture credential' }
            $modelVisibility = (Find 'local-fixture-model' -ByName).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
            $modelVisibility.Toggle()
            SelectChoice 'Provider' 'OpenAI'
            SelectChoice 'Provider' 'Ollama'
            $modelVisibility = (Find 'local-fixture-model' -ByName).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
            if ($modelVisibility.Current.ToggleState -ne [Windows.Automation.ToggleState]::Off) { throw 'Model visibility did not persist per provider' }
            SetEditableChoice 'ModelSelector' 'native-keyboard-model'
            PressEnterOn 'ModelSelector'
            SelectNav 'Chats'
            Find 'native-keyboard-model' -ByName | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: Appearance layout, keyboard submission and saved per-provider model visibility'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'menus-ui') {
            Invoke 'Choose model' -ByName
            Find 'Model and context' -ByName | Out-Null
            Find 'Apply' -ByName | Out-Null
            Capture 'native-model-dialog.png'
            if (!(New-Object -ComObject WScript.Shell).AppActivate($script:process.Id)) { throw "Could not activate the popup for pointer input" }
            $bounds = $script:window.Current.BoundingRectangle
            [KronoWindowCapture]::SetCursorPos([int]($bounds.Left + 35), [int]($bounds.Top + 100)) | Out-Null
            [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
            [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 350
            $dialogTitle = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Model and context')
            if ($script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $dialogTitle)) { throw 'Clicking the popup scrim did not cancel the dialog' }
            Find 'Composer' | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: clicking outside the model dialog returns to the composer'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'arcade-ui') {
            SelectNav 'Games'
            OpenGameTile 0
            if ((Find 'GameStatus').Current.Name -ne 'Choose a direction to start') { throw 'Snake did not wait for input' }
            Start-Sleep -Milliseconds 400
            if ((Find 'GameStatus').Current.Name -ne 'Choose a direction to start') { throw 'Snake started before input' }
            Capture 'native-snake-ready.png'
            Invoke 'Move right' -ByName
            Find 'Play again' -ByName | Out-Null
            if ((Find 'GameStatus').Current.Name -notmatch '^Game over') { throw 'Snake did not show Game over' }
            Capture 'native-snake-game-over.png'
            Invoke 'Play again' -ByName
            if ((Find 'GameStatus').Current.Name -ne 'Choose a direction to start') { throw 'Snake replay did not reset to its ready state' }
            SelectGame 'Flap'
            Find 'Press Space or Flap to start' -ByName | Out-Null
            Capture 'native-flap-ready.png'
            Invoke 'ArcadeAction'
            Find 'Play again' -ByName | Out-Null
            if ((Find 'GameStatus').Current.Name -notmatch '^(Flight ended|Wing clipped)') { throw 'Flap did not show the collision result' }
            Invoke 'Play again' -ByName
            Find 'Press Space or Flap to start' -ByName | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: Snake and Flap starting states, game over and replay'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'workspace-ui') {
            TestWorkspaceInputs
            Write-Output 'NATIVE_SMOKE_PASS: terminal, browser start page, history, address navigation, and Enter submission'
            return
        }
        SelectNav 'Chats'
        $attachment = Join-Path $ReportDirectory 'keyboard-attachment.txt'
        [IO.File]::WriteAllText($attachment, 'Keyboard submission fixture')
        Invoke 'Attach files…' -ByName
        SetValue 'AttachmentPath' $attachment
        PressEnterOn 'AttachmentPath'
        Capture 'native-attachment-enter.png'
        Find 'Remove keyboard-attachment.txt' -ByName | Out-Null
        Write-Output 'NATIVE_SMOKE_PASS: attachment path submitted with Enter'
        return
    }
    Capture 'native-login.png'
    SetValue 'EmailInput' 'pending-fixture@example.com'
    SetValue 'PasswordInput' 'fixture-password'
    PressEnterOn 'PasswordInput'
    Find 'Check your email' -ByName | Out-Null
    Invoke 'ResendConfirmationButton'
    Find 'A new confirmation email was requested.' -ByName | Out-Null
    Invoke 'BackToLoginButton'
    SetValue 'PasswordInput' 'fixture-password'
    function AuthFormHeight([string]$title, [string]$footer) {
        $heading = Find $title -ByName
        $button = Find $footer
        $deadline = (Get-Date).AddSeconds(5)
        do {
            $top = $heading.Current.BoundingRectangle
            $bottom = $button.Current.BoundingRectangle
            if (!$top.IsEmpty -and !$bottom.IsEmpty -and $bottom.Bottom -gt $top.Top) {
                return ($bottom.Bottom - $top.Top)
            }
            Start-Sleep -Milliseconds 50
        } while ((Get-Date) -lt $deadline)
        throw "No rendered bounds for $title"
    }
    $signInHeight = AuthFormHeight 'Welcome to Krono' 'RegisterButton'
    Invoke 'RegisterButton'
    Find 'Create your account' -ByName | Out-Null
    $signupHeight = AuthFormHeight 'Create your account' 'BackToSignInButton'
    if ($signInHeight -le 0 -or [Math]::Abs($signInHeight - $signupHeight) -gt 1) {
        throw "Authentication card heights differ: $signInHeight px for sign-in, $signupHeight px for signup"
    }
    Invoke 'CreateAccountButton'
    Capture 'native-auth-busy.png'
    Find 'We sent a confirmation link to:' -ByName | Out-Null
    Capture 'native-email-confirmation.png'
    Invoke-RestMethod -Method Post ($Fixture + '/confirm-signup') | Out-Null
    Find 'Sign out' -ByName | Out-Null
    Invoke '+ New chat' -ByName
    Find 'Send' -ByName | Out-Null
    if (FindOptional 'OpenSideChatButton') { throw 'Side chat was offered before a conversation existed' }
    Find 'Composer' | Out-Null
    SelectNav 'Chats'
    Invoke 'Projects' -ByName
    Find 'No projects created.' -ByName | Out-Null
    Invoke 'New project' -ByName
    SetValue 'ProjectName' 'Native fixture project'
    Invoke 'Add folder…' -ByName
    SetValue 'ProjectFolderPath' $ReportDirectory
    Invoke 'Add folder' -ByName
    Find $ReportDirectory -ByName | Out-Null
    Invoke 'Save project' -ByName
    Invoke 'New project' -ByName
    SetValue 'ProjectName' 'Original fixture project'
    Invoke 'Save project' -ByName
    Invoke-RestMethod -Method Post ($Fixture + '/seed-memory') | Out-Null
    SelectNav 'Settings'
    SelectSettingsCategory 'Appearance'
    foreach ($appearance in @('dark','halloween','light','system')) {
        SelectChoice 'Appearance' $appearance
        if ($appearance -eq 'system') { continue }
        Capture ('native-theme-' + $appearance + '.png')
        $popup = (Find 'Appearance').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
        $popup.Expand()
        Find 'system' -ByName | Out-Null
        Capture ('native-dropdown-' + $appearance + '.png')
        $popup.Collapse()
    }
    SelectSettingsCategory 'Models'
    SetValue 'ProviderKey' 'fixture-provider-key'
    Invoke 'Save key' -ByName
    $model = Find 'ModelSelector'
    $edit = $model.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
    ($edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue('native-fixture-model')
    Invoke 'Use model' -ByName
    SelectNav 'Chats'
    Invoke 'native-fixture-model' -ByName
    Find 'Model and context' -ByName | Out-Null
    if ($env:KRONO_SMOKE_SCOPE -eq 'account-ui') { Capture 'native-model-dialog.png' }
    SetEditableChoice 'ChatModelPicker' 'native-fixture-model'
    SelectChoice 'Conversationcontext' 'Recent messages'
    Invoke 'Apply' -ByName
    Find 'native-fixture-model' -ByName | Out-Null
    SetValue 'Composer' 'Native test message'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 300; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while ($assertions.completions -lt 1 -and (Get-Date) -lt $deadline)
    if (!$assertions.chatHasNoTools) { throw 'Native chat did not reach provider with tools omitted' }
    Find 'OpenSideChatButton' | Out-Null
    if (!$assertions.globalOnly) { throw 'Native unassigned chat memory scope is incorrect' }
    Start-Sleep -Milliseconds 500
    $chatList = Find 'ChatList'
    $selectedChatRow = ($chatList.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection() | Select-Object -First 1
    if (!$selectedChatRow) {
        $itemCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)
        $selectedChatRow = $chatList.FindFirst([Windows.Automation.TreeScope]::Descendants, $itemCondition)
    }
    $chatBounds = $selectedChatRow.Current.BoundingRectangle
    [KronoWindowCapture]::SetCursorPos([int]($chatBounds.Left + $chatBounds.Width / 2), [int]($chatBounds.Top + $chatBounds.Height / 2)) | Out-Null
    [KronoWindowCapture]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)
    [KronoWindowCapture]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
    Invoke 'Delete chat' -ByName
    Find 'Delete this chat?' -ByName | Out-Null
    Invoke 'Cancel' -ByName
    Invoke 'Project…' -ByName
    SelectChoice 'ChatProjectSelector' 'Original fixture project'
    Invoke 'Move' -ByName
    function WaitForChatProject([string]$expected) {
        $deadline = (Get-Date).AddSeconds(10)
        do {
            if ((Invoke-RestMethod ($Fixture + '/chat-project-proof')).project -eq $expected) { return }
            Start-Sleep -Milliseconds 150
        } while ((Get-Date) -lt $deadline)
        throw "The chat move to $expected was not saved"
    }
    WaitForChatProject 'Original fixture project'
    Invoke 'Project…' -ByName
    Capture 'native-move-current-project.png'
    SelectChoice 'ChatProjectSelector' 'Native fixture project'
    Invoke 'Move' -ByName
    WaitForChatProject 'Native fixture project'
    if ($env:KRONO_SMOKE_SCOPE -eq 'account-ui') {
        Invoke 'native-fixture-model' -ByName
        SelectChoice 'ChatModelPicker' 'gpt-5-mini'
        SelectChoice 'Reasoningeffort' 'high'
        Invoke 'Apply' -ByName
        Find 'gpt-5-mini' -ByName | Out-Null
        Invoke-RestMethod -Method Post ($Fixture + '/seed-chat-history') | Out-Null
        SetValue 'Composer' 'Verify the selected reasoning effort'
        Invoke 'Send' -ByName
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $reasoningProof = Invoke-RestMethod ($Fixture + '/reasoning-proof')
            if ($reasoningProof.accepted) { break }
            Start-Sleep -Milliseconds 150
        } while ((Get-Date) -lt $deadline)
        if (!$reasoningProof.accepted) { throw 'The selected model and reasoning effort did not reach the provider' }
        if ($reasoningProof.history.Count -ne 20 -or $reasoningProof.history[0] -ne 'Fixture history message 5' -or $reasoningProof.history[18] -ne 'Fixture history message 23' -or $reasoningProof.history[19] -ne 'Verify the selected reasoning effort') {
            throw 'Recent messages did not send exactly the latest 20 history entries in order'
        }
        Write-Output 'NATIVE_SMOKE_PASS: authentication, settings themes, chat model selection, and project moves'
        return
    }
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
    Capture 'native-workspace.png'
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
    SelectSettingsCategory 'Models'
    SelectChoice 'Provider' 'Ollama'
    SetValue 'ProviderEndpoint' ($Fixture + '/local/v1')
    Invoke 'Save endpoint' -ByName
    Invoke 'Test connection' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $connected = (Find 'ProviderConnectionStatus').Current.Name } while ($connected -notmatch '^Connected' -and (Get-Date) -lt $deadline)
    if ($connected -notmatch '^Connected') { throw 'Native local endpoint connection test failed' }
    if (FindOptional 'ModelSelector') { throw 'Model settings should be hidden for a provider without a key' }
    SetValue 'ProviderKey' 'fixture-local-placeholder'
    Invoke 'Save key' -ByName
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
    SelectSettingsCategory 'Models'
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
    SelectSettingsCategory 'Models'
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
        SelectSettingsCategory 'Models'
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
    SelectSettingsCategory 'Models'
    SelectChoice 'Provider' 'OpenAI'
    SelectNav 'Chats'
    SelectChoice 'Mode' 'chat'
    SetValue 'Composer' 'NATIVE_PARENT_WAIT'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 100; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while (!$proof.parentHeld -and (Get-Date) -lt $deadline)
    if (!$proof.parentHeld) { throw 'Parent fixture stream did not start' }
    $stopButton = Find 'Stop' -ByName
    if (!$stopButton.Current.IsEnabled) { throw 'Empty composer did not offer Stop during a run' }
    SetValue 'Composer' 'Draft during the current run'
    $sendButton = Find 'Send' -ByName
    if ($sendButton.Current.IsEnabled) { throw 'Typing during a run enabled a second send' }
    if (($sendButton.GetRuntimeId() -join ',') -ne ($stopButton.GetRuntimeId() -join ',')) { throw 'Send and Stop used different controls' }
    SetValue 'Composer' ''
    Find 'Stop' -ByName | Out-Null
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
    OpenGameTile 1
    Invoke 'GameCell0'
    Find 'Cell 1: X' -ByName | Out-Null
    Find 'Agent running' -ByName | Out-Null
    Invoke-RestMethod -Method Post ($Fixture + '/release-parent') | Out-Null
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 200; $proof = Invoke-RestMethod ($Fixture + '/side-proof') } while ($proof.queued -gt 0 -and (Get-Date) -lt $deadline)
    if ($proof.queued -ne 0 -or $proof.transferred -ne 1) { throw 'Side transfer did not append after the safe boundary' }
    Find 'Response ready' -ByName | Out-Null
    Invoke 'GameCell1'
    Find 'Cell 2: X' -ByName | Out-Null
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
    AssertGameCards @(0..23 | ForEach-Object { 'GameTile' + $_ }) 'catalog'
    if (FindOptional 'GameSaveChoice') { throw 'A save dialog appeared before selecting a game' }
    0..23 | ForEach-Object { Find ('GamePreviewImage' + $_) | Out-Null }
    OpenGameTile 1 'Cancel' -RequireChoice
    AssertGameCards @(0..23 | ForEach-Object { 'GameTile' + $_ }) 'catalog after cancel'
    SetValue 'GameSearch' 'Gauntlet'
    AssertGameCards @('GameTile15') 'search'
    SetValue 'GameSearch' ''
    Toggle 'FavoriteGame13'
    Toggle 'GameFavoritesFilter'
    AssertGameCards @('GameTile13') 'favorites'
    Toggle 'GameFavoritesFilter'
    SelectChoice 'GameCategory' 'Strategy'
    AssertGameCards @('GameTile1','GameTile4','GameTile12','GameTile23') 'strategy category'
    SelectChoice 'GameCategory' 'All categories'
    (Find 'GameTile0').SetFocus()
    Capture 'native-game-library.png'
    OpenGameTile 0
    Find 'Restart' -ByName | Out-Null
    foreach ($game in @('Tic-Tac-Toe','2048','Minesweeper','Connect Four','Breakout','Memory Match','Pong','Snake','Lights Out','Simon','Odd Color','Orbit','Reversi','Sudoku','Stack','Color Gauntlet','Maze Chase','Flap','Darts','Jigsaw','Block Fit','Falling Blocks','Sand Blocks','Solitaire')) {
        SelectGame $game
        Invoke 'Restart' -ByName
        if ($game -eq 'Tic-Tac-Toe') {
            Invoke 'GameCell0'
            if ((Find 'GameCell0').Current.Name -ne 'Cell 1: X') { throw 'Native tic-tac-toe did not apply the move' }
        }
        if ($game -eq 'Minesweeper') {
            Toggle 'MineFlagMode'
            Invoke 'GameCell63'
            if ((Find 'GameCell63').Current.Name -notmatch 'flagged$') { throw 'Minesweeper flag tool did not mark the tile' }
            Toggle 'MineFlagMode'
            Invoke 'GameCell0'
            if ((Find 'GameStatus').Current.Name -match '^Score 0$') { throw 'Native minesweeper did not reveal the first cell' }
            if ((Find 'GameCell0').Current.Name -notmatch ': [0-8]$') { throw 'Minesweeper did not expose its dug count' }
            Capture 'native-minesweeper.png'
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
            foreach ($index in $solution) {
                $id = 'GameCell' + $index
                $before = (Find $id).Current.Name
                Invoke $id
                $deadline = (Get-Date).AddSeconds(3)
                do {
                    Start-Sleep -Milliseconds 50
                    $after = (Find $id).Current.Name
                } while ($after -eq $before -and (Get-Date) -lt $deadline)
                if ($after -eq $before) { throw "Lights Out did not apply the press on $id" }
            }
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
            if ($state -notmatch '^Your turn') { Capture 'failed-simon.png'; throw ('Simon never enabled pattern input: ' + $state + ' · ' + (Find 'GameStatus').Current.HelpText) }
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
            $deadline = (Get-Date).AddSeconds(3)
            do {
                $status = (Find 'GameStatus').Current.Name
                if ($status -match '^Different shade missed') { break }
                Start-Sleep -Milliseconds 80
            } while ((Get-Date) -lt $deadline)
            if ($status -notmatch '^Different shade missed') { throw 'Odd Color did not end after a wrong choice' }
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
            WaitColorInput 1
            Invoke 'Use color' -ByName
            if ((Find 'GameStatus').Current.Name -notmatch '^Remember color 2/5' -or (Find 'Use color' -ByName).Current.IsEnabled) { throw 'Gauntlet did not hide inputs for the next color' }
            WaitColorInput 2
            Invoke 'Use color' -ByName
            WaitColorInput 3
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
            if ((Find 'GameStatus').Current.Name -notmatch '^Dart 1/9 .* Choose horizontal position') { throw 'Darts did not begin with horizontal aim' }
            Invoke 'ArcadeAction'
            if ((Find 'GameStatus').Current.Name -notmatch 'Choose height') { throw 'Darts did not lock horizontal aim' }
            Invoke 'ArcadeAction'
            if ((Find 'GameStatus').Current.Name -notmatch '^Dart 2/9 .* Choose horizontal position') { throw 'Darts did not return to horizontal aim after throwing' }
            Invoke 'ArcadeAction'
            if ((Find 'GameStatus').Current.Name -notmatch '^Dart 2/9 .* Choose height') { throw 'Darts did not register the first throw' }
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
        if ($game -eq 'Block Fit') {
            Invoke 'GameCell0'
            $fit = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Block Fit').state
            if ($fit.Moves -ne 1 -or @($fit.Board | Where-Object { $_ -gt 0 }).Count -eq 0 -or $fit.Blocks.Tray[0] -ne -1) { throw 'Block Fit did not consume and place its first shape' }
            Capture 'native-block-fit.png'
        }
        if ($game -in @('Falling Blocks','Sand Blocks')) {
            Invoke 'Rotate' -ByName
            Invoke 'HardDrop'
            Invoke 'Pause / resume' -ByName
            $state = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq $game).state
            $expected = if ($game -eq 'Sand Blocks') { 64 } else { 4 }
            if ($state.Moves -ne 1 -or @($state.Blocks.Grid | Where-Object { $_ -gt 0 }).Count -ne $expected) { throw ('Hard drop did not lock the expected cells: ' + $game) }
            Capture ('native-' + $game.Replace(' ', '-').ToLowerInvariant() + '.png')
        }
        if ($game -eq 'Solitaire') {
            Invoke 'SolStock'
            if ((Find 'SolWaste').Current.Name -notmatch '^Waste:') { throw 'Solitaire did not draw from stock' }
            $wasteName = (Find 'SolWaste').Current.Name
            Capture 'native-solitaire.png'
        }
    }
    Invoke 'GameLibraryBack'
    Toggle 'GameRecentFilter'
    if (@(GameCards).Count -ne 8 -or 'GameTile23' -notin @(GameCards)) { throw 'Recent games did not contain the eight latest saves' }
    Toggle 'GameRecentFilter'
    TestWorkspaceInputs
    if (!(Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin'))) { throw 'Encrypted session missing' }
    SelectNav 'Settings'
    SelectSettingsCategory 'Appearance'
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
    SelectSettingsCategory 'Appearance'
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
    SelectSettingsCategory 'Appearance'
    Find 'TextFontSize' | Out-Null
    Invoke 'Save typography' -ByName
    $fontProof = Invoke-RestMethod ($Fixture + '/font-proof')
    if ($fontProof.text -ne '14' -or $fontProof.code -ne '14') { throw 'Non-finite font sizes prevented recovery to valid defaults' }
    $savedGames = (Invoke-RestMethod ($Fixture + '/game-proof')).saves
    if ($savedGames.Count -ne 24) { throw 'Not all twenty-four games were persisted' }
    SelectNav 'Games'
    Toggle 'GameFavoritesFilter'
    AssertGameCards @('GameTile13') 'restored favorites'
    Toggle 'GameFavoritesFilter'
    OpenGameTile 1 -RequireChoice
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
    3..5 | ForEach-Object { WaitColorInput $_; Invoke 'Use color' -ByName }
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
    if ((Find 'GameStatus').Current.Name -notmatch '^Dart 2/9 .* Choose height') { throw 'Darts lost the locked axis on restart' }
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
    SelectGame 'Block Fit'
    $savedFit = ($savedGames | Where-Object game -eq 'Block Fit').state
    $filled = 0..63 | Where-Object { $savedFit.Board[$_] -gt 0 } | Select-Object -First 1
    if ((Find ('GameCell' + $filled)).Current.Name -notmatch 'filled$') { throw 'Block Fit lost the placed piece' }
    foreach ($game in @('Falling Blocks','Sand Blocks')) {
        SelectGame $game
        $savedBlock = ($savedGames | Where-Object game -eq $game).state
        if ((Find 'GameStatus').Current.Name -ne ('Paused · Score ' + $savedBlock.Score)) { throw ('Restored block score mismatch: ' + $game) }
        Invoke 'Pause / resume' -ByName
        Start-Sleep -Milliseconds 1200
        Invoke 'HardDrop'
        Invoke 'Pause / resume' -ByName
        $continued = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq $game).state
        if ($continued.Moves -lt 2) { throw ('Restored falling game did not continue: ' + $game) }
    }
    SelectGame 'Solitaire'
    if ((Find 'SolWaste').Current.Name -ne $wasteName) { throw 'Solitaire waste was lost across restart' }
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
    Invoke-RestMethod -Method Post ($Fixture + '/solitaire-endgame') | Out-Null
    CloseApp
    Launch
    Find 'Sign out' -ByName | Out-Null
    SelectNav 'Games'
    OpenGameTile 0
    $repaired = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake').state
    if ($repaired.Version -ne 1 -or $repaired.Snake.Count -ne 3) { throw 'Invalid game save did not recover safely' }
    Capture 'native-restored-game.png'
    SelectGame 'Solitaire'
    Invoke 'Pause / resume' -ByName
    Invoke 'SolWaste'
    Invoke 'SolFoundation0'
    if ((Find 'GameStatus').Current.Name -ne 'Solitaire complete · Score 520') { throw 'Solitaire did not complete the last suited foundation' }
    Invoke 'GameLibraryBack'
    Find 'PERSONAL BEST  520' -ByName | Out-Null
    OpenGameTile 23 'New game' -RequireChoice
    if ((Find 'GameStatus').Current.Name -notmatch '^Foundations 0/52') { throw 'New game choice did not replace the save' }
    PressEnterOn 'SignOutButton'
    Find 'Sign out of Krono Agent?' -ByName | Out-Null
    Invoke 'Cancel' -ByName
    $dialogTitle = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Sign out of Krono Agent?')
    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $open = $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $dialogTitle)
        $ready = !$open -and (Find 'SignOutButton').Current.IsEnabled
    } while (!$ready -and (Get-Date) -lt $deadline)
    if (!$ready) { throw 'Canceling sign-out did not return control to the current page' }
    Find 'GameStatus' | Out-Null
    if (!(Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin'))) { throw 'Canceling sign-out removed the local session' }
    if (!(Find 'Games' -ByName).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Canceling sign-out lost the selected page' }
    (Find 'SignOutButton').SetFocus()
    Start-Sleep -Milliseconds 500
    PressEnterOn 'SignOutButton'
    Invoke 'Sign out now' -ByName
    Find 'LoginButton' | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin')) { throw 'Logout retained local session' }
    SetValue 'EmailInput' 'native-fixture@example.com'
    SetValue 'PasswordInput' 'fixture-password'
    Start-Sleep -Milliseconds 150
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
    SelectNav 'Settings'
    SelectSettingsCategory 'Account'
    Toggle 'AccountSyncEnabled'
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 150; $sync = (Find 'AccountSyncStatus').Current.Name } while ($sync -notmatch '^Account data is up to date' -and (Get-Date) -lt $deadline)
    if ($sync -notmatch '^Account data is up to date') { throw ('Native account sync did not complete: ' + $sync) }
    $syncProof = Invoke-RestMethod ($Fixture + '/sync-proof')
    if ($syncProof.memory -ne 'Native global memory fixture' -or $syncProof.games -ne 24 -or $syncProof.sideLeaked) { throw 'Native account sync did not upload the expected data scope' }
    Invoke-RestMethod -Method Post ($Fixture + '/sync-remote-memory') | Out-Null
    Invoke-RestMethod -Method Post ($Fixture + '/edit-local-memory') | Out-Null
    SelectNav 'Settings'
    SelectSettingsCategory 'Account'
    Invoke 'Sync now' -ByName
    Find 'Review differences' -ByName | Out-Null
    Invoke 'Review differences' -ByName
    Find 'Resolve account differences' -ByName | Out-Null
    Invoke 'Use cloud' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 150; $sync = (Find 'AccountSyncStatus').Current.Name } while ($sync -notmatch '^Account data is up to date' -and (Get-Date) -lt $deadline)
    if ($sync -notmatch '^Account data is up to date') { throw ('Native conflict choice did not resolve: ' + $sync) }
    if ((Invoke-RestMethod ($Fixture + '/local-memory')).content -ne 'Cloud fixture memory') { throw 'Native conflict choice did not restore the cloud memory' }
    Capture 'native-synced-memory.png'
    SelectNav 'Chats'
    SetValue 'Composer' 'Unsent draft during a scheduled run'
    SelectNav 'Scheduled tasks'
    Invoke '+ New task' -ByName
    SetValue 'ScheduleName' 'Native scheduled fixture'
    SetValue 'SchedulePrompt' 'NATIVE_SCHEDULE_PROOF'
    (Find 'ScheduleModelOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    SelectChoice 'ScheduleProvider' 'OpenAI'
    SetValue 'ScheduleModel' 'native-fixture-model'
    SetValue 'ScheduleFirstRun' ((Get-Date).AddDays(-1).ToString('yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture))
    PressEnterOn 'ScheduleFirstRun'
    if (@((Invoke-RestMethod ($Fixture + '/schedule-proof')).runs).Count -ne 0) { throw 'Scheduled task ran before opting in' }
    Toggle 'ScheduleEnabled'
    Find 'Scheduled result ready' -ByName | Out-Null
    $scheduled = Invoke-RestMethod ($Fixture + '/schedule-proof')
    if ($scheduled.runs.Count -ne 1 -or $scheduled.runs[0].state -ne 'completed' -or $scheduled.response -ne 'Native scheduled result.') { throw 'Due scheduled task did not complete exactly once' }
    Find 'ScheduleHistory' | Out-Null
    Find 'Open run conversation' -ByName | Out-Null
    Capture 'native-scheduled-task.png'
    Invoke 'Open run conversation' -ByName
    if ((Find 'Composer').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'Unsent draft during a scheduled run') { throw 'Scheduled task overwrote the unsent draft' }
    $attachment = Join-Path $ReportDirectory 'fixture-attachment.txt'
    [IO.File]::WriteAllText($attachment, ('ATTACHMENT_PROOF_TEXT ' + (-join [char[]](0x05E9, 0x05DC, 0x05D5, 0x05DD))), [Text.UTF8Encoding]::new($false))
    AttachFixture $attachment
    Capture 'native-attachment.png'
    SelectChoice 'Mode' 'chat'
    SetValue 'Composer' 'Use the selected fixture file'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 150; $attachmentProof = Invoke-RestMethod ($Fixture + '/attachment-proof') } while (!$attachmentProof.accepted -and (Get-Date) -lt $deadline)
    if (!$attachmentProof.accepted) { throw 'Attachment content did not reach Chat mode without its local path or tools' }
    Invoke-RestMethod -Method Post ($Fixture + '/revoke') | Out-Null
    CloseApp
    Launch
    Find 'LoginButton' | Out-Null
    if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-session.bin')) { throw 'Revoked session was retained after restart' }
    Write-Output 'NATIVE_SMOKE_PASS: native auth, account sync/conflict UI, scheduled result, scoped memory, cloud/local profiles, complete streams, command approval, side chats, 24 games, PTY, WebView2, restart and revocation'
} catch {
    Write-Output $_.ScriptStackTrace
    throw
} finally { CloseApp }
