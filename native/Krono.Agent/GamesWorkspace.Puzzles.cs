using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    readonly Button orbitTap = new() { Content = "Tap orbit", HorizontalAlignment = HorizontalAlignment.Center };
    readonly List<int> sequence = [];
    int simonStart, simonInput, simonVisual, oddCell, oddRound;
    bool simonWaiting;
    Color oddBase, oddShade;
    double orbitAngle, orbitTarget, orbitSpeed;

    void ResetPuzzles()
    {
        orbitTap.Visibility = game == 11 ? Visibility.Visible : Visibility.Collapsed;
        if (game == 8) {
            // Generate only from legal moves on the solved board: every puzzle is solvable.
            do { Array.Clear(board); for (var i = 0; i < 16; i++) ToggleLights(random.Next(25)); } while (board.Take(25).All(value => value == 0));
        }
        if (game == 9) { sequence.Clear(); sequence.Add(random.Next(4)); simonInput = simonStart = 0; simonVisual = -3; simonWaiting = false; }
        if (game == 10) { oddRound = 1; NextOddColor(); }
        if (game == 11) { orbitAngle = 270; orbitTarget = 90; orbitSpeed = 2.3; }
    }
    void ToggleLights(int index)
    {
        foreach (var offset in new[] { 0, -5, 5, -1, 1 }) {
            var other = index + offset;
            if (other is >= 0 and < 25 && (offset is not (-1 or 1) || other / 5 == index / 5)) board[other] ^= 1;
        }
    }
    void NextOddColor()
    {
        oddCell = random.Next(16);
        oddBase = Color.FromArgb(255, (byte)random.Next(50, 180), (byte)random.Next(50, 180), (byte)random.Next(50, 180));
        var difference = Math.Max(8, 55 - oddRound * 2);
        oddShade = Color.FromArgb(255, (byte)(oddBase.R + difference), (byte)(oddBase.G + difference), (byte)(oddBase.B + difference));
    }
    int SimonCue => tick < simonStart ? -1 : (tick - simonStart) / 30 >= sequence.Count ? -2 : (tick - simonStart) % 30 < 20 ? sequence[(tick - simonStart) / 30] : -1;
    void UpdatePuzzles()
    {
        if (game == 9 && !simonWaiting) {
            var cue = SimonCue;
            if (cue == -2) simonWaiting = true;
            if (cue != simonVisual) { simonVisual = cue; Draw(); }
        }
        if (game == 11) { orbitAngle = (orbitAngle + orbitSpeed) % 360; Draw(); }
    }
    void PuzzleClick(int index)
    {
        if (paused || ended) return;
        if (game == 8 && index is >= 0 and < 25) {
            ToggleLights(index); moves++;
            if (board.Take(25).All(value => value == 0)) { score = Math.Max(1, 1000 - moves * 10); End("All lights out"); }
        }
        if (game == 9 && index is >= 0 and < 4 && simonWaiting) {
            if (index != sequence[simonInput]) End("Pattern missed");
            else if (++simonInput == sequence.Count) {
                score += sequence.Count * 10;
                if (sequence.Count == 20) End("All 20 patterns remembered");
                else { sequence.Add(random.Next(4)); simonInput = 0; simonStart = tick + 20; simonWaiting = false; simonVisual = -3; }
            }
        }
        if (game == 10 && index is >= 0 and < 16) {
            if (index != oddCell) End("Different shade missed");
            else { score += 100; if (oddRound++ == 20) End("20 shades found"); else NextOddColor(); }
        }
        Draw();
    }
    void HitOrbit()
    {
        if (game != 11 || paused || ended) return;
        var difference = Math.Abs((orbitAngle - orbitTarget + 540) % 360 - 180);
        if (difference > 32) End("Outside the arc");
        else { score += difference <= 7 ? 25 : 10; orbitTarget = (orbitTarget + random.Next(80, 281)) % 360; orbitSpeed = Math.Min(12, orbitSpeed + .35); }
        Draw();
    }
    void DrawPuzzles()
    {
        if (game == 11) { DrawOrbit(); return; }
        var columns = game == 8 ? 5 : game == 9 ? 2 : 4;
        var width = 480.0 / columns;
        if (!ended && !paused) status.Text = game switch { 8 => $"Moves {moves} · Turn every light off", 9 => simonWaiting ? $"Your turn · {simonInput}/{sequence.Count} · Score {score}" : $"Watch pattern {sequence.Count} · Score {score}", _ => $"Shade {oddRound}/20 · Score {score}" };
        for (var index = 0; index < columns * columns; index++) {
            var cellIndex = index;
            var lit = game == 8 ? board[index] == 1 : game == 9 && !simonWaiting && !ended && SimonCue == index;
            var color = game == 10 ? index == oddCell ? oddShade : oddBase : game == 9 ? colors[new[] { 0, 4, 2, 1 }[index]] : colors[8];
            var colorBrush = new SolidColorBrush(color);
            var button = new Button { Width = width - 12, Height = width - 12, CornerRadius = new CornerRadius(game == 9 ? 38 : 14), Padding = new Thickness(0),
                Background = game == 10 || lit ? colorBrush : Muted, BorderBrush = colorBrush, BorderThickness = new Thickness(game == 9 ? 2 : 0),
                Foreground = lit ? new SolidColorBrush(Color.FromArgb(255, 16, 35, 31)) : White, FontSize = game == 9 ? 40 : 25, Content = game == 9 ? lit ? "◉" : (index + 1).ToString() : game == 8 ? lit ? "●" : "·" : "" };
            AutomationProperties.SetAutomationId(button, "GameCell" + index);
            AutomationProperties.SetName(button, game == 10 ? $"Color {index + 1}: #{color.R:X2}{color.G:X2}{color.B:X2}" : game == 9 ? $"Pad {index + 1}: {(lit ? "lit" : "idle")}" : $"Light {index + 1}: {(lit ? "on" : "off")}");
            button.Click += (_, _) => PuzzleClick(cellIndex);
            Canvas.SetLeft(button, index % columns * width + 6); Canvas.SetTop(button, index / columns * width + 6); canvas.Children.Add(button);
        }
    }
    void DrawOrbit()
    {
        var ring = new Ellipse { Width = 344, Height = 344, Stroke = Muted, StrokeThickness = 22 };
        Canvas.SetLeft(ring, 68); Canvas.SetTop(ring, 68); canvas.Children.Add(ring);
        Arc(orbitTarget - 32, orbitTarget + 32, Accent, 24);
        Arc(orbitTarget - 7, orbitTarget + 7, White, 24);
        var radians = orbitAngle * Math.PI / 180;
        var dot = new Ellipse { Width = 20, Height = 20, Fill = White };
        Canvas.SetLeft(dot, 230 + Math.Cos(radians) * 172); Canvas.SetTop(dot, 230 + Math.Sin(radians) * 172); canvas.Children.Add(dot);
        Label(score.ToString(), 200, 192, 48); Label("10 · arc     25 · core", 111, 264, 21);
        Label("SPACE / TAP", 160, 310, 18);
        void Arc(double start, double finish, Brush brush, double thickness) {
            var line = new Polyline { Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            for (var degrees = start; degrees <= finish; degrees += 1) { var angle = degrees * Math.PI / 180; line.Points.Add(new Windows.Foundation.Point(240 + Math.Cos(angle) * 172, 240 + Math.Sin(angle) * 172)); }
            canvas.Children.Add(line);
        }
    }
}
