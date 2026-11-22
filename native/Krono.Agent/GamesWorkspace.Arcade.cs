using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    ArcadeSave arcade = new();
    readonly Button arcadeAction = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly ToggleButton jigsawPreview = new() { Content = "Show reference", HorizontalAlignment = HorizontalAlignment.Center };
    Canvas? dartboard, flapScenery;
    Canvas? flapLayer, flapBird;
    readonly Dictionary<double[], Canvas> pipeViews = new();
    Rectangle? wingTop, wingShade;
    const int MazeChase = 16, Flap = 17, Darts = 18, Jigsaw = 19;
    const double FlapLift = -9.6, FlapGravity = .72;
    void BuildArcadeControls(StackPanel footer)
    {
        AutomationProperties.SetAutomationId(arcadeAction, "ArcadeAction");
        arcadeAction.Click += (_, _) => ArcadeAction(); jigsawPreview.Click += (_, _) => Draw();
        footer.Children.Add(arcadeAction); footer.Children.Add(jigsawPreview);
        canvas.PointerPressed += StartJigsawDrag;
        canvas.PointerMoved += MoveJigsawDrag;
        canvas.PointerReleased += CompleteJigsawDrag;
        canvas.PointerCaptureLost += (_, _) => CancelJigsawDrag();
    }
    void ResetArcade()
    {
        arcade = new ArcadeSave();
        arcadeAction.IsEnabled = true;
        arcadeAction.Visibility = game is Flap or Darts ? Visibility.Visible : Visibility.Collapsed;
        jigsawPreview.Visibility = game == Jigsaw ? Visibility.Visible : Visibility.Collapsed; jigsawPreview.IsChecked = false;
        arcadeAction.Content = game == Flap ? "Flap" : "Lock horizontal aim";
        if (game == MazeChase) arcade.Pellets = ArcadeRules.Maze.Select(c => c == '.' ? 1 : c == 'o' ? 2 : 0).ToArray();
        if (game == Flap) arcade.Pipes.Add([420, 245, 0]);
        if (game == Jigsaw) ResetJigsaw();
    }
    void ArcadeAction()
    {
        if (ended || paused) return;
        if (game is Flap or Darts) PlayGameSound(GameSound.Click);
        if (game == Flap) {
            var starting = !arcade.FlapStarted;
            arcade.FlapStarted = true;
            arcade.BirdVelocity = FlapLift;
            if (starting) Draw();
            SaveCurrent();
            return;
        }
        if (game == Darts) {
            if (!arcade.XLocked) { arcade.DartX = AimX; arcade.DartAxisTick = tick; arcade.XLocked = true; }
            else { var y = AimY; var points = ArcadeRules.DartScore(arcade.DartX, y); arcade.Hits.Add([arcade.DartX, y]); arcade.XLocked = false; score += points; gameMessage = $"Last dart {points}"; if (arcade.Hits.Count == 9) End("Nine darts thrown", won: true); }
        }
        Draw(); SaveCurrent();
    }
    double AimX => Math.Sin(tick * .113) * 187;
    double AimY => Math.Sin((tick - arcade.DartAxisTick) * .137) * 187;
    void UpdateArcade()
    {
        if (game == MazeChase && tick % 5 == 0 && arcade.WantedDirection != 0) {
            var next = ArcadeRules.MazeNext(arcade.MazePlayer, arcade.WantedDirection);
            if (next != arcade.MazePlayer) arcade.MazeDirection = arcade.WantedDirection;
            arcade.MazePlayer = ArcadeRules.MazeNext(arcade.MazePlayer, arcade.MazeDirection);
            var pellet = arcade.Pellets[arcade.MazePlayer];
            if (pellet > 0) { PlayGameSound(GameSound.Pickup); score += pellet == 2 ? 30 : 10; if (pellet == 2) arcade.Power = 24; arcade.Pellets[arcade.MazePlayer] = 0; }
            if (arcade.Pellets.All(n => n == 0)) { End("Maze cleared", won: true); Draw(); return; }
            for (var i = 0; i < arcade.Ghosts.Length; i++) {
                var ghost = arcade.Ghosts[i];
                if (ghost == arcade.MazePlayer || (tick / 5 + i) % (botDifficulty == 0 ? 3 : 2) == 0) {
                    if (BotMakesMistake && random.NextDouble() < .45) {
                        var exits = Enumerable.Range(1, 4).Select(direction => ArcadeRules.MazeNext(ghost, direction)).Where(cell => cell != ghost).ToArray();
                        if (exits.Length > 0) arcade.Ghosts[i] = exits[random.Next(exits.Length)];
                    } else arcade.Ghosts[i] = ArcadeRules.GhostMove(ghost, arcade.MazePlayer, arcade.Power > 0);
                }
                if (arcade.Ghosts[i] != arcade.MazePlayer) continue;
                if (arcade.Power > 0) { score += 50; arcade.Ghosts[i] = ArcadeSave.GhostHomes[i]; }
                else {
                    if (--arcade.Lives <= 0) End("Caught by a ghost");
                    else { arcade.MazePlayer = ArcadeRules.MazeHome; arcade.Ghosts = (int[])ArcadeSave.GhostHomes.Clone(); arcade.MazeDirection = arcade.WantedDirection = 0; gameMessage = "Choose a direction to continue"; }
                    break;
                }
            }
            if (arcade.Power > 0) arcade.Power--;
            Draw();
        }
        if (game == Flap && arcade.FlapStarted) {
            arcade.BirdVelocity = Math.Min(12, arcade.BirdVelocity + FlapGravity); arcade.BirdY += arcade.BirdVelocity;
            var pipeSpeed = Math.Min(4.6, 2.6 + score * .0075);
            foreach (var pipe in arcade.Pipes) {
                pipe[0] -= pipeSpeed;
                if (ArcadeRules.FlapHits(arcade.BirdY, pipe[0], pipe[1])) { End("Wing clipped"); Draw(); return; }
                if (pipe[0] + 56 < 107 && pipe[2] == 0) { pipe[2] = 1; score += 10; PlayGameSound(GameSound.Pickup); }
            }
            arcade.Pipes.RemoveAll(p => p[0] < -56);
            if (arcade.Pipes.Count == 0 || arcade.Pipes[^1][0] < 245) arcade.Pipes.Add([480, random.Next(140, 341), 0]);
            if (arcade.BirdY < 13 || arcade.BirdY > 467) End("Flight ended");
            if (ended || flapLayer is null) Draw();
            else { UpdateScore(); RefreshFlapFrame(); }
        }
        if (game == Darts) Draw();
    }
    void DrawArcade()
    {
        var running = !paused && !ended;
        if (game == MazeChase) {
            if (running) status.Text = $"Lives {arcade.Lives} · Score {score} · {arcade.Pellets.Count(n => n > 0)} dots left";
            for (var i = 0; i < ArcadeRules.Maze.Length; i++) {
                var x = i % 15 * 32; var y = i / 15 * 32;
                if (!ArcadeRules.MazeOpen(i)) Bevel(x + 1, y + 1, 30, 30, Ink(0x294d70), 5);
                else if (arcade.Pellets[i] > 0) { var size = arcade.Pellets[i] == 2 ? 12 : 4; Rect(x + 16 - size / 2, y + 16 - size / 2, size, size, Accent, size / 2); }
            }
            var cell = arcade.MazePlayer; Rect(cell % 15 * 32 + 5, cell / 15 * 32 + 5, 22, 22, Mint, 7);
            for (var i = 0; i < arcade.Ghosts.Length; i++) { cell = arcade.Ghosts[i]; Rect(cell % 15 * 32 + 5, cell / 15 * 32 + 6, 22, 20, arcade.Power > 0 ? White : new SolidColorBrush(colors[i == 0 ? 4 : 5]), 9); Rect(cell % 15 * 32 + 10, cell / 15 * 32 + 12, 4, 4, Muted, 2); Rect(cell % 15 * 32 + 18, cell / 15 * 32 + 12, 4, 4, Muted, 2); }
        }
        if (game == Flap) {
            if (running && !arcade.FlapStarted) status.Text = "Press Space or Flap to start";
            flapScenery ??= MakeFlapScenery();
            canvas.Children.Add(flapScenery);
            flapLayer = new Canvas { Width = 480, Height = 480, IsHitTestVisible = false };
            pipeViews.Clear();
            canvas.Children.Add(flapLayer);
            Rect(0, 468, 480, 12, Ink(0x315c58), 0);
            Rect(0, 468, 480, 2, Ink(0x8ac2a1), 0);
            flapBird = new Canvas { Width = 144, Height = 30, IsHitTestVisible = false };
            ArcadePolygon(flapBird, Ink(0xc35c3f), (109, 1), (98, -4), (103, 6), (110, 8));
            BirdRect(flapBird, 105, -13, 30, 26, 0xcb6d38, 13);
            BirdRect(flapBird, 106, -13, 28, 23, 0xffb85e, 12);
            BirdRect(flapBird, 112, 2, 17, 8, 0xffdfa5, 4);
            wingShade = BirdRect(flapBird, 106, -3, 15, 10, 0xe88742, 5);
            wingTop = BirdRect(flapBird, 109, -3, 11, 6, 0xffcf78, 3);
            BirdRect(flapBird, 122, -8, 9, 10, 0xfff4dc, 4.5);
            BirdRect(flapBird, 126, -6, 3.5, 5, 0x283136, 1.75);
            ArcadePolygon(flapBird, Ink(0xef8040), (132, -1), (141, 2), (132, 6));
            canvas.Children.Add(flapBird);
            RefreshFlapFrame();
        }
        if (game == Darts) {
            arcadeAction.Content = arcade.XLocked ? "Lock height & throw" : "Lock horizontal aim";
            arcadeAction.IsEnabled = running;
            if (running) status.Text = $"Dart {arcade.Hits.Count + 1}/9 · {(arcade.XLocked ? "2/2 Choose height" : "1/2 Choose horizontal position")} · Score {score}";
            dartboard ??= MakeDartboard(); canvas.Children.Add(dartboard);
            for (var i = 0; i < arcade.Hits.Count; i++) {
                var hit = arcade.Hits[i]; var x = 240 + hit[0]; var y = 240 + hit[1];
                var latest = i == arcade.Hits.Count - 1;
                Rect(x - 5, y - 5, 10, 10, Ink(0x1b1717), 5);
                canvas.Children.Add(new Line { X1 = x, Y1 = y, X2 = x + 12, Y2 = y - 15, Stroke = latest ? Ink(0xf6be84) : White, StrokeThickness = 2 });
                Rect(x + 8, y - 19, 10, 7, latest ? Ink(0xf6be84) : Ink(0xb7a79a), 2);
            }
            Rect(48, 14, 384, 18, Ink(0x372b29), 8);
            Rect(448, 48, 18, 384, Ink(0x372b29), 8);
            Rect(53, 22, 374, 2, Ink(0xab8c70), 1);
            Rect(456, 53, 2, 374, Ink(0xab8c70), 1);
            if (!ended) {
                var x = 240 + (arcade.XLocked ? arcade.DartX : AimX);
                var y = 240 + AimY;
                var guide = Ink(0x91efba);
                Rect(x - 2, 15, 4, 16, arcade.XLocked ? Ink(0xe4bc8a) : guide, 2);
                if (arcade.XLocked) Rect(449, y - 2, 16, 4, guide, 2);
            }
        }
        if (game == Jigsaw) {
            if (running) status.Text = $"Moves {moves} · Drag into the empty square";
            DrawJigsaw();
        }
    }
    static void ArcadePolygon(Canvas surface, Brush fill, params (double X, double Y)[] points)
    {
        var polygon = new Polygon { Fill = fill };
        foreach (var (x, y) in points) polygon.Points.Add(new(x, y));
        surface.Children.Add(polygon);
    }
    static Canvas MakeFlapScenery()
    {
        var sky = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        sky.GradientStops.Add(new GradientStop { Color = Ink(0x5d939e).Color, Offset = 0 });
        sky.GradientStops.Add(new GradientStop { Color = Ink(0xadd5cc).Color, Offset = .62 });
        sky.GradientStops.Add(new GradientStop { Color = Ink(0xffdfa5).Color, Offset = 1 });
        var scenery = new Canvas { Width = 480, Height = 480, Background = sky, IsHitTestVisible = false };
        void Disc(double x, double y, double size, Brush brush)
        {
            var disc = new Ellipse { Width = size, Height = size, Fill = brush };
            Canvas.SetLeft(disc, x); Canvas.SetTop(disc, y); scenery.Children.Add(disc);
        }
        Disc(339, 62, 78, Ink(0xe9dcaf)); Disc(347, 70, 62, Ink(0xffeabc));
        foreach (var (x, y) in new[] { (37, 86), (188, 143), (359, 200) }) {
            var cloud = Ink(0xd6e5d6);
            Disc(x, y + 8, 25, cloud); Disc(x + 13, y, 34, cloud); Disc(x + 35, y + 9, 24, cloud);
        }
        ArcadePolygon(scenery, Ink(0x83aaa0), (0, 355), (68, 290), (120, 319), (208, 256), (293, 337), (373, 277), (480, 342), (480, 480), (0, 480));
        ArcadePolygon(scenery, Ink(0x5c8a7d), (0, 401), (68, 360), (145, 385), (223, 333), (308, 385), (400, 342), (480, 371), (480, 480), (0, 480));
        ArcadePolygon(scenery, Ink(0x416f65), (0, 436), (92, 408), (172, 429), (254, 395), (347, 429), (432, 398), (480, 418), (480, 480), (0, 480));
        return scenery;
    }
    static Rectangle BirdRect(Canvas scene, double x, double y, double width, double height, int color, double radius = 0)
    {
        var shape = new Rectangle { Width = width, Height = height, Fill = Ink(color), RadiusX = radius, RadiusY = radius };
        Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); scene.Children.Add(shape);
        return shape;
    }

    void RefreshFlapFrame()
    {
        if (flapLayer is not { } layer || flapBird is not { } bird) return;
        foreach (var entry in pipeViews.Where(entry => !arcade.Pipes.Contains(entry.Key)).ToArray()) {
            layer.Children.Remove(entry.Value);
            pipeViews.Remove(entry.Key);
        }
        void Move(FrameworkElement part, double x, double y)
        {
            var previousX = Canvas.GetLeft(part);
            var previousY = Canvas.GetTop(part);
            Canvas.SetLeft(part, x); Canvas.SetTop(part, y);
            if (!double.IsNaN(previousX) && !double.IsNaN(previousY) && !ended)
                SlidePiece(part, previousX - x, previousY - y, 30);
        }
        foreach (var pipe in arcade.Pipes) {
            if (!pipeViews.TryGetValue(pipe, out var shape)) {
                shape = new Canvas { Width = 56, Height = 480 };
                void Pipe(double y, double height, double lip) {
                    BirdRect(shape, 0, y, 56, height, 0x244c50);
                    BirdRect(shape, 5, y, 45, height, 0x3b7571);
                    BirdRect(shape, 9, y, 4, height, 0x5d9690);
                    BirdRect(shape, 0, lip, 56, 15, 0x244c50, 4);
                    BirdRect(shape, 2, lip + 1, 52, 11, 0x78ab91, 3);
                }
                Pipe(0, pipe[1] - 80, pipe[1] - 95);
                Pipe(pipe[1] + 80, 400 - pipe[1], pipe[1] + 80);
                pipeViews.Add(pipe, shape);
                layer.Children.Add(shape);
            }
            Move(shape, pipe[0], 0);
        }
        Move(bird, 0, arcade.BirdY);
        if (wingShade is not null && wingTop is not null) {
            var y = arcade.FlapStarted && !ended ? Math.Sin(tick * .65) * 4 - 3 : -3;
            Canvas.SetTop(wingShade, y);
            Canvas.SetTop(wingTop, y);
        }
    }
    Canvas MakeDartboard()
    {
        var result = new Canvas { Width = 480, Height = 480, IsHitTestVisible = false };
        var rim = new Ellipse { Width = 376, Height = 376, Fill = RaisedFace(Ink(0x211d1c)), Stroke = RaisedFace(Ink(0x8b7159)), StrokeThickness = 5 }; Canvas.SetLeft(rim, 52); Canvas.SetTop(rim, 52); result.Children.Add(rim);
        var ringA = Ink(0xb8494e); var ringB = Ink(0x358463);
        for (var i = 0; i < 20; i++) {
            var center = i * 18 - 90; var band = i % 2 == 0 ? ringA : ringB;
            Segment(16, 170, center, i % 2 == 0 ? Ink(0x312c2b) : Ink(0xe2ceaa));
            Segment(156, 170, center, band); Segment(96, 108, center, band);
            var p = At(200, center); var label = new TextBlock { Tag = "game-code", Text = ArcadeRules.DartSectors[i].ToString(), FontFamily = KronoDesign.CodeFont, FontSize = 18, Foreground = Ink(0xf1e1c7), Width = 30, TextAlignment = TextAlignment.Center }; Canvas.SetLeft(label, p.X - 15); Canvas.SetTop(label, p.Y - 12); result.Children.Add(label);
        }
        foreach (var (radius, fill) in new[] { (16, ringB), (6, ringA) }) { var circle = new Ellipse { Width = 2 * radius, Height = 2 * radius, Fill = fill }; Canvas.SetLeft(circle, 240 - radius); Canvas.SetTop(circle, 240 - radius); result.Children.Add(circle); }
        return result;
        static Windows.Foundation.Point At(double radius, double angle) => new(240 + Math.Cos(angle * Math.PI / 180) * radius, 240 + Math.Sin(angle * Math.PI / 180) * radius);
        void Segment(int inside, int outside, int center, Brush color) {
            var wedge = new Polygon { Fill = color };
            for (var n = -9; n <= 9; n += 3) wedge.Points.Add(At(outside, center + n));
            for (var n = 9; n >= -9; n -= 3) wedge.Points.Add(At(inside, center + n));
            result.Children.Add(wedge);
        }
    }
    public sealed class ArcadeSave
    {
        internal static readonly int[] GhostHomes = [112, 22];
        public int[] Pellets { get; set; } = [];
        public int MazePlayer { get; set; } = ArcadeRules.MazeHome;
        public int[] Ghosts { get; set; } = (int[])GhostHomes.Clone();
        public int MazeDirection { get; set; }
        public int WantedDirection { get; set; }
        public int Power { get; set; }
        public int Lives { get; set; } = 3;
        public double BirdY { get; set; } = 235;
        public double BirdVelocity { get; set; }
        public bool FlapStarted { get; set; }
        public List<double[]> Pipes { get; set; } = [];
        public bool XLocked { get; set; }
        public int DartAxisTick { get; set; }
        public double DartX { get; set; }
        public List<double[]> Hits { get; set; } = [];
        public int Picture { get; set; }
        public bool SlidingPuzzle { get; set; }
    }
    static bool ValidArcade(int game, GameSave s)
    {
        var a = s.Arcade; if (a is null) return false;
        return game switch {
            MazeChase => a.Pellets?.Length == ArcadeRules.Maze.Length && a.Pellets.All(n => n is >= 0 and <= 2) && ArcadeRules.MazeOpen(a.MazePlayer) && a.Ghosts?.Length == 2 && a.Ghosts.All(ArcadeRules.MazeOpen) && a.Lives is >= 0 and <= 3 && a.Power is >= 0 and <= 24 && a.MazeDirection is >= 0 and <= 4 && a.WantedDirection is >= 0 and <= 4,
            Flap => double.IsFinite(a.BirdY) && a.BirdY is >= 0 and <= 490 && double.IsFinite(a.BirdVelocity) && a.BirdVelocity is >= -12 and <= 12 && a.Pipes is { Count: > 0 and <= 4 } && a.Pipes.All(p => p?.Length == 3 && p[0] is >= -60 and <= 480 && p[1] is >= 140 and <= 340 && p[2] is 0 or 1),
            Darts => a.DartAxisTick >= 0 && a.DartAxisTick <= s.Tick && a.DartX is >= -187 and <= 187 && a.Hits is { Count: <= 9 } && a.Hits.All(p => p?.Length == 2 && p.All(n => n is >= -187 and <= 187)),
            Jigsaw => a.Picture is >= 0 and < 10 && s.Board.Take(16).Order().SequenceEqual(Enumerable.Range(0, 16)),
            _ => false
        };
    }
}
