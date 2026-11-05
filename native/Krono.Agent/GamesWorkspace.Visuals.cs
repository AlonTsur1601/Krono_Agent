using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    readonly bool previewOnly;
    readonly Canvas effects = new() { Width = 480, Height = 480, IsHitTestVisible = false };
    readonly TextBlock scoreReadout = new() { FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    readonly TextBlock pointsGain = new() { FontSize = 13, Opacity = 0 };
    readonly ToggleButton flagMode = new() { Content = "Place flags", HorizontalAlignment = HorizontalAlignment.Center };
    int shownScore = -1;
    Canvas? playTexture;
    static readonly string[] MemorySymbols = ["✦", "◆", "☽", "✿", "●", "▲", "✚", "♥"];
    static SolidColorBrush Ink(int color) => new(Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color));
    static readonly SolidColorBrush[] MineNumbers = [Ink(0x6d5b45), Ink(0x245390), Ink(0x2f672b), Ink(0xa33635), Ink(0x59317b), Ink(0x873b25), Ink(0x187077), Ink(0x363542), Ink(0x635341)];
    static readonly SolidColorBrush[] DigitColors = [Ink(0x99c1ff), Ink(0x9de5bc), Ink(0xffb79e), Ink(0xd8baff), Ink(0xffd78b), Ink(0x8de1e5), Ink(0xffa9d1), Ink(0xb1e59a), Ink(0xa9b1ff)];
    static readonly SolidColorBrush[] BlockColors = [Ink(0x52e4ec), Ink(0xffde6d), Ink(0xbd86ff), Ink(0x69e99d), Ink(0xff7689), Ink(0x739fff), Ink(0xffa75e)];
    static readonly SolidColorBrush[] SudokuBoxes = [Ink(0x223348), Ink(0x32304b), Ink(0x263d41), Ink(0x3d2c40), Ink(0x213d4a), Ink(0x38364a), Ink(0x273b38), Ink(0x403446), Ink(0x25324d)];
    static readonly Brush BlockShine = new SolidColorBrush(Color.FromArgb(78, 255, 255, 255));
    static readonly Brush BlockShadow = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0));
    Grid ScoreHeader()
    {
        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.VerticalAlignment = VerticalAlignment.Center; status.TextWrapping = TextWrapping.Wrap; header.Children.Add(status);
        var score = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        score.Children.Add(new TextBlock { Text = "SCORE", CharacterSpacing = 160, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Opacity = .72 });
        score.Children.Add(scoreReadout); pointsGain.VerticalAlignment = VerticalAlignment.Center; score.Children.Add(pointsGain);
        var badge = new Border { Padding = new Thickness(13, 5, 12, 5), CornerRadius = new CornerRadius(12), Child = score,
            Background = new SolidColorBrush(Color.FromArgb(30, 115, 175, 167)) };
        Grid.SetColumn(badge, 1); header.Children.Add(badge); AutomationProperties.SetAutomationId(scoreReadout, "GameScore");
        return header;
    }
    void UpdateScore()
    {
        if (previewOnly || shownScore == score) return;
        scoreReadout.FontFamily = pointsGain.FontFamily = KronoDesign.CodeFont;
        scoreReadout.Foreground = pointsGain.Foreground = ReadableAccent(game);
        scoreReadout.Text = score.ToString("00");
        var difference = score - shownScore; var visible = shownScore >= 0 && difference > 0;
        shownScore = score; pointsGain.Text = visible ? "+" + difference : "";
        if (!visible) { pointsGain.Opacity = 0; return; }
        pointsGain.Opacity = 1;
        if (!new UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(pointsGain);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(0, 1); fade.InsertKeyFrame(.42f, 1); fade.InsertKeyFrame(1, 0); fade.Duration = TimeSpan.FromMilliseconds(760); visual.StartAnimation("Opacity", fade);
    }
    void ClearGlow(IEnumerable<(double X, double Y, double Width, double Height)> areas)
    {
        if (previewOnly) return;
        effects.Children.Clear();
        foreach (var (x, y, width, height) in areas) {
            var glow = new Rectangle { Width = width, Height = height, RadiusX = 5, RadiusY = 5, Fill = Accent, Opacity = .58 };
            Canvas.SetLeft(glow, x); Canvas.SetTop(glow, y); effects.Children.Add(glow);
        }
        if (!new UISettings().AnimationsEnabled) { effects.Children.Clear(); return; }
        var visual = ElementCompositionPreview.GetElementVisual(effects);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(0, 1); fade.InsertKeyFrame(.20f, .85f); fade.InsertKeyFrame(1, 0); fade.Duration = TimeSpan.FromMilliseconds(360); visual.StartAnimation("Opacity", fade);
    }
    void AnimateBoardEntry()
    {
        if (!new UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(canvas);
        visual.CenterPoint = new System.Numerics.Vector3(240, 240, 0);
        var settle = visual.Compositor.CreateVector3KeyFrameAnimation();
        settle.InsertKeyFrame(0, new(.96f, .96f, 1)); settle.InsertKeyFrame(1, System.Numerics.Vector3.One);
        settle.Duration = TimeSpan.FromMilliseconds(180); visual.StartAnimation("Scale", settle);
    }
    void StageStatus()
    {
        if (previewOnly) return;
        var full = status.Text;
        AutomationProperties.SetName(status, full);
        var caption = string.Join(" · ", full.Split(" · ", StringSplitOptions.RemoveEmptyEntries).Where(part => !part.StartsWith("Score ", StringComparison.Ordinal)));
        status.Text = caption.Length > 0 ? caption : game == 3 ? $"{flags.Count(flag => flag)}/10 flags · {revealed.Count(cell => cell)} dug" : names[game];
    }
    void ArcBackdrop()
    {
        canvas.Background = game == 3 ? Ink(0x203d35) : game == Flap ? Ink(0x172943) : game == Solitaire ? Ink(0x143b39) : Ink(0x101a2a);
        if (game is 0 or 5 or 7 or 11 or 14 or Darts or FallingBlocks or SandBlocks) {
            if (playTexture is null) {
                playTexture = new Canvas { IsHitTestVisible = false, Width = 480, Height = 480 };
                var faint = new SolidColorBrush(Color.FromArgb(21, 147, 184, 218));
                for (var x = 24; x < 480; x += 48) for (var y = 24; y < 480; y += 48) {
                    var dot = new Ellipse { Width = 2, Height = 2, Fill = faint }; Canvas.SetLeft(dot, x); Canvas.SetTop(dot, y); playTexture.Children.Add(dot);
                }
            }
            canvas.Children.Add(playTexture);
        }
    }
    void Bevel(double x, double y, double w, double h, Brush color, double radius = 6)
    {
        Rect(x, y, w, h, color, radius);
        if (w > 8 && h > 7) { Rect(x + 3, y + 2, w - 6, Math.Min(4, h / 4), BlockShine, 2); Rect(x + 3, y + h - 4, w - 6, 2, BlockShadow, 1); }
    }
    void ToggleMineFlag(int cell)
    {
        if (game != 3 || ended || paused || cell is < 0 or >= 64 || revealed[cell]) return;
        flags[cell] = !flags[cell]; Draw(); ClearGlow([(cell % 8 * 60.0 + 2, cell / 8 * 60.0 + 2, 56, 56)]); SaveCurrent();
    }
    void DrawMines()
    {
        for (var i = 0; i < 64; i++) {
            var index = i; var x = i % 8 * 60; var y = i / 8 * 60;
            var alt = (i % 8 + i / 8) % 2 == 0; var exposed = revealed[i] || ended && board[i] == -1;
            var mine = exposed && board[i] == -1; var n = exposed && !mine ? Neighbors(i).Count(j => board[j] == -1) : 0;
            var surface = new Canvas { Width = 56, Height = 56, IsHitTestVisible = false };
            var ground = mine ? Ink(0xf28874) : exposed ? Ink(alt ? 0xddc79d : 0xd2b78e) : Ink(alt ? 0x66a958 : 0x54994f);
            void Spot(double left, double top, double w, double h, Brush fill, double r) { var shape = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill }; Canvas.SetLeft(shape, left); Canvas.SetTop(shape, top); surface.Children.Add(shape); }
            Spot(0, 0, 56, 56, ground, 5);
            if (exposed && !mine) {
                Spot(2, 2, 52, 5, Ink(0xb99e7d), 2); Spot(4, 49, 48, 3, Ink(0xe9d4ac), 1);
                if (n > 0) { var number = new TextBlock { Text = n.ToString(), FontFamily = KronoDesign.CodeFont, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = MineNumbers[n], Width = 56, TextAlignment = TextAlignment.Center }; Canvas.SetTop(number, 8); surface.Children.Add(number); }
            } else if (mine) {
                Spot(12, 12, 32, 32, Ink(0x543044), 15); Spot(25, 5, 6, 46, Ink(0x543044), 3); Spot(5, 25, 46, 6, Ink(0x543044), 3); Spot(18, 17, 8, 8, White, 4);
            } else {
                Spot(4, 3, 48, 3, new SolidColorBrush(Color.FromArgb(65, 217, 246, 173)), 1);
                for (var j = 0; j < 3; j++) Spot(8 + (i * 11 + j * 17) % 40, 12 + (i * 7 + j * 13) % 34, 4, 2, Ink(alt ? 0x4f9447 : 0x70ad56), 1);
                if (flags[i]) {
                    Spot(19, 14, 4, 27, Ink(0x243c39), 2); Spot(13, 40, 22, 4, Ink(0x243c39), 2);
                    var flag = new Polygon { Fill = Ink(0xff785f) }; flag.Points.Add(new(21, 12)); flag.Points.Add(new(43, 20)); flag.Points.Add(new(21, 28)); surface.Children.Add(flag);
                }
            }
            var button = GameTileButton(i, x + 2, y + 2, 56, 56, surface, mine ? "mine" : exposed ? n.ToString() : flags[i] ? "flagged" : "unopened", ground, 5);
            button.Click += (_, _) => { if (flagMode.IsChecked == true) ToggleMineFlag(index); else PlayAt(x + 30, y + 30); };
            button.RightTapped += (_, e) => { e.Handled = true; ToggleMineFlag(index); };
        }
    }
    Button GameTileButton(int index, double x, double y, double width, double height, object content, string label, Brush background, double radius)
    {
        var button = new Button { Content = content, Width = width, Height = height, Padding = new Thickness(0), Background = background, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(radius), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, "GameCell" + index); AutomationProperties.SetName(button, $"Cell {index + 1}: {label}");
        Canvas.SetLeft(button, x); Canvas.SetTop(button, y); canvas.Children.Add(button); return button;
    }
    void DrawTicTacToe()
    {
        var winning = TicLine();
        for (var i = 0; i < 9; i++) {
            var x = i % 3 * 160; var y = i / 3 * 160; var value = board[i];
            var surface = new Canvas { Width = 146, Height = 146, IsHitTestVisible = false };
            var tone = value == 1 ? Ink(0x84e5cd) : Ink(0xf7a7c3);
            if (value == 1) { Mark(39, 39, 107, 107); Mark(107, 39, 39, 107); }
            else if (value == 2) { var ring = new Ellipse { Width = 88, Height = 88, StrokeThickness = 15, Stroke = tone }; Canvas.SetLeft(ring, 29); Canvas.SetTop(ring, 29); surface.Children.Add(ring); }
            else { var dot = new Ellipse { Width = 7, Height = 7, Fill = Ink(0x4a5977) }; Canvas.SetLeft(dot, 69); Canvas.SetTop(dot, 69); surface.Children.Add(dot); }
            var tile = GameTileButton(i, x + 7, y + 7, 146, 146, surface, value == 1 ? "X" : value == 2 ? "O" : "empty", Ink(winning.Contains(i) ? 0x34524f : 0x25334c), 19);
            if (winning.Contains(i)) { tile.BorderBrush = tone; tile.BorderThickness = new Thickness(3); }
            tile.Click += (_, _) => PlayAt(x + 80, y + 80);
            void Mark(double ax, double ay, double bx, double by) => surface.Children.Add(new Line { X1 = ax, Y1 = ay, X2 = bx, Y2 = by, Stroke = tone, StrokeThickness = 16, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
    }
    int[] TicLine()
    {
        if (!ended) return [];
        foreach (var cells in new int[][] { [0,1,2], [3,4,5], [6,7,8], [0,3,6], [1,4,7], [2,5,8], [0,4,8], [2,4,6] }) if (board[cells[0]] != 0 && cells.All(i => board[i] == board[cells[0]])) return cells;
        return [];
    }
    FrameworkElement BoardPreview(int index)
    {
        var scene = new GamesWorkspace(workspace, index).canvas;
        scene.IsHitTestVisible = false; AutomationProperties.SetAccessibilityView(scene, AccessibilityView.Raw);
        var surface = new Grid { Height = 155, Background = scene.Background, IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(surface, "GamePreview" + index);
        var view = new Viewbox { Child = scene, Stretch = Stretch.Uniform, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center };
        surface.Children.Add(view);
        scene.Loaded += Render;
        async void Render(object sender, RoutedEventArgs args) {
            scene.Loaded -= Render;
            try {
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(scene, 384, 384);
                if (bitmap.PixelWidth == 0 || !surface.IsLoaded) return;
                var picture = new Image { Source = bitmap, Width = 480, Height = 480, Stretch = Stretch.Uniform, IsHitTestVisible = false };
                AutomationProperties.SetAutomationId(picture, "GamePreviewImage" + index); view.Child = picture;
            } catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { /* Retain the original game scene when snapshotting is unavailable. */ }
        }
        return surface;
    }
    void SeedPreview()
    {
        score = 120;
        if (game == 0) { snake = [115,114,113,112,111,110,109,108,107,106,105,104,124,144,164,184,204,205,206,207,208,209,210,211,212,232,252,272,292,291,290,289,288,287]; food = 137; }
        if (game == 1) { board[0] = board[4] = board[8] = 1; board[1] = board[5] = 2; ended = true; endMessage = "You won"; }
        if (game == 2) { int[] sample = [2,4,0,0,8,16,2,0,32,64,8,0,128,256,32,4]; Array.Copy(sample, board, 16); }
        if (game == 3) {
            foreach (var mine in new[] { 6,13,20,24,31,38,42,50,57,62 }) board[mine] = -1;
            Reveal(9); flags[13] = flags[24] = true;
        }
        if (game == 4) { board[35] = 1; board[36] = 2; board[37] = 1; board[38] = 2; board[39] = 1; board[30] = 2; board[31] = 1; board[24] = 2; }
        if (game is 5 or 7) { ballX = 280; ballY = 267; paddle = 192; enemy = 312; if (game == 5) bricks.RemoveAll(b => b.Y > 50 && (int)b.X % 4 == 1); }
        if (game == 6) foreach (var i in new[] { 0,2,5,6,9,13 }) revealed[i] = true;
        if (game == 9) tick = 4;
        if (game == 11) { orbitAngle = 28; orbitTarget = 56; }
        if (game == 12) { NativeGameRules.ReversiPlace(board, 19, 1); NativeGameRules.ReversiPlace(board, NativeGameRules.ReversiBot(board), 2); }
        if (game == 13) selectedSquare = Array.FindIndex(clues, fixedCell => !fixedCell);
        if (game == 14) { tower.Clear(); tower.AddRange([[120,240],[136,224],[150,210],[161,193],[167,180],[176,166],[182,150]]); stackLevel = 6; stackWidth = 150; stackX = 246; }
        if (game == 16) { arcade.MazePlayer = 196; arcade.Ghosts = [82, 112]; foreach (var i in new[] {196,197,198,199,200,201,202,203,204,205}) arcade.Pellets[i] = 0; }
        if (game == 17) { arcade.BirdY = 210; arcade.Pipes = [[230,265,0],[440,215,0]]; }
        if (game == 18) { arcade.XLocked = true; arcade.DartX = -19; arcade.Hits = [[12,28],[-44,60],[10,-96]]; tick = 24; }
        if (game == 19) for (var i = 0; i < 16; i++) board[i] = i == 6 ? 10 : i == 10 ? 6 : i;
        if (game == 20) for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) if ((x + y * 3) % 7 < 3) board[y * 8 + x] = 1 + (x / 2 + y / 2) % 7;
        if (game is 21 or 22) {
            for (var y = FallingHeight - 1; y >= FallingHeight * .68; y--) for (var x = 0; x < FallingWidth; x++) if ((x * 11 + y * 7) % 11 < 8 || game == 21 && y % 3 > 0) blocks.Grid[y * FallingWidth + x] = (x / BlockScale + y / BlockScale) % 7 + 1;
            blocks.Y = FallingHeight / 4;
        }
        if (game == 23) cards.DrawStock();
    }
}
