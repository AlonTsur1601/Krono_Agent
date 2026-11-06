using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    int[] sudoku = new int[81]; bool[] clues = new bool[81]; int selectedSquare = -1;
    string gameMessage = "";
    readonly StackPanel advancedControls = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
    readonly StackPanel sudokuControls = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly Button stackTap = new() { Content = "Place block" };
    readonly NumberBox red = new() { Header = "R", Value = 128, Minimum = 0, Maximum = 255, Width = 80 };
    readonly NumberBox green = new() { Header = "G", Value = 128, Minimum = 0, Maximum = 255, Width = 80 };
    readonly NumberBox blue = new() { Header = "B", Value = 128, Minimum = 0, Maximum = 255, Width = 80 };
    readonly StackPanel colorControls = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly Button rememberColor = new() { Content = "Use color", VerticalAlignment = VerticalAlignment.Bottom };
    double stackX, stackWidth; int stackDirection, stackCombo, stackLevel;
    readonly List<double[]> tower = [];
    uint[] targetColors = []; readonly List<uint> chosenColors = []; int colorStart;
    bool updatingColor;
    uint CurrentColor => 0xff000000u | (uint)Component(red) << 16 | (uint)Component(green) << 8 | (uint)Component(blue);
    static int Component(NumberBox control) => double.IsFinite(control.Value) ? (int)Math.Clamp(control.Value, 0, 255) : 128;
    void BuildAdvancedControls(StackPanel footer)
    {
        for (var digit = 1; digit <= 9; digit++) { var value = digit; var button = new Button { Content = digit.ToString(), Padding = new Thickness(10, 6, 10, 6), MinWidth = 30 }; AutomationProperties.SetAutomationId(button, "SudokuDigit" + digit); button.Click += (_, _) => EnterSudoku(value); sudokuControls.Children.Add(button); }
        var erase = new Button { Content = "Erase", Padding = new Thickness(8) }; erase.Click += (_, _) => EnterSudoku(0); sudokuControls.Children.Add(erase);
        stackTap.Click += (_, _) => PlaceStack();
        foreach (var (box, id) in new[] { (red, "ColorRed"), (green, "ColorGreen"), (blue, "ColorBlue") }) {
            AutomationProperties.SetAutomationId(box, id); box.ValueChanged += (_, _) => { if (!updatingColor && game == 15) { Draw(); SaveCurrent(); } }; colorControls.Children.Add(box);
        }
        rememberColor.Click += (_, _) => AcceptColor(); colorControls.Children.Add(rememberColor);
        advancedControls.Children.Add(sudokuControls); advancedControls.Children.Add(stackTap); advancedControls.Children.Add(colorControls); footer.Children.Add(advancedControls);
    }
    void ResetAdvanced()
    {
        gameMessage = ""; selectedSquare = -1;
        advancedControls.Visibility = game >= 12 ? Visibility.Visible : Visibility.Collapsed;
        sudokuControls.Visibility = game == 13 ? Visibility.Visible : Visibility.Collapsed;
        stackTap.Visibility = game == 14 ? Visibility.Visible : Visibility.Collapsed;
        colorControls.Visibility = game == 15 ? Visibility.Visible : Visibility.Collapsed;
        if (game == 12) { board[27] = board[36] = 2; board[28] = board[35] = 1; }
        if (game == 13) { sudoku = NativeGameRules.NewSudoku(random); clues = sudoku.Select(value => value > 0).ToArray(); }
        if (game == 14) { stackX = 0; stackWidth = 240; stackDirection = 1; stackCombo = stackLevel = 0; tower.Clear(); tower.Add([120, 240]); }
        if (game == 15) {
            targetColors = Enumerable.Range(0, 5).Select(_ => 0xff000000u | (uint)random.Next(32, 225) << 16 | (uint)random.Next(32, 225) << 8 | (uint)random.Next(32, 225)).ToArray();
            chosenColors.Clear(); colorStart = 167; SetColor(0xff808080);
        }
    }
    void SetColor(uint value) { updatingColor = true; red.Value = value >> 16 & 255; green.Value = value >> 8 & 255; blue.Value = value & 255; updatingColor = false; }
    void AdvancedClick(int index)
    {
        if (paused || ended) return;
        if (game == 13) { selectedSquare = index; gameMessage = clues[index] ? "This clue is fixed" : "Choose a digit, or press 1–9"; }
        if (game == 12 && NativeGameRules.ReversiPlace(board, index, 1)) {
            do {
                var choice = NativeGameRules.ReversiBot(board);
                if (choice < 0) break;
                NativeGameRules.ReversiPlace(board, choice, 2);
            } while (NativeGameRules.ReversiMoves(board, 1).Length == 0);
            var yours = board.Count(value => value == 1); var theirs = board.Count(value => value == 2); score = yours;
            if (NativeGameRules.ReversiMoves(board, 1).Length == 0 && NativeGameRules.ReversiMoves(board, 2).Length == 0) End(yours == theirs ? "Draw" : yours > theirs ? "You won" : "Bot won");
            else gameMessage = NativeGameRules.ReversiMoves(board, 2).Length == 0 ? "Bot passes · your turn" : "Your turn";
        }
        Draw(); SaveCurrent();
    }
    void EnterSudoku(int digit)
    {
        if (game != 13 || paused || ended || selectedSquare < 0 || clues[selectedSquare]) return;
        if (digit > 0 && !NativeGameRules.SudokuLegal(sudoku, selectedSquare, digit)) { gameMessage = "That digit conflicts with its row, column or box"; Draw(); return; }
        sudoku[selectedSquare] = digit; moves++; score = Enumerable.Range(0, 81).Count(i => !clues[i] && sudoku[i] > 0) * 10; gameMessage = "";
        if (sudoku.All(value => value > 0)) End("Sudoku complete");
        Draw(); SaveCurrent();
    }
    void UpdateAdvanced()
    {
        if (game == 14) { stackX += stackDirection * Math.Min(12, 5 + stackLevel * .28); if (stackX < 0 || stackX + stackWidth > 480) { stackX = Math.Clamp(stackX, 0, 480 - stackWidth); stackDirection *= -1; } Draw(); }
        if (game == 15 && tick <= colorStart) Draw();
    }
    void PlaceStack()
    {
        if (game != 14 || paused || ended) return;
        var last = tower[^1]; var placed = NativeGameRules.StackPlace(stackX, stackWidth, last[0], last[1], stackCombo);
        if (placed.Width <= 0) End("No overlap");
        else {
            stackWidth = placed.Width; stackCombo = placed.Combo; stackLevel++; score += placed.Perfect ? 15 : 10;
            tower.Add([placed.X, placed.Width]); if (tower.Count > 10) tower.RemoveAt(0);
            gameMessage = placed.Perfect ? stackCombo % 3 == 0 ? "Perfect ×3 · width restored" : $"Perfect ×{stackCombo}" : "Keep stacking";
            stackDirection = stackLevel % 2 == 0 ? 1 : -1; stackX = stackDirection > 0 ? 0 : 480 - stackWidth;
        }
        Draw(); SaveCurrent();
    }
    void AcceptColor()
    {
        if (game != 15 || paused || ended || tick < colorStart || chosenColors.Count >= 5) return;
        var color = CurrentColor; var distance = NativeGameRules.DeltaE(targetColors[chosenColors.Count], color);
        score += (int)Math.Round(100 * Math.Max(0, 1 - distance / 100)); chosenColors.Add(color);
        if (chosenColors.Count == 5) End("Gauntlet complete");
        else SetColor(0xff808080);
        Draw(); SaveCurrent();
    }
    void DrawAdvanced()
    {
        if (game is 12 or 13) {
            var columns = game == 12 ? 8 : 9; var width = 480.0 / columns;
            var legal = game == 12 ? NativeGameRules.ReversiMoves(board, 1) : [];
            var detail = gameMessage.Length == 0 ? "" : " · " + gameMessage;
            if (!paused && !ended) status.Text = game == 12 ? $"Coral {board.Count(n => n == 1)} · Ivory {board.Count(n => n == 2)}{detail}" : $"Score {score}{detail}";
            for (var i = 0; i < columns * columns; i++) {
                var index = i; var value = game == 12 ? board[i] : sudoku[i]; var enabled = game == 12 ? legal.Contains(i) : !clues[i];
                var box = i / 27 * 3 + i % 9 / 3;
                var near = selectedSquare >= 0 && (i % 9 == selectedSquare % 9 || i / 9 == selectedSquare / 9);
                var cell = new Button { Style = (Style)Application.Current.Resources["KronoFlatButton"], Width = width - 4, Height = width - 4, Padding = new Thickness(0), BorderThickness = new Thickness(game == 13 && selectedSquare == i ? 2 : 0), BorderBrush = Ink(0x87e3d1),
                    CornerRadius = new CornerRadius(game == 12 ? 20 : 6), FontSize = 24,
                    Background = game == 13 ? near && !clues[i] ? Ink(0x34505f) : SudokuBoxes[box] : Ink(0x27454b),
                    Foreground = game == 12 ? value == 1 ? Ink(0xff9d8e) : White : value > 0 ? DigitColors[value-1] : White,
                    Content = game == 12 ? value > 0 ? "●" : enabled ? "·" : "" : value == 0 ? "" : value.ToString() };
                if (game == 12) cell.FontSize = value > 0 ? 40 : 28;
                StyleGamePiece(cell);
                if (game == 12 && value > 0) {
                    var disc = value == 1 ? Ink(0xff9d8e) : White;
                    cell.Content = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 33, Height = 33, Fill = RaisedFace(disc), StrokeThickness = 2,
                        Stroke = new SolidColorBrush(Shade(disc.Color, -.35)) };
                }
                AutomationProperties.SetAutomationId(cell, "GameCell" + i);
                AutomationProperties.SetName(cell, game == 12 ? $"Square {i + 1}: {(value == 1 ? "coral" : value == 2 ? "ivory" : enabled ? "playable" : "empty")}" : $"Square {i + 1}: {(value == 0 ? "empty" : value.ToString())}{(clues[i] ? " fixed" : "")}");
                cell.Click += (_, _) => AdvancedClick(index);
                Canvas.SetLeft(cell, i % columns * width + 2); Canvas.SetTop(cell, i / columns * width + 2); canvas.Children.Add(cell);
            }
            if (game == 13) for (var boundary = 1; boundary < 3; boundary++) { Rect(boundary * 160 - 2, 0, 4, 480, Ink(0x6d86ab), 0); Rect(0, boundary * 160 - 2, 480, 4, Ink(0x6d86ab), 0); }
        }
        if (game == 14) {
            if (!paused && !ended) status.Text = $"Level {stackLevel} · Score {score} · {gameMessage}";
            for (var i = 0; i < tower.Count; i++) Bevel(tower[i][0], 430 - i * 28, tower[i][1], 24, BlockColors[(stackLevel - tower.Count + i + 1 + BlockColors.Length * 100) % BlockColors.Length], 6);
            Bevel(stackX, 430 - tower.Count * 28, stackWidth, 24, Ink(0xabf1d2), 6); Label("SPACE / PLACE", 150, 28, 19);
        }
        if (game == 15) {
            var showing = tick < colorStart && !ended;
            rememberColor.IsEnabled = !showing && !paused && !ended;
            red.IsEnabled = green.IsEnabled = blue.IsEnabled = !showing && !paused && !ended;
            if (!paused && !ended) status.Text = showing ? $"Remember all five · {Math.Ceiling((colorStart - tick) * .03)}s" : $"Recreate color {chosenColors.Count + 1}/5 · Score {score}";
            for (var i = 0; i < 5; i++) {
                Bevel(8 + i * 94, 58, 88, 118, Ink(0x334b65), 14);
                Rect(10 + i * 94, 60, 84, 110, showing || ended ? new SolidColorBrush(Unpack(targetColors[i])) : Muted, 12);
                Label((i + 1).ToString(), 44 + i * 94, 185, 20);
                if (i < chosenColors.Count) { Rect(10 + i * 94, 230, 84, 110, new SolidColorBrush(Unpack(chosenColors[i])), 12); if (ended) Label($"ΔE {NativeGameRules.DeltaE(targetColors[i], chosenColors[i]):F1}", 15 + i * 94, 350, 16); }
            }
            if (!showing && !ended) { Rect(150, 365, 180, 60, new SolidColorBrush(Unpack(CurrentColor)), 15); Label($"#{CurrentColor & 0xffffff:X6}", 184, 433, 20); }
            if (showing) Label("Watch the order, then rebuild each shade.", 35, 270, 19);
        }
    }
}
