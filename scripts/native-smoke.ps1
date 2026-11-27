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
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
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
    if ($Id -notin @('AttachmentPath', 'ScheduleFirstRun', 'ProviderEndpoint', 'ProviderKey', 'ModelSelector', 'TerminalInput', 'BrowserAddress', 'BrowserFindInput') -and !$shell.AppActivate($script:process.Id)) { throw 'Could not activate the app for keyboard input' }
    if ($Id -eq 'PasswordInput') {
        (Find 'EmailInput').SetFocus()
        Start-Sleep -Milliseconds 150
        $shell.SendKeys('{TAB}')
        Start-Sleep -Milliseconds 150
    } else {
        (Find $Id).SetFocus()
    }
    $shell.SendKeys('{ENTER}')
}
function SetValue([string]$Id, [string]$Value) { $element = Find $Id; $pattern = $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern); $pattern.SetValue($Value) }
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
function SelectChoice([string]$Id, [string]$Name) {
    $box = Find $Id
    $popup = $box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Name)
    $deadline = (Get-Date).AddSeconds(10)
    do {
        if ($popup.Current.ExpandCollapseState -ne [Windows.Automation.ExpandCollapseState]::Expanded) { $popup.Expand() }
        foreach ($item in $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)) {
            $selection = $null
            if (!$item.Current.IsOffscreen -and $item.Current.ClassName -like '*ComboBoxItem' -and $item.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
                $selection.Select()
                $selected = $box.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
                if ($selected.Count -eq 1 -and $selected[0].Current.Name -eq $Name) {
                    if ($popup.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Expanded) { $popup.Collapse() }
                    return
                }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Choice '$Name' did not appear in '$Id'"
}
function UseFixtureModel {
    SelectNav 'Settings'
    SelectSettingsCategory 'Models'
    # Make the cloud fixture selection explicit even when OpenAI is already displayed during local discovery.
    SelectChoice 'Provider' 'Anthropic'
    SelectChoice 'Provider' 'OpenAI'
    SetValue 'ProviderKey' 'fixture-provider-key'
    Invoke 'Save key' -ByName
    $model = Find 'ModelSelector'
    $selection = $model.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern)
    $deadline = (Get-Date).AddSeconds(10)
    do {
        if ($selection.Current.GetSelection().Count -gt 0) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    SelectChoice 'ModelSelector' 'gpt-native-fixture-model'
}
function TestLiveMarkdown {
    SelectSettingsCategory 'Instructions'
    $rtlWord = "$([char]0x05E2)$([char]0x05D5)$([char]0x05DC)$([char]0x05DD)"
    $markdown = "# Title`n`n**Bold phrase** and *italic phrase*`n`n++Underlined phrase++`n`n**outer _inner_ end**`n`n$([char]0x05E9)$([char]0x05DC)$([char]0x05D5)$([char]0x05DD) $([char]::ConvertFromUtf32(0x1F642)) marker|end`n`n$rtlWord"
    Invoke 'Edit global instructions' -ByName
    SetValue 'GlobalInstructions' $markdown
    Invoke 'Save instructions' -ByName
    SelectSettingsCategory 'Appearance'
    SetToggle 'LiveMarkdownEditing' On
    SelectSettingsCategory 'Instructions'
    Invoke 'Edit global instructions' -ByName
    $editor = Find 'GlobalInstructions'
    $pattern = $editor.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern)
    $bold = $null
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $bold = $pattern.DocumentRange.FindText('Bold phrase', $false, $false)
        if ($bold -and $bold.GetAttributeValue([Windows.Automation.TextPattern]::FontWeightAttribute) -ge 600) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if (!$bold -or $bold.GetAttributeValue([Windows.Automation.TextPattern]::FontWeightAttribute) -lt 600) { throw 'Bold Markdown was not rendered inside the editor' }
    $italic = $pattern.DocumentRange.FindText('italic phrase', $false, $false)
    if (!$italic -or $italic.GetAttributeValue([Windows.Automation.TextPattern]::IsItalicAttribute) -ne $true) { throw 'Italic Markdown was not rendered' }
    $underline = $pattern.DocumentRange.FindText('Underlined phrase', $false, $false)
    if (!$underline -or $underline.GetAttributeValue([Windows.Automation.TextPattern]::UnderlineStyleAttribute).ToString() -eq 'None') { throw 'Underline Markdown was not rendered' }
    $nested = $pattern.DocumentRange.FindText('inner', $false, $false)
    if (!$nested -or $nested.GetAttributeValue([Windows.Automation.TextPattern]::FontWeightAttribute) -lt 600 -or
        $nested.GetAttributeValue([Windows.Automation.TextPattern]::IsItalicAttribute) -ne $true) { throw 'Nested Markdown lost its enclosing style' }
    Capture 'native-markdown-editor.png'
    if (!(New-Object -ComObject WScript.Shell).AppActivate($script:process.Id)) { throw 'Could not activate the live Markdown editor' }
    $editor.SetFocus()
    $caret = $bold.Clone()
    $caret.MoveEndpointByRange([Windows.Automation.Text.TextPatternRangeEndpoint]::End, $bold, [Windows.Automation.Text.TextPatternRangeEndpoint]::Start)
    $caret.Move([Windows.Automation.Text.TextUnit]::Character, 3) | Out-Null
    $caret.Select()
    Capture 'native-markdown-caret.png'
    (New-Object -ComObject WScript.Shell).SendKeys('X')
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $typed = $pattern.DocumentRange.FindText('BolXd phrase', $false, $false)
        if ($typed -and $typed.GetAttributeValue([Windows.Automation.TextPattern]::FontWeightAttribute) -ge 600) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if (!$typed -or $typed.GetAttributeValue([Windows.Automation.TextPattern]::FontWeightAttribute) -lt 600) { throw ('Typing did not preserve the live Markdown formatting. Editor: ' + ($pattern.DocumentRange.GetText(-1) | ConvertTo-Json -Compress)) }
    $markdown = $markdown.Replace('Bold phrase', 'BolXd phrase')
    $pattern = (Find 'GlobalInstructions').GetCurrentPattern([Windows.Automation.TextPattern]::Pattern)
    $target = $pattern.DocumentRange.FindText('marker', $false, $false)
    if (!$target) { throw 'The mixed-direction Markdown test line was missing' }
    $caret = $target.Clone()
    $caret.MoveEndpointByRange([Windows.Automation.Text.TextPatternRangeEndpoint]::End, $target, [Windows.Automation.Text.TextPatternRangeEndpoint]::Start)
    $caret.Move([Windows.Automation.Text.TextUnit]::Character, 3) | Out-Null
    $caret.Select()
    (New-Object -ComObject WScript.Shell).SendKeys(' ')
    Start-Sleep -Milliseconds 220
    (New-Object -ComObject WScript.Shell).SendKeys('7')
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $typed = $pattern.DocumentRange.FindText('mar 7ker|end', $false, $false)
        if (!$typed) { Start-Sleep -Milliseconds 100 }
    } while (!$typed -and (Get-Date) -lt $deadline)
    if (!$typed) { throw 'The character after a space moved in the mixed-direction Markdown line' }
    $markdown = $markdown.Replace('marker|end', 'mar 7ker|end')
    $rtlCaret = $pattern.DocumentRange.FindText($rtlWord, $false, $false)
    $rtlCaret.MoveEndpointByRange([Windows.Automation.Text.TextPatternRangeEndpoint]::Start, $rtlCaret, [Windows.Automation.Text.TextPatternRangeEndpoint]::End)
    $rtlCaret.Select()
    (New-Object -ComObject WScript.Shell).SendKeys(' ')
    Start-Sleep -Milliseconds 220
    (New-Object -ComObject WScript.Shell).SendKeys('7')
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $typed = $pattern.DocumentRange.FindText($rtlWord + ' 7', $false, $false)
        if (!$typed) { Start-Sleep -Milliseconds 100 }
    } while (!$typed -and (Get-Date) -lt $deadline)
    if (!$typed) { throw 'The character after a trailing space moved in the Hebrew Markdown line' }
    $markdown = $markdown.Replace($rtlWord, $rtlWord + ' 7')
    Invoke 'Save instructions' -ByName
    SelectSettingsCategory 'Appearance'
    SetToggle 'LiveMarkdownEditing' Off
    SelectSettingsCategory 'Instructions'
    Invoke 'Edit global instructions' -ByName
    $editor = Find 'GlobalInstructions'
    $value = $editor.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $plain = $value.Current.Value
    if (($plain -replace "`r`n?", "`n") -ne $markdown) { throw ('Toggling live Markdown changed the editable source from ' + ($markdown | ConvertTo-Json -Compress) + ' to ' + ($plain | ConvertTo-Json -Compress)) }
    $before = 'abcdefghijklmnopqrstuvwxyz' * 2
    SetValue 'GlobalInstructions' ($before + '|end')
    $editor.SetFocus()
    Start-Sleep -Milliseconds 300
    $pattern = $editor.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern)
    $marker = $pattern.DocumentRange.FindText('|', $false, $false).GetBoundingRectangles()
    [KronoWindowCapture]::SetCursorPos([int][Math]::Ceiling($marker[0].Left), [int]($marker[0].Top + $marker[0].Height / 2)) | Out-Null
    [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    (New-Object -ComObject WScript.Shell).SendKeys(' ')
    Start-Sleep -Milliseconds 220
    (New-Object -ComObject WScript.Shell).SendKeys('7')
    $deadline = (Get-Date).AddSeconds(3)
    while ($value.Current.Value -eq ($before + '|end') -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }
    if ($value.Current.Value -cne ($before + ' 7|end')) {
        Capture 'native-caret-click-failure.png' -Screen
        throw "Instructions caret click at $($marker[0]) in $($editor.Current.BoundingRectangle) produced '$($value.Current.Value)'"
    }
    SetValue 'GlobalInstructions' $markdown
    Invoke 'Save instructions' -ByName
    Capture 'native-markdown-saved.png'
}
function OpenAdvancedModel {
    Invoke 'Choose model' -ByName
    Find 'Advanced…' -ByName | Out-Null
    Invoke 'Advanced…' -ByName
    Find 'ChatModelPicker' | Out-Null
}
function WaitForBounds([string]$Id) {
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $element = FindOptional $Id
        if ($element) {
            $bounds = $element.Current.BoundingRectangle
            if (!$bounds.IsEmpty -and $bounds.Width -gt 0 -and $bounds.Height -gt 0) { return $bounds }
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "No screen bounds for $Id"
}
function RightClick([string]$Id) {
    $bounds = WaitForBounds $Id
    [KronoWindowCapture]::SetCursorPos([int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + $bounds.Height / 2)) | Out-Null
    [KronoWindowCapture]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)
    [KronoWindowCapture]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
}
function DragGameCell([int]$From, [int]$To) {
    $source = WaitForBounds ('GameCell' + $From)
    $target = WaitForBounds ('GameCell' + $To)
    $x = $source.Left + $source.Width / 2
    $y = $source.Top + $source.Height / 2
    $dx = $target.Left + $target.Width / 2 - $x
    $dy = $target.Top + $target.Height / 2 - $y
    [KronoWindowCapture]::SetCursorPos([int]$x, [int]$y) | Out-Null
    [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    try {
        foreach ($fraction in @(0.25, 0.5, 0.75, 1.0)) {
            Start-Sleep -Milliseconds 30
            [KronoWindowCapture]::SetCursorPos([int]($x + $dx * $fraction), [int]($y + $dy * $fraction)) | Out-Null
        }
    } finally { [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
}
function WaitTicTurn {
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $state = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Tic-Tac-Toe').state
        if ($null -ne $state -and $state.BotAt -eq 0) {
            return (1..8 | Where-Object { $state.Board[$_] -eq 0 } | Select-Object -First 1)
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw 'Tic-tac-toe did not finish its bot turn'
}
function FindOptional([string]$Id) {
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    try { $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition) } catch { if ($_.FullyQualifiedErrorId -notmatch 'ElementNotAvailableException') { throw } }
}
function OpenGameTile([int]$Index, [string]$Choice = 'Load save', [switch]$RequireChoice) {
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
function SetToggle([string]$Id, [Windows.Automation.ToggleState]$State) {
    $deadline = (Get-Date).AddSeconds(5)
    do {
        try {
            $element = Find $Id
            $pattern = $element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
            if ($pattern.Current.ToggleState -eq $State) { return }
            $pattern.Toggle()
        } catch {
            if ($_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException] -and
                $_.Exception.InnerException -isnot [ObjectDisposedException]) { throw }
        }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)
    throw "Toggle '$Id' did not reach '$State'"
}
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
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $submit = Find 'Attach path' -ByName
        if ($submit.Current.IsEnabled) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if (!$submit.Current.IsEnabled) { throw 'The attachment path did not enable submission' }
    $submit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Find ('Remove ' + [IO.Path]::GetFileName($Path)) -ByName | Out-Null
}
function CloseApp {
    if (!$script:process -or $script:process.HasExited) { return }
    $script:process.CloseMainWindow() | Out-Null
    if (!$script:process.WaitForExit(5000)) {
        Stop-Process -InputObject $script:process
        if (!$script:process.WaitForExit(5000)) { throw 'Native process did not exit' }
    }
}
function Capture([string]$Name, [switch]$Screen) {
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
    if ($Screen) {
        $graphics.CopyFromScreen([int]$bounds.Left, [int]$bounds.Top, 0, 0, $bitmap.Size)
    } else {
        $dc = $graphics.GetHdc()
        try {
            if (![KronoWindowCapture]::PrintWindow($script:process.MainWindowHandle, $dc, 2)) {
                throw 'Could not capture the Krono window.'
            }
        } finally { $graphics.ReleaseHdc($dc) }
    }
    $bitmap.Save((Join-Path $ReportDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
function TestWorkspaceInputs {
    $shell = New-Object -ComObject WScript.Shell
    if (!$shell.AppActivate($script:process.Id)) { throw 'Could not activate the terminal window' }
    SelectNav 'Terminal'
    Start-Sleep -Seconds 2
    $currentTerminal = FindOptional 'TerminalOutput'
    if (!$currentTerminal -or $currentTerminal.Current.Name -ne 'PowerShell terminal') { Invoke 'WorkspaceAddTab' }
    $terminal = Find 'PowerShell terminal' -ByName
    $deadline = (Get-Date).AddSeconds(5)
    while (!$terminal.Current.HasKeyboardFocus -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!$terminal.Current.HasKeyboardFocus) { throw 'The terminal did not receive focus automatically' }
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
    if ($env:KRONO_SMOKE_SCOPE -eq 'runtime-workspaces') {
        Invoke 'Close tab' -ByName
        SelectNav 'Chats'
        SelectNav 'Terminal'
        $replacement = Find 'TerminalOutput'
        if (($replacement.GetRuntimeId() -join ',') -eq ($output.GetRuntimeId() -join ',')) { throw 'Terminal reused its closed session' }
    }
    SelectNav 'Browser'
    Find 'Search Google or enter an address' -ByName | Out-Null
    1..3 | ForEach-Object { Invoke 'Reload' -ByName }
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
    if ($env:KRONO_SMOKE_SCOPE -in @('workspace-ui', 'runtime-workspaces')) {
        if (!(New-Object -ComObject WScript.Shell).AppActivate($script:process.Id)) { throw 'Could not activate the browser for pointer input' }
        $addressControl = Find 'BrowserAddress'
        $address = $addressControl.Current.BoundingRectangle
        $windowBounds = $script:window.Current.BoundingRectangle
        $pageX = [int]($address.Left + $address.Width / 2)
        # The fixture link covers the browser document below its heading.
        $pageY = [int](($address.Bottom + $windowBounds.Bottom) / 2)
        $deadline = (Get-Date).AddSeconds(5)
        $handle = $script:process.MainWindowHandle
        [KronoWindowCapture]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x13) | Out-Null
        try {
            if ([KronoWindowCapture]::GetForegroundWindow() -ne $handle) { throw 'The browser lost foreground focus before the pointer test' }
            # Leave the link before re-entering it, even if the cursor was already over it at navigation.
            [KronoWindowCapture]::SetCursorPos($pageX, [int]$address.Top) | Out-Null
            Start-Sleep -Milliseconds 100
            if (![KronoWindowCapture]::SetCursorPos($pageX, $pageY)) { throw 'Could not move the pointer into the browser document' }
            [KronoWindowCapture]::mouse_event(1, 2, 2, 0, [UIntPtr]::Zero)
            do {
                $pointer = (Invoke-RestMethod ($Fixture + '/browser-pointer-proof')).browserPointerReached
                $destination = $addressControl.Current.ItemStatus
                if ($pointer -and $destination -like '*/fixture-destination') { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
        } finally { [KronoWindowCapture]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x13) | Out-Null }
        if (!$pointer -or $destination -notlike '*/fixture-destination') {
            Capture 'browser-pointer-window.png'
            if (!$pointer) { throw "The fixture did not receive the pointer on its browser link at screen point ($pageX, $pageY)" }
            throw 'The page received pointer input but the browser link destination was missing'
        }
        Capture 'native-browser-link-status.png'
        Invoke 'BrowserMoreOptions'
        Invoke 'BrowserMenuFind'
        SetValue 'BrowserFindInput' 'WebView2'
        PressEnterOn 'BrowserFindInput'
        $deadline = (Get-Date).AddSeconds(5)
        do {
            $selection = (Invoke-RestMethod ($Fixture + '/browser-selection')).browserSelection
            if ($selection -eq 'WebView2') { break }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        if ($selection -ne 'WebView2') { throw 'Enter in Find on page did not select the requested text' }
        Invoke 'Add bookmark' -ByName
        Invoke 'Save bookmark' -ByName
        Find 'Edit bookmark' -ByName | Out-Null
        Invoke 'BrowserMoreOptions'
        Invoke 'BrowserMenuClock'
        Find 'BrowserHistory' | Out-Null
        $buttons = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)
        $historyEntry = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $buttons) |
            Where-Object { $_.Current.Name -match '^Native browser fixture, .*[0-9]' } | Select-Object -First 1
        if (!$historyEntry) { throw 'The history entry did not display its domain title and time' }
        Capture 'native-browser-history.png'
        Invoke 'WorkspaceAddTab'
        Find 'Search Google or enter an address' -ByName | Out-Null
        Start-Sleep -Milliseconds 600
        Capture 'native-browser-rounded-home.png'
    }
}
function TestGamePreviews {
    $names = 0..23 | ForEach-Object { (Find ('GameTile' + $_)).Current.Name }
    try {
        for ($index = 0; $index -lt $names.Count; $index++) {
            $tile = 'GameTile' + $index
            $card = FindOptional $tile
            if (!$card -or $card.Current.ItemStatus -ne 'Board preview available') {
                # Filtering brings lazy previews into view at any window width.
                SetValue 'GameSearch' $names[$index]
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    $ready = (Find $tile).Current.ItemStatus -eq 'Board preview available'
                    if ($ready) { break }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                if (!$ready) {
                    Capture 'native-game-preview-failure.png'
                    throw "The board preview for '$($names[$index])' did not render"
                }
            }
        }
    } finally {
        SetValue 'GameSearch' ''
    }
    (Find 'GameTile0').SetFocus()
}
function TestGameSaveLifecycle {
    SelectNav 'Games'
    TestGamePreviews
    $ticBounds = WaitForBounds 'GameTile1'
    $defaultWidth = $ticBounds.Width
    if ([Math]::Abs($ticBounds.Top - (WaitForBounds 'GameTile0').Top) -gt 2) { throw 'Adding a difficulty slider shifted a game card out of its row' }
    $sliders = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Bot difficulty')))
    $ranges = @($sliders | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Slider })
    if ($ranges.Count -ne 5) { throw 'Five bot games did not expose independent difficulty sliders' }
    Toggle 'FavoriteGame1'
    Toggle 'GameFavoritesFilter'
    AssertGameCards @('GameTile1') 'favorites'
    if ([Math]::Abs((WaitForBounds 'GameTile1').Width - $defaultWidth) -gt 2) { throw 'A favorite card grew wider than the catalog cards' }
    Toggle 'GameFavoritesFilter'
    Toggle 'FavoriteGame1'
    Capture 'native-game-library-cards.png'
    OpenGameTile 0
    if ((Find 'GameStatus').Current.Name -like 'Paused*') { Invoke 'Pause / resume' -ByName }
    Invoke 'Move left' -ByName
    Invoke 'Restart' -ByName
    Invoke 'Move left' -ByName
    Invoke 'GameLibraryBack'
    $saved = (Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake'
    if ($saved) { throw 'Entering, rejecting an invalid move or restarting an unplayed game created a save' }

    OpenGameTile 0
    if ((Find 'GameStatus').Current.Name -like 'Paused*') { Invoke 'Pause / resume' -ByName }
    Invoke 'Move right' -ByName
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $played = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake').state
        if (!$played) { Start-Sleep -Milliseconds 100 }
    } while (!$played -and (Get-Date) -lt $deadline)
    if (!$played -or (!$played.Turned -and $played.Tick -le 0)) { throw 'A played round did not save progress' }
    if ((Find 'GameStatus').Current.Name -notlike 'Paused*') { Invoke 'Pause / resume' -ByName }
    Invoke 'GameLibraryBack'
    OpenGameTile 0 'New game' -RequireChoice
    Invoke 'GameLibraryBack'
    if ((Invoke-RestMethod ($Fixture + '/game-proof')).saves.game -contains 'Snake') { throw 'New game kept the previous save' }
    OpenGameTile 0
    Invoke 'Move right' -ByName
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $saved = (Invoke-RestMethod ($Fixture + '/game-proof')).saves.game -contains 'Snake'
        if (!$saved) { Start-Sleep -Milliseconds 100 }
    } while (!$saved -and (Get-Date) -lt $deadline)
    if (!$saved) { throw 'A replacement save was not created after a move' }
    Invoke 'Restart' -ByName
    Invoke 'Move left' -ByName
    Invoke 'GameLibraryBack'
    $after = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Snake').state
    if ($after) { throw 'Restarting kept the previous save or created an untouched save' }
}
function ReopenTrayWorkspace([IntPtr]$Hwnd) {
    $relaunch = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
    try {
        if (!$relaunch.WaitForExit(10000) -or $relaunch.ExitCode -ne 0) { throw 'Reopening Krono did not redirect to the running tray process' }
        $deadline = (Get-Date).AddSeconds(5)
        while (![KronoWindowCapture]::IsWindowVisible($Hwnd) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (![KronoWindowCapture]::IsWindowVisible($Hwnd)) { throw 'The original tray window did not restore' }
    } finally { if (!$relaunch.HasExited) { Stop-Process -InputObject $relaunch } }
}
function TestAgentBrowser {
    UseFixtureModel
    SelectNav 'Chats'
    SelectChoice 'Mode' 'agent'
    SetValue 'Composer' 'NATIVE_BROWSER_PROOF'
    Invoke 'Send' -ByName
    Invoke 'Allow once' -ByName
    Find 'AgentBrowserTab' | Out-Null
    Invoke 'Allow once' -ByName
    Find 'Native agent browser verified.' -ByName | Out-Null
    $proof = Invoke-RestMethod ($Fixture + '/agent-browser-proof')
    if (!$proof.agentBrowserRan -or !$proof.profilesSeparate -or !$proof.agentCookiePresent) { throw 'Agent browser visit, link follow, or profile isolation failed' }
}
try {
    if ($env:KRONO_SMOKE_SCOPE -eq 'plugins-install-ui') {
        Add-Type -OutputAssembly (Join-Path $ReportDirectory 'gh.exe') -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.Threading;
public static class PluginFixture {
    public static int Main() {
        Console.WriteLine("one-time code KRNO-TEST");
        Thread.Sleep(15000);
        return 1;
    }
}
'@
        $env:PATH = "$ReportDirectory;$env:PATH"
    }
    Launch
    $windowPattern = $script:window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    if ($windowPattern.Current.WindowVisualState -ne [Windows.Automation.WindowVisualState]::Maximized) { throw 'Krono did not open maximized' }
    Find 'EmailInput' | Out-Null
    if ($env:KRONO_SMOKE_SCOPE -in @('workspace-ui', 'runtime-workspaces', 'games-lifecycle', 'tray-lifecycle', 'arcade-ui', 'attachment-ui', 'menus-ui', 'settings-ui', 'visual-ui', 'navigation-ui', 'dropdown-ui', 'plugins-ui', 'local-model-ui', 'markdown-ui', 'browser-feedback', 'plugins-layout', 'plugins-install-ui', 'appearance-layout', 'dropdown-empty-ui', 'desktop-notices', 'project-list-ui')) {
        SetValue 'EmailInput' 'native-fixture@example.com'
        SetValue 'PasswordInput' 'fixture-password'
        Invoke 'LoginButton'
        Find 'Sign out' -ByName | Out-Null
        if ($env:KRONO_SMOKE_SCOPE -eq 'project-list-ui') {
            SelectNav 'Chats'
            Invoke 'Projects' -ByName
            foreach ($name in @('Alpha project', 'Zulu project')) {
                Invoke 'New project' -ByName
                Find 'Create project' -ByName | Out-Null
                SetValue 'ProjectName' $name
                Invoke 'Save project' -ByName
                Find ('New chat in ' + $name) -ByName | Out-Null
            }
            function ProjectBounds([string]$label) {
                $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $label)
                $deadline = (Get-Date).AddSeconds(5)
                do {
                    try {
                        $elements = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
                        $visible = $elements | Where-Object { !$_.Current.IsOffscreen } |
                            Sort-Object { $_.Current.BoundingRectangle.Left } -Descending | Select-Object -First 1
                        if ($visible) { return $visible.Current.BoundingRectangle }
                    } catch {
                        if ($_.FullyQualifiedErrorId -notmatch 'ElementNotAvailableException') { throw }
                    }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                throw "Project or chat row not visible: $label"
            }
            function DragProject($from, $to, [bool]$before, [string]$preview = '') {
                (New-Object -ComObject WScript.Shell).AppActivate($script:process.Id) | Out-Null
                $x = [int]($from.Left + $from.Width / 2)
                $y = [int]($from.Top + $from.Height / 2)
                $targetX = [int]($to.Left + $to.Width / 2)
                $targetY = [int]($to.Top + $to.Height * $(if ($before) { 0.2 } else { 0.8 }))
                [KronoWindowCapture]::SetCursorPos($x, $y) | Out-Null
                Start-Sleep -Milliseconds 80
                [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
                try {
                    foreach ($step in 1..12) {
                        [KronoWindowCapture]::SetCursorPos([int]($x + ($targetX - $x) * $step / 12), [int]($y + ($targetY - $y) * $step / 12)) | Out-Null
                        [KronoWindowCapture]::mouse_event(1, 0, 2, 0, [UIntPtr]::Zero)
                        Start-Sleep -Milliseconds 35
                    }
                    if ($preview) { Capture $preview -Screen }
                    Start-Sleep -Milliseconds 300
                } finally { [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
                Start-Sleep -Milliseconds 350
            }
            function AssertProjectOrder([string]$first, [string]$second) {
                foreach ($prefix in @('New chat in ', 'Collapse ')) {
                    $upper = (ProjectBounds ($prefix + $first)).Top
                    $lower = (ProjectBounds ($prefix + $second)).Top
                    if ($upper -ge $lower) { throw "Incorrect project order in $prefix" }
                }
            }
            AssertProjectOrder 'Alpha project' 'Zulu project'
            $zulu = ProjectBounds 'Zulu project'
            $alpha = ProjectBounds 'Alpha project'
            DragProject $zulu $alpha $true 'native-project-drag-preview.png'
            AssertProjectOrder 'Zulu project' 'Alpha project'
            DragProject (ProjectBounds 'Collapse Zulu project') (ProjectBounds 'Collapse Alpha project') $false
            AssertProjectOrder 'Alpha project' 'Zulu project'
            Capture 'native-project-list.png'
            Invoke-RestMethod -Method Post ($Fixture + '/seed-project-order-chats') | Out-Null
            $projectToOpen = ProjectBounds 'Alpha project'
            [KronoWindowCapture]::SetCursorPos([int]($projectToOpen.Left + $projectToOpen.Width / 2), [int]($projectToOpen.Top + $projectToOpen.Height / 2)) | Out-Null
            [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
            [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
            Find 'Most recent project message' -ByName | Out-Null
            if ((ProjectBounds 'Alpha second').Top -ge (ProjectBounds 'Alpha first').Top) { throw 'Default project chat order was not recent activity' }
            SelectNav 'Settings'
            SelectSettingsCategory 'Appearance'
            SelectChoice 'Projectconversationorder' 'Manual'
            SelectNav 'Chats'
            if ((ProjectBounds 'Alpha second').Top -ge (ProjectBounds 'Alpha first').Top) { throw 'Switching to manual changed the current order' }
            DragProject (ProjectBounds 'Alpha first') (ProjectBounds 'Alpha second') $true 'native-project-chat-drag.png'
            if ((ProjectBounds 'Alpha first').Top -ge (ProjectBounds 'Alpha second').Top) { throw 'Chat drag did not change its order' }
            SelectNav 'Settings'
            SelectSettingsCategory 'Appearance'
            SelectChoice 'Projectconversationorder' 'Recent activity'
            SelectNav 'Chats'
            if ((ProjectBounds 'Alpha second').Top -ge (ProjectBounds 'Alpha first').Top) { throw 'Recent activity did not restore chronological order' }
            SelectNav 'Settings'
            SelectSettingsCategory 'Appearance'
            SelectChoice 'Projectconversationorder' 'Manual'
            SelectNav 'Chats'
            DragProject (ProjectBounds 'Outside project') (ProjectBounds 'Alpha first') $true
            DragProject (ProjectBounds 'Alpha second') (ProjectBounds 'Outside project') $false 'native-project-drag-boundary.png'
            if ((ProjectBounds 'Alpha first').Top -ge (ProjectBounds 'Alpha second').Top -or
                (ProjectBounds 'Alpha second').Top -ge (ProjectBounds 'Outside project').Top) { throw 'Drag did not stop at the project boundary' }
            Invoke 'New chat in Alpha project' -ByName
            Find 'New chat' -ByName | Out-Null
            if ((ProjectBounds 'New chat').Top -ge (ProjectBounds 'Alpha second').Top) { throw 'A new chat was not inserted at the top of its project' }
            if ((Find 'ChatProjectButton').Current.Name -ne 'Alpha project') { throw 'New chat did not open in the project' }
            $permissions = (Find 'Permissions').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
            if ($permissions.Count -ne 1 -or $permissions[0].Current.Name -ne 'project') { throw 'New project chat did not reuse permissions' }
            Invoke 'Chat actions' -ByName
            Invoke ('Move to project' + [char]0x2026) -ByName
            Find 'ChatProjectSelector' | Out-Null
            SelectChoice 'ChatProjectSelector' 'Zulu project'
            Invoke 'Move' -ByName
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $selectedProject = (Find 'ChatProjectButton').Current.Name
                if ($selectedProject -eq 'Zulu project') { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($selectedProject -ne 'Zulu project') {
                throw 'Manual project move did not update the active chat'
            }
            Invoke 'Projects' -ByName
            Invoke 'Edit project' -ByName
            Find 'Edit project' -ByName | Out-Null
            Find 'Source folders' -ByName | Out-Null
            Capture 'native-project-editor.png'
            Invoke 'Cancel' -ByName
            Invoke ('Delete project' + [char]0x2026) -ByName
            Find 'Delete Alpha project?' -ByName | Out-Null
            Invoke 'Delete project' -ByName
            Find 'New chat in Zulu project' -ByName | Out-Null
            $deleted = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'New chat in Alpha project')
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $staleRow = $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $deleted)
                if (!$staleRow) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($staleRow) { throw 'Deleted project remained on the overview' }
            Write-Output 'NATIVE_SMOKE_PASS: project and chat drag ordering, project boundaries, new chat permissions, manual move, and deletion'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'browser-feedback') {
            SelectNav 'Browser'
            SetValue 'BrowserAddress' ($Fixture + '/probe')
            PressEnterOn 'BrowserAddress'
            Find 'Native browser fixture' -ByName | Out-Null
            Invoke 'Add bookmark' -ByName
            Invoke 'Save bookmark' -ByName
            Invoke 'Edit bookmark' -ByName
            $savedLink = (Find 'BookmarkLink').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($savedLink -ne ($Fixture + '/probe')) { throw 'The bookmark editor did not prefill the current link' }
            SetValue 'BookmarkName' 'Renamed fixture bookmark'
            SetValue 'BookmarkLink' ($Fixture + '/probe?bookmark=1')
            Invoke 'Save bookmark' -ByName
            Find 'Renamed fixture bookmark' -ByName | Out-Null
            RightClick 'BrowserBookmark0'
            Invoke 'Delete bookmark' -ByName
            if (FindOptional 'BrowserBookmark0') { throw 'The bookmark remained in the bar after deletion' }
            $add = (Find 'WorkspaceAddTab').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
            1..5 | ForEach-Object { $add.Invoke() }
            $items = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $tabCount = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $items).Count
                if ($tabCount -eq 6) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($tabCount -ne 6) { throw 'Rapid add did not create all browser tabs' }
            $toolbar = (Find 'BrowserMoreOptions').Current.BoundingRectangle
            [KronoWindowCapture]::SetCursorPos([int]($toolbar.Left + $toolbar.Width / 2), [int]($toolbar.Top + $toolbar.Height / 2)) | Out-Null
            [KronoWindowCapture]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 350
            Capture 'browser-rapid-tabs.png' -Screen
            $close = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Close tab')
            $closeButtons = @($script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $close))
            if ($closeButtons.Count -lt 3) { throw 'The new browser tabs did not expose close controls' }
            foreach ($button in $closeButtons[1..2]) { $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $tabCount = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, $items).Count
                if ($tabCount -eq 4) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($tabCount -ne 4) { throw 'Rapid close did not remove both browser tabs' }
            Invoke 'BrowserMoreOptions'
            Invoke 'BrowserMenuClock'
            Find 'BrowserHistory' | Out-Null
            Capture 'browser-history.png'
            Write-Output 'NATIVE_SMOKE_PASS: bookmark create/edit/delete, rapid browser tab creation and closure, and history'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'desktop-notices') {
            Invoke-RestMethod ($Fixture + '/seed-unavailable-models') -Method Post | Out-Null
            SelectNav 'Settings'
            SelectSettingsCategory 'Models'
            SelectChoice 'Provider' 'Ollama'
            (Find 'ModelSelector').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            $empty = Find 'EmptyDropdownMessage'
            $emptyStates = @('Could not load models', 'No models available')
            $deadline = (Get-Date).AddSeconds(8)
            while ($empty.Current.Name -notin $emptyStates -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
            Capture 'native-model-discovery-failure.png' -Screen
            $toast = FindOptional 'ErrorNotice'
            if ($empty.Current.Name -notin $emptyStates -or $toast) {
                throw "Unexpected model failure display. Dropdown: $($empty.Current.Name). Toast shown: $([bool]$toast)"
            }
            SelectSettingsCategory 'Updates'
            Invoke 'Check for updates' -ByName
            $notice = $null
            $deadline = (Get-Date).AddSeconds(34)
            do {
                $notice = FindOptional 'SuccessNotice'
                if (!$notice) { $notice = FindOptional 'ErrorNotice' }
                if (!$notice) { Start-Sleep -Milliseconds 100 }
            } while (!$notice -and (Get-Date) -lt $deadline)
            if (!$notice -or (FindOptional 'UpdateCheckStatus')) { throw 'The update check did not report its result exclusively in the toast' }
            Write-Output 'NATIVE_SMOKE_PASS: model failures remain inline and update checks report through the toast'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'dropdown-empty-ui') {
            SelectNav 'Scheduled tasks'
            Invoke '+ New task' -ByName
            $provider = (Find 'ScheduleProvider').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
            if ($provider.Count -ne 1 -or $provider[0].Current.Name -ne 'OpenAI') { SelectChoice 'ScheduleProvider' 'OpenAI' }
            $picker = (Find 'ScheduleModel').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
            $trigger = (Find 'ScheduleModel').Current.BoundingRectangle
            $picker.Expand()
            $label = Find 'EmptyDropdownMessage'
            Start-Sleep -Milliseconds 200
            $empty = $label.Current.BoundingRectangle
            Capture 'native-empty-dropdown.png' -Screen
            if ($empty.IsEmpty -or $empty.Height -lt 15) { throw 'Empty dropdown did not leave room for its label' }
            $leftInset = $empty.Left - $trigger.Left
            $rightInset = $trigger.Right - $empty.Right
            if ($leftInset -lt 10 -or $leftInset -gt 32 -or [Math]::Abs($leftInset - $rightInset) -gt 2) {
                throw "Empty dropdown did not match its trigger. Label: $empty. Trigger: $trigger"
            }
            Write-Output 'NATIVE_SMOKE_PASS: empty dropdown label is readable and surface matches trigger width'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'games-lifecycle') {
            TestGameSaveLifecycle
            Write-Output 'NATIVE_SMOKE_PASS: viewport game previews and untouched, played, and restarted save lifecycles'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'tray-lifecycle') {
            TestAgentBrowser
            $address = (Find 'BrowserAddress').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($address -notlike '*/agent-browser-next') { throw 'Agent Browser did not display the followed page address' }
            $hwnd = $script:process.MainWindowHandle
            $windowPattern.Close()
            $deadline = (Get-Date).AddSeconds(5)
            while ([KronoWindowCapture]::IsWindowVisible($hwnd) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
            if ([KronoWindowCapture]::IsWindowVisible($hwnd) -or $script:process.HasExited) { throw 'The native window did not hide into the running tray instance' }
            ReopenTrayWorkspace $hwnd
            $restoredAddress = (Find 'BrowserAddress').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($address -ne $restoredAddress -or !(FindOptional 'Composer') -or !(FindOptional 'AgentBrowserTab')) {
                throw 'Reopening from tray lost the agent browser tab, address or conversation split'
            }
            SelectNav 'Browser'
            if (FindOptional 'AgentBrowserTab') { throw 'Agent tabs were visible in the main browser by default' }
            Invoke 'BrowserMoreOptions'
            $visibility = Find 'BrowserShowAgentTabs'
            $visibility.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
            Find 'AgentBrowserTab' | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: real agent browser navigation and shortcut restoration of the original tray window and browser split'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'runtime-workspaces') {
            TestWorkspaceInputs
            TestAgentBrowser
            Capture 'native-agent-browser-split.png'
            SetValue 'Composer' 'NATIVE_COMMAND_PROOF'
            Invoke 'Send' -ByName
            Invoke 'Allow once' -ByName
            Find 'Native command fixture response.' -ByName | Out-Null
            $agentTerminal = Find 'TerminalOutput'
            $agentOutput = $agentTerminal.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($agentTerminal.Current.Name -ne 'Agent terminal' -or $agentOutput -notmatch 'native-command-proof' -or $agentOutput -notmatch '\b42\b') { throw 'The agent terminal did not show the real command and its output' }
            Find 'Composer' | Out-Null
            SetValue 'Composer' 'NATIVE_TRAY_PROOF'
            Invoke 'Send' -ByName
            $deadline = (Get-Date).AddSeconds(10)
            do { $proof = Invoke-RestMethod ($Fixture + '/tray-proof'); Start-Sleep -Milliseconds 100 } while (!$proof.working -and (Get-Date) -lt $deadline)
            if (!$proof.working) { throw 'Agent run never reached the provider before hiding to the tray' }
            $hwnd = $script:process.MainWindowHandle
            $windowPattern.Close()
            $deadline = (Get-Date).AddSeconds(5)
            while ([KronoWindowCapture]::IsWindowVisible($hwnd) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
            if ([KronoWindowCapture]::IsWindowVisible($hwnd) -or $script:process.HasExited) { throw 'The native window did not hide into the running tray instance' }
            Invoke-RestMethod -Method Post ($Fixture + '/finish-tray-proof') | Out-Null
            $deadline = (Get-Date).AddSeconds(10)
            do { $proof = Invoke-RestMethod ($Fixture + '/tray-proof'); Start-Sleep -Milliseconds 100 } while (!$proof.completed -and (Get-Date) -lt $deadline)
            if (!$proof.completed -or $script:process.HasExited -or [KronoWindowCapture]::IsWindowVisible($hwnd)) { throw 'The agent did not save its finished response while the app remained in the tray' }
            ReopenTrayWorkspace $hwnd
            $restored = (Find 'TerminalOutput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($restored -ne $agentOutput -or !(FindOptional 'Composer')) { throw 'Reopening from tray lost the active conversation or its Terminal split' }
            Write-Output 'NATIVE_SMOKE_PASS: PTY, blank-tab reload, separate agent browser, tray completion and active split restoration'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'markdown-ui') {
            UseFixtureModel
            SelectNav 'Chats'
            Invoke 'Choose model' -ByName
            Find 'Advanced…' -ByName | Out-Null
            Find 'Composer' | Out-Null
            Invoke 'Choose model' -ByName
            SelectNav 'Settings'
            SelectSettingsCategory 'Instructions'
            Invoke 'Edit global instructions' -ByName
            $editor = (Find 'GlobalInstructions').Current.BoundingRectangle
            $save = (Find 'Save instructions' -ByName).Current.BoundingRectangle
            if ($editor.Width -lt 440 -or $editor.Height -lt 220 -or $save.Bottom -gt $script:window.Current.BoundingRectangle.Bottom) {
                throw 'Global editor did not use the available settings viewport'
            }
            Invoke 'Cancel' -ByName
            TestLiveMarkdown
            Write-Output 'NATIVE_SMOKE_PASS: inline reasoning panel and viewport-sized Markdown instructions editor'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'local-model-ui') {
            SelectNav 'Settings'
            SelectSettingsCategory 'Models'
            SelectChoice 'Provider' 'Local Model'
            if (FindOptional 'ProviderEndpoint') { throw 'Automatic local discovery showed a manual endpoint' }
            if (FindOptional 'ProviderKey') { throw 'Automatic local discovery showed a key field' }
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $selected = (Find 'ModelSelector').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
                if ($selected.Count -eq 1) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($selected.Count -ne 1) { throw 'No model was selected automatically' }
            $default = $selected[0].Current.Name
            $popup = (Find 'ModelSelector').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
            $popup.Expand()
            Find 'native-discovered-model:latest · Jan' -ByName | Out-Null
            $first = $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
                Where-Object { $_.Current.ClassName -like '*ComboBoxItem' -and !$_.Current.IsOffscreen } | Select-Object -First 1
            if (!$first -or $first.Current.Name -ne $default) { throw 'The first listed model was not selected automatically' }
            $popup.Collapse()
            SelectChoice 'ModelSelector' 'alpha-discovered-model:latest · Jan'
            SelectChoice 'ModelSelector' 'native-discovered-model:latest · Jan'
            SelectNav 'Chats'
            SetValue 'Composer' 'NATIVE_AUTOMATIC_LOCAL_MODEL'
            Invoke 'Send' -ByName
            Find 'Automatically discovered local model replied.' -ByName | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: first detected local model, manual selection, and routed completion'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -in @('plugins-ui', 'plugins-layout', 'plugins-install-ui')) {
            SelectNav 'Settings'
            SelectSettingsCategory 'Plugins'
            $left = (Find 'PluginPrimary-github').Current.BoundingRectangle
            $right = (Find 'PluginPrimary-vercel').Current.BoundingRectangle
            if ($left.IsEmpty -or $right.IsEmpty -or $right.Left -le $left.Right) { throw 'The plugin catalog did not show two columns' }
            if ($env:KRONO_SMOKE_SCOPE -eq 'plugins-layout') {
                $bounds = @('github','vercel','firebase','notion') | ForEach-Object { (Find ('PluginPrimary-' + $_)).Current.BoundingRectangle.Left }
                if (@($bounds | Sort-Object -Unique).Count -lt 3) { throw 'The catalog did not fit at least three columns in this window' }
                Capture 'plugins-wide.png'
                Write-Output 'NATIVE_SMOKE_PASS: responsive plugin columns'
                return
            }
            $package = Join-Path $ReportDirectory 'native-installed-plugins/33333333-3333-4333-8333-333333333333/github.json'
            if (Test-Path -LiteralPath $package) { throw 'GitHub was installed before Install was clicked' }
            Invoke 'PluginPrimary-github'
            Find 'GitHub installed.' -ByName | Out-Null
            Find 'PluginRemove-github' | Out-Null
            if (!(Test-Path -LiteralPath $package)) { throw 'Installing GitHub did not write its plugin package' }
            $packaged = [IO.File]::ReadAllText($package)
            CloseApp
            [IO.File]::WriteAllText($package, '{"id":"github","previousVersion":true}')
            Launch
            Find 'Sign out' -ByName | Out-Null
            SelectNav 'Settings'
            SelectSettingsCategory 'Plugins'
            Find 'PluginRemove-github' | Out-Null
            if ([IO.File]::ReadAllText($package) -ne $packaged) { throw 'An updated plugin package was not restored at startup' }
            (Find 'PluginRemove-github').SetFocus()
            $removeBounds = WaitForBounds 'PluginRemove-github'
            $badgeBounds = WaitForBounds 'PluginStatus-github'
            $connectBounds = WaitForBounds 'PluginPrimary-github'
            if ($badgeBounds.Top -ge $removeBounds.Bottom -or $badgeBounds.Bottom -le $removeBounds.Top) {
                throw 'The plugin status and remove action are on different rows'
            }
            if ($connectBounds.Left -lt $badgeBounds.Right -or $connectBounds.Top -ge $removeBounds.Bottom -or $connectBounds.Bottom -le $removeBounds.Top) {
                throw 'Connect was not on the same row to the right of the other plugin actions'
            }
            if ($env:KRONO_SMOKE_SCOPE -eq 'plugins-install-ui') {
                Invoke 'PluginPrimary-github'
                Find 'PluginCopyCode-github' | Out-Null
                Start-Sleep -Milliseconds 230
                $copyBounds = WaitForBounds 'PluginCopyCode-github'
                $cancelBounds = WaitForBounds 'PluginPrimary-github'
                $badgeBounds = WaitForBounds 'PluginStatus-github'
                Capture 'native-plugin-device-code.png'
                if ($copyBounds.Left -lt $badgeBounds.Right -or $cancelBounds.Left -lt $copyBounds.Right -or
                    $copyBounds.Top -ge $cancelBounds.Bottom -or $copyBounds.Bottom -le $cancelBounds.Top) {
                    throw "Device action row geometry mismatch. Status: $badgeBounds. Copy: $copyBounds. Cancel: $cancelBounds"
                }
                if (FindOptional 'PluginProgress-github') { throw 'Device-code approval rendered a second row' }
                Invoke 'PluginPrimary-github'
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 100
                    $label = (Find 'PluginPrimary-github').Current.Name
                } while ($label -ne 'Connect' -and (Get-Date) -lt $deadline)
                $copy = FindOptional 'PluginCopyCode-github'
                if ($label -ne 'Connect' -or ($copy -and !$copy.Current.IsOffscreen -and !$copy.Current.BoundingRectangle.IsEmpty)) {
                    throw "Cancelling approval did not restore the card. Primary action: $label"
                }
            }
            SelectChoice 'PluginFilter' 'Installed'
            if (FindOptional 'PluginPrimary-vercel') { throw 'Installed filter shows an uninstalled plugin' }
            SelectChoice 'PluginFilter' 'All'
            if ((Find 'PluginPrimary-github').Current.Name -ne 'Connect') { throw 'GitHub did not offer browser connection after installation' }
            Invoke 'PluginRemove-github'
            Find 'Remove GitHub?' -ByName | Out-Null
            Invoke 'Cancel' -ByName
            if ((Find 'PluginPrimary-github').Current.Name -ne 'Connect') { throw 'Cancelling removal changed the installed plugin' }
            $confirm = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Remove plugin')
            $deadline = (Get-Date).AddSeconds(5)
            do {
                Invoke 'PluginRemove-github'
                Start-Sleep -Milliseconds 200
                $prompt = $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $confirm)
            } while (!$prompt -and (Get-Date) -lt $deadline)
            Invoke 'Remove plugin' -ByName
            Find 'GitHub removed.' -ByName | Out-Null
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $action = (Find 'PluginPrimary-github').Current.Name
                if ($action -eq 'Install') { break }
                Start-Sleep -Milliseconds 75
            } while ((Get-Date) -lt $deadline)
            if ($action -ne 'Install') { throw 'Removing GitHub did not restore Install' }
            if (FindOptional 'PluginToken-github') { throw 'GitHub exposed a manual credential field' }
            if (Test-Path -LiteralPath $package) { throw 'Removing GitHub left its plugin package behind' }
            if ($env:KRONO_SMOKE_SCOPE -eq 'plugins-install-ui') {
                SelectChoice 'PluginFilter' 'Not installed'
                Find 'PluginPrimary-github' | Out-Null
                Write-Output 'NATIVE_SMOKE_PASS: real plugin package installation, filtering, and removal'
                return
            }
            Invoke 'PluginPrimary-node'
            Find 'PluginOptions-node' | Out-Null
            if ((Find 'PluginStatus-node').Current.Name -ne 'Installed') { throw 'The local runtime required a cloud connection' }
            Invoke 'PluginPrimary-typescript'
            Find 'PluginRemove-typescript' | Out-Null
            [IO.File]::WriteAllText((Join-Path $ReportDirectory 'package.json'), '{"name":"native-plugin-fixture","scripts":{"test":"node --test"}}')
            UseFixtureModel
            SelectNav 'Chats'
            Invoke 'Projects' -ByName
            Invoke 'New project' -ByName
            SetValue 'ProjectName' 'Plugin fixture project'
            Invoke 'Add folder…' -ByName
            SetValue 'ProjectFolderPath' $ReportDirectory
            Invoke 'Add folder' -ByName
            Find 'Remove folder' -ByName | Out-Null
            Invoke 'Save project' -ByName
            Find 'Edit project' -ByName | Out-Null
            Invoke 'Back to chat' -ByName
            SelectNav 'Settings'
            SelectSettingsCategory 'Instructions'
            Invoke 'Edit global instructions' -ByName
            SetValue 'GlobalInstructions' 'Global plugin fixture instructions'
            Invoke 'Save instructions' -ByName
            Invoke 'Plugin fixture project' -ByName
            SetValue 'ProjectInstructions' 'Project plugin fixture instructions'
            Invoke 'Save instructions' -ByName
            SelectNav 'Chats'
            Invoke 'ChatProjectButton'
            SelectChoice 'ChatProjectSelector' 'Plugin fixture project'
            Invoke 'Choose' -ByName
            SelectChoice 'Mode' 'agent'
            SelectChoice 'Permissions' 'project'
            SetValue 'Composer' 'NATIVE_PLUGIN_PROOF'
            Invoke 'Send' -ByName
            Find 'Native Node plugin verified.' -ByName | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: plugin catalog, install/connect/remove, skill guidance and Node package tool through the agent'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'dropdown-ui') {
            foreach ($theme in @('light', 'dark', 'halloween')) {
                SelectNav 'Settings'
                SelectSettingsCategory 'Appearance'
                SelectChoice 'Appearance' $theme
                SelectNav 'Chats'
                $picker = (Find 'Mode').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
                $picker.Expand()
                $item = Find 'plan' -ByName
                Start-Sleep -Milliseconds 230
                $row = $item.Current.BoundingRectangle
                $trigger = (Find 'Mode').Current.BoundingRectangle
                Capture ('native-input-dropdown-' + $theme + '.png') -Screen
                $leftInset = $row.Left - $trigger.Left
                $rightInset = $trigger.Right - $row.Right
                if ($row.IsEmpty -or $leftInset -lt 0 -or $rightInset -lt 0 -or $leftInset -gt 12 -or
                    [Math]::Abs($leftInset - $rightInset) -gt 2) { throw "Dropdown geometry mismatch ($theme). Row: $row. Trigger: $trigger" }
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
            Find 'Advanced…' -ByName | Out-Null
            Capture 'compact-model.png'
            Invoke 'Choose model' -ByName
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
        if ($env:KRONO_SMOKE_SCOPE -in @('settings-ui','appearance-layout')) {
            SelectNav 'Settings'
            SelectSettingsCategory 'Appearance'
            Capture 'native-appearance-typography.png'
            $familyHeight = (Find 'TextFontFamily').Current.BoundingRectangle.Height
            $sizeHeight = (Find 'TextFontSize').Current.BoundingRectangle.Height
            if ([Math]::Abs($familyHeight - $sizeHeight) -gt 3) { throw 'Font family and size fields did not match in height' }
            foreach ($prefix in @('Text', 'Code')) {
                $family = (Find ($prefix + 'FontFamily')).Current.BoundingRectangle
                $size = (Find ($prefix + 'FontSize')).Current.BoundingRectangle
                if ($family.IsEmpty -or $size.IsEmpty -or [Math]::Abs($family.Top - $size.Top) -gt 2 -or $size.Left -lt $family.Right) {
                    throw "$prefix font and size did not share a row"
                }
            }
            if ($env:KRONO_SMOKE_SCOPE -eq 'appearance-layout') {
                Write-Output 'NATIVE_SMOKE_PASS: font family and size fields match in height'
                return
            }
            SelectChoice 'CodeFontFamily' 'Consolas'
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $font = (Invoke-RestMethod ($Fixture + '/font-proof')).codeFamily
                if ($font -eq 'Consolas') { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($font -ne 'Consolas') { throw 'Choosing a font did not autosave the typography settings' }
            UseFixtureModel
            SelectNav 'Scheduled tasks'
            Invoke '+ New task' -ByName
            SetValue 'ScheduleName' 'Schedule from Enter'
            SetValue 'SchedulePrompt' 'Keyboard submission fixture'
            (Find 'ScheduleModelOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            SelectChoice 'ScheduleProvider' 'OpenAI'
            SelectChoice 'ScheduleModel' 'gpt-native-fixture-model'
            SetValue 'ScheduleFirstRun' ((Get-Date).AddDays(1).ToString('yyyy-MM-dd, HH:mm', [Globalization.CultureInfo]::InvariantCulture))
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
            Invoke-RestMethod ($Fixture + '/seed-provider-settings') -Method Post | Out-Null
            SelectNav 'Settings'
            SelectSettingsCategory 'Models'
            SelectChoice 'Provider' 'Ollama'
            $modelBounds = (Find 'ModelSelector').Current.BoundingRectangle
            if ([Math]::Abs($modelBounds.Left - (Find 'Provider').Current.BoundingRectangle.Left) -gt 2) { throw 'The model selector did not align with the provider field' }
            foreach ($id in @('ProviderConnectionOptions', 'ProviderEndpoint', 'ProviderTools')) {
                if (FindOptional $id) { throw ('Removed model setting is still visible: ' + $id) }
            }
            if (FindOptional 'ProviderKey') { throw 'A loopback Ollama server showed a required key field' }
            Invoke 'Configure optional server token' -ByName
            SetValue 'ProviderKey' 'fixture-placeholder'
            PressEnterOn 'ProviderKey'
            $savedNotice = Find 'Ollama API key saved.' -ByName
            Invoke 'Save key' -ByName
            $errorNotice = Find 'Enter an API key before saving.' -ByName
            if ($savedNotice.Current.AutomationId -ne 'SuccessNotice' -or $errorNotice.Current.AutomationId -ne 'ErrorNotice' -or
                $errorNotice.Current.BoundingRectangle.Top -le $savedNotice.Current.BoundingRectangle.Top) {
                throw 'Success and error notifications did not stack'
            }
            Capture 'native-stacked-notices.png'
            $deadline = (Get-Date).AddSeconds(5)
            do {
                $saved = Test-Path -LiteralPath (Join-Path $ReportDirectory 'native-credentials/33333333-3333-4333-8333-333333333333/ollama.bin')
                if ($saved) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if (!$saved) { throw 'Enter did not save the fixture credential' }
            SelectChoice 'ModelSelector' 'native-keyboard-model'
            Capture 'native-provider-models.png'
            SelectChoice 'Provider' 'OpenAI'
            SelectChoice 'Provider' 'Ollama'
            $selected = (Find 'ModelSelector').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
            if ($selected.Count -ne 1 -or $selected[0].Current.Name -ne 'native-keyboard-model') { throw 'Manual model selection did not persist' }
            SelectSettingsCategory 'Instructions'
            TestLiveMarkdown
            SelectNav 'Chats'
            Find 'native-keyboard-model' -ByName | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: Typography, manual model preference, keyboard credential submission, and Markdown editor'
            return
        }
        if ($env:KRONO_SMOKE_SCOPE -eq 'menus-ui') {
            Invoke 'Choose model' -ByName
            Find 'Advanced…' -ByName | Out-Null
            Capture 'native-model-dialog.png'
            if (!(New-Object -ComObject WScript.Shell).AppActivate($script:process.Id)) { throw "Could not activate the popup for pointer input" }
            $bounds = $script:window.Current.BoundingRectangle
            [KronoWindowCapture]::SetCursorPos([int]($bounds.Left + 35), [int]($bounds.Top + 100)) | Out-Null
            [KronoWindowCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
            [KronoWindowCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 350
            $dialogTitle = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'Advanced…')
            if ($script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $dialogTitle)) { throw 'Clicking outside did not close the inline reasoning panel' }
            Find 'Composer' | Out-Null
            Write-Output 'NATIVE_SMOKE_PASS: clicking outside the inline reasoning panel returns to the composer'
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
    Find 'Remove folder' -ByName | Out-Null
    Invoke 'Save project' -ByName
    Find 'Edit project' -ByName | Out-Null
    $savedFolder = (Invoke-RestMethod ($Fixture + '/project-folder-proof')).path
    if (!$savedFolder -or (Split-Path $savedFolder -Leaf) -ne (Split-Path $ReportDirectory -Leaf)) {
        throw "Project folder was not saved: $savedFolder"
    }
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
    UseFixtureModel
    SelectNav 'Chats'
    OpenAdvancedModel
    if ($env:KRONO_SMOKE_SCOPE -eq 'account-ui') { Capture 'native-model-dialog.png' }
    SelectChoice 'ChatModelPicker' 'gpt-native-fixture-model'
    SelectChoice 'Conversationcontext' 'Recent messages'
    Invoke 'Apply' -ByName
    Find 'gpt-native-fixture-model' -ByName | Out-Null
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
    Invoke 'ChatProjectButton'
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
    Invoke 'ChatProjectButton'
    Capture 'native-move-current-project.png'
    SelectChoice 'ChatProjectSelector' 'Native fixture project'
    Invoke 'Move' -ByName
    WaitForChatProject 'Native fixture project'
    if ($env:KRONO_SMOKE_SCOPE -eq 'account-ui') {
        OpenAdvancedModel
        SelectChoice 'ChatModelPicker' 'gpt-5-mini'
        Invoke 'Apply' -ByName
        Find 'gpt-5-mini' -ByName | Out-Null
        Invoke 'Choose model' -ByName
        $effort = (Find 'Reasoning effort' -ByName).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)
        $effort.SetValue($effort.Current.Maximum)
        Invoke 'Choose model' -ByName
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
    $work = (Find 'Work details' -ByName).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($work.Current.ExpandCollapseState -ne [Windows.Automation.ExpandCollapseState]::Collapsed) { throw 'Agent work did not collapse on completion' }
    $work.Expand()
    if ($work.Current.ExpandCollapseState -ne [Windows.Automation.ExpandCollapseState]::Expanded) { throw 'Completed work could not be reopened' }
    $work.Collapse()
    Invoke-RestMethod ($Fixture + '/seed-provider-settings') -Method Post | Out-Null
    SelectNav 'Settings'
    SelectSettingsCategory 'Models'
    SelectChoice 'Provider' 'Ollama'
    Invoke 'Configure optional server token' -ByName
    SetValue 'ProviderKey' 'fixture-local-placeholder'
    Invoke 'Save key' -ByName
    Start-Sleep -Milliseconds 500
    SelectChoice 'ModelSelector' 'local-fixture-model'
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
    SelectChoice 'ModelSelector' 'basic-fixture-model'
    SelectNav 'Chats'
    SetValue 'Composer' 'Native manual model B without tool support'
    Invoke 'Send' -ByName
    $deadline = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 100; $assertions = Invoke-RestMethod ($Fixture + '/assertions') } while ((!$assertions.localModelBNoTools -or !(Find 'Send' -ByName).Current.IsEnabled) -and (Get-Date) -lt $deadline)
    if (!$assertions.localModelBNoTools) { throw 'Model B received tools despite its disabled capability' }
    SelectNav 'Settings'
    SelectSettingsCategory 'Models'
    SelectChoice 'ModelSelector' 'local-fixture-model'
    SelectChoice 'Provider' 'OpenAI'
    $selected = (Find 'ModelSelector').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selected.Count -ne 1 -or $selected[0].Current.Name -ne 'gpt-native-fixture-model') { throw 'Provider switching lost its model selection' }
    foreach ($cloudProfile in @(@{Id='azure';Name='Azure OpenAI'},@{Id='xai';Name='xAI'},@{Id='groq';Name='Groq'},@{Id='mistral';Name='Mistral'},@{Id='openrouter';Name='OpenRouter'})) {
        SelectNav 'Settings'
        SelectSettingsCategory 'Models'
        SelectChoice 'Provider' $cloudProfile.Name
        SetValue 'ProviderKey' ('fixture-' + $cloudProfile.Id + '-key')
        Invoke 'Save key' -ByName
        Start-Sleep -Milliseconds 300
        SelectChoice 'ModelSelector' ($cloudProfile.Id + '-fixture')
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
    if ((Find 'GameStatus').Current.Name -like 'Paused*') {
        if (!(New-Object -ComObject WScript.Shell).AppActivate($script:process.Id)) { throw 'Could not reactivate the game window' }
        Invoke 'Pause / resume' -ByName
    }
    $ticCell = WaitTicTurn
    Invoke ('GameCell' + $ticCell)
    Find ('Cell ' + ($ticCell + 1) + ': X') -ByName | Out-Null
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
    TestGamePreviews
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
            RightClick 'GameCell63'
            $deadline = (Get-Date).AddSeconds(2)
            while ((Find 'GameCell63').Current.Name -notmatch 'flagged$' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
            if ((Find 'GameCell63').Current.Name -notmatch 'flagged$') { throw 'Minesweeper right click did not mark the tile' }
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
        if ($game -eq '2048') {
            foreach ($direction in @('Move left', 'Move right', 'Move up', 'Move down')) {
                Invoke $direction -ByName
                if ((Invoke-RestMethod ($Fixture + '/game-proof')).saves.game -contains '2048') { break }
            }
        }
        if ($game -eq 'Breakout') { Invoke 'Move left' -ByName; Capture 'native-breakout.png' }
        if ($game -eq 'Pong') { Invoke 'Move up' -ByName }
        if ($game -eq 'Snake') { Invoke 'Move right' -ByName }
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
            if ((Find 'GameStatus').Current.Name -notmatch '^Shade 2 · Score 100') { throw 'Odd Color did not advance or score the correct choice' }
            Capture 'native-odd-color.png'
            Invoke 'GameLibraryBack'
            Invoke-RestMethod -Method Post ($Fixture + '/odd-color-round-20') | Out-Null
            OpenGameTile 10 -RequireChoice
            Invoke 'Pause / resume' -ByName
            $color = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Odd Color').state
            if ($color.OddRound -ne 20) { throw 'Reopening Odd Color replaced its saved fixture progress' }
            Invoke ('GameCell' + $color.OddCell)
            if ((Find 'GameStatus').Current.Name -notmatch '^Shade 21 · Score 2000') { throw 'Odd Color stopped at its previous twenty-round cap' }
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
            if ((Find 'GameCell0').Current.ControlType -eq [Windows.Automation.ControlType]::Button) { throw 'Reversi offered an illegal move' }
            Invoke 'GameCell19'
            if ((Find 'GameCell19').Current.Name -notmatch 'coral$') { throw 'Reversi did not apply the opening move' }
            Capture 'native-reversi.png'
        }
        if ($game -eq 'Sudoku') {
            $sudoku = @(0..80 | ForEach-Object {
                $name = (Find ('GameCell' + $_)).Current.Name
                if ($name -match ': ([1-9]) fixed$') { [int]$Matches[1] } else { 0 }
            })
            $square = 0..80 | Where-Object { $sudoku[$_] -eq 0 -and @($sudoku[([int][Math]::Floor($_ / 9) * 9)..([int][Math]::Floor($_ / 9) * 9 + 8)] | Where-Object { $_ -gt 0 }).Count -gt 0 } | Select-Object -First 1
            $row = [int][Math]::Floor($square / 9)
            $conflict = $sudoku[($row * 9)..($row * 9 + 8)] | Where-Object { $_ -gt 0 } | Select-Object -First 1
            Invoke ('GameCell' + $square)
            Invoke ('SudokuDigit' + $conflict)
            if ((Find 'GameStatus').Current.Name -notmatch 'conflicts') { throw 'Sudoku accepted a conflicting row digit' }
            $column = $square % 9
            $used = @(0..80 | Where-Object { $_ % 9 -eq $column -or [Math]::Floor($_ / 9) -eq $row -or ([Math]::Floor(($_ % 9) / 3) -eq [Math]::Floor($column / 3) -and [Math]::Floor($_ / 27) -eq [Math]::Floor($square / 27)) } | ForEach-Object { $sudoku[$_] })
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
            $emptyTiles = @(0..15 | Where-Object { (Find ('GameCell' + $_)).Current.Name -eq 'Empty square' })
            if ($emptyTiles.Count -ne 1) { throw 'Jigsaw did not expose a single empty square' }
            $blank = $emptyTiles[0]
            $adjacent = @(0..15 | Where-Object { [Math]::Abs($_ % 4 - $blank % 4) + [Math]::Abs([Math]::Floor($_ / 4) - [Math]::Floor($blank / 4)) -eq 1 })
            $distant = 0..15 | Where-Object { $_ -ne $blank -and $_ -notin $adjacent } | Select-Object -First 1
            DragGameCell $distant $blank
            if ((Find ('GameCell' + $blank)).Current.Name -ne 'Empty square') { throw 'Jigsaw accepted a nonadjacent drag' }
            DragGameCell $adjacent[0] $blank
            $deadline = (Get-Date).AddSeconds(3)
            do {
                $puzzle = ((Invoke-RestMethod ($Fixture + '/game-proof')).saves | Where-Object game -eq 'Jigsaw').state
                if ($puzzle.Moves -eq 1) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if ($puzzle.Moves -ne 1 -or (Find ('GameCell' + $adjacent[0])).Current.Name -ne 'Empty square') { throw 'Jigsaw did not slide the adjacent piece' }
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
    SelectChoice 'TextFontFamily' 'Space Grotesk'
    SelectChoice 'CodeFontFamily' 'Consolas'
    SelectNav 'Chats'
    SetValue 'Composer' 'Typography sample'
    $font = (Find 'Composer').GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::FontNameAttribute)
    if ($font -notmatch 'Space Grotesk') { throw ('Cached chat did not update its text font: ' + $font) }
    SetValue 'Composer' ''
    SelectNav 'Terminal'
    $font = (Find 'TerminalOutput').GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::FontNameAttribute)
    if ($font -notmatch 'Consolas') { throw ('Cached terminal did not update its independent code font: ' + $font) }
    Capture 'native-typography-roles.png'
    SelectNav 'Settings'
    SelectSettingsCategory 'Appearance'
    SelectChoice 'TextFontFamily' 'Rubik'
    SelectChoice 'CodeFontFamily' 'JetBrains Mono'
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
    SelectChoice 'CodeFontFamily' 'Consolas'
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
    $pausedCell = 1..8 | Where-Object { (Find ('GameCell' + $_)).Current.Name -match 'empty$' } | Select-Object -First 1
    $pausedSquare = Find ('GameCell' + $pausedCell)
    if ($pausedSquare.Current.IsEnabled -and $pausedSquare.Current.ControlType -eq [Windows.Automation.ControlType]::Button) { Invoke ('GameCell' + $pausedCell) }
    if ((Find ('GameCell' + $pausedCell)).Current.Name -notmatch 'empty$') { throw 'Paused game accepted a move' }
    Invoke 'Pause / resume' -ByName
    $empty = WaitTicTurn
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
    for ($i = 0; $i -lt 16; $i++) {
        $expected = if ($pieces[$i] -eq 15) { 'Empty square' } else { 'Tile ' + ($i + 1) + ' · Piece ' + ($pieces[$i] + 1) }
        if ((Find ('GameCell' + $i)).Current.Name -ne $expected) { throw 'Jigsaw board was lost across restart' }
    }
    Invoke 'GameLibraryBack'
    Invoke-RestMethod -Method Post ($Fixture + '/jigsaw-endgame') | Out-Null
    OpenGameTile 19 -RequireChoice
    Invoke 'Pause / resume' -ByName
    DragGameCell 15 14
    $deadline = (Get-Date).AddSeconds(3)
    while ((Find 'GameStatus').Current.Name -notmatch '^Picture complete' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
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
    if ((Invoke-RestMethod ($Fixture + '/game-proof')).saves.game -notcontains 'Snake') { throw 'Snake had no save before Restart' }
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
    if ($beforeSnake -or $afterSnake) { throw 'Restarting then leaving an untouched game left a save' }
    Invoke-RestMethod -Method Post ($Fixture + '/corrupt-game') | Out-Null
    Invoke-RestMethod -Method Post ($Fixture + '/solitaire-endgame') | Out-Null
    CloseApp
    Launch
    Find 'Sign out' -ByName | Out-Null
    SelectNav 'Games'
    OpenGameTile 0
    Invoke 'Move right' -ByName
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
    SelectChoice 'ScheduleModel' 'gpt-native-fixture-model'
    SetValue 'ScheduleFirstRun' ((Get-Date).AddDays(-1).ToString('yyyy-MM-dd, HH:mm', [Globalization.CultureInfo]::InvariantCulture))
    PressEnterOn 'ScheduleFirstRun'
    if (@((Invoke-RestMethod ($Fixture + '/schedule-proof')).runs).Count -ne 0) { throw 'Scheduled task ran before opting in' }
    Toggle 'ScheduleEnabled'
    Find 'Scheduled result ready' -ByName | Out-Null
    $scheduled = Invoke-RestMethod ($Fixture + '/schedule-proof')
    if ($scheduled.runs.Count -ne 1 -or $scheduled.runs[0].state -ne 'completed' -or $scheduled.response -ne 'Native scheduled result.') { throw 'Due scheduled task did not complete exactly once' }
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
