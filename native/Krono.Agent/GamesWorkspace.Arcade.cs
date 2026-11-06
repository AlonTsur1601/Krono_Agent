using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    ArcadeSave arcade = new();
    readonly Button arcadeAction = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly ToggleButton jigsawPreview = new() { Content = "Show reference", HorizontalAlignment = HorizontalAlignment.Center };
    Canvas? dartboard, flapScenery;
    const int MazeChase = 16, Flap = 17, Darts = 18, Jigsaw = 19;
    void BuildArcadeControls(StackPanel footer)
    {
        AutomationProperties.SetAutomationId(arcadeAction, "ArcadeAction");
        arcadeAction.Click += (_, _) => ArcadeAction(); jigsawPreview.Click += (_, _) => Draw();
        footer.Children.Add(arcadeAction); footer.Children.Add(jigsawPreview);
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
        if (game == Jigsaw) { int[] pieces; do { pieces = Enumerable.Range(0, 16).OrderBy(_ => random.Next()).ToArray(); } while (pieces.Select((piece, i) => piece == i).All(v => v)); Array.Copy(pieces, board, 16); }
    }
    void ArcadeAction()
    {
        if (ended || paused) return;
        if (game == Flap) { arcade.FlapStarted = true; arcade.BirdVelocity = -7.6; }
        if (game == Darts) {
            if (!arcade.XLocked) { arcade.DartX = AimX; arcade.DartAxisTick = tick; arcade.XLocked = true; }
            else { var y = AimY; var points = ArcadeRules.DartScore(arcade.DartX, y); arcade.Hits.Add([arcade.DartX, y]); arcade.XLocked = false; score += points; gameMessage = $"Last dart {points}"; if (arcade.Hits.Count == 9) End("Nine darts thrown"); }
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
            if (pellet > 0) { score += pellet == 2 ? 30 : 10; if (pellet == 2) arcade.Power = 24; arcade.Pellets[arcade.MazePlayer] = 0; }
            if (arcade.Pellets.All(n => n == 0)) { End("Maze cleared"); Draw(); return; }
            for (var i = 0; i < arcade.Ghosts.Length; i++) {
                if (arcade.Ghosts[i] == arcade.MazePlayer || (tick / 5 + i) % 2 == 0) arcade.Ghosts[i] = ArcadeRules.GhostMove(arcade.Ghosts[i], arcade.MazePlayer, arcade.Power > 0);
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
            arcade.BirdVelocity = Math.Min(12, arcade.BirdVelocity + .45); arcade.BirdY += arcade.BirdVelocity;
            foreach (var pipe in arcade.Pipes) {
                pipe[0] -= 3.1;
                if (ArcadeRules.FlapHits(arcade.BirdY, pipe[0], pipe[1])) { End("Wing clipped"); Draw(); return; }
                if (pipe[0] + 56 < 107 && pipe[2] == 0) { pipe[2] = 1; score += 10; }
            }
            arcade.Pipes.RemoveAll(p => p[0] < -56);
            if (arcade.Pipes.Count == 0 || arcade.Pipes[^1][0] < 245) arcade.Pipes.Add([480, random.Next(140, 341), 0]);
            if (arcade.BirdY < 13 || arcade.BirdY > 467) End("Flight ended");
            Draw();
        }
        if (game == Darts) Draw();
    }
    void SwapPiece(int index)
    {
        if (paused || ended || jigsawPreview.IsChecked == true) return;
        if (arcade.Piece < 0) arcade.Piece = index;
        else if (arcade.Piece == index) arcade.Piece = -1;
        else {
            (board[index], board[arcade.Piece]) = (board[arcade.Piece], board[index]); arcade.Piece = -1; moves++;
            if (board.Take(16).Select((piece, i) => piece == i).All(v => v)) { score = Math.Max(1, 1000 - moves * 15); End("Picture complete"); }
        }
        Draw(); SaveCurrent();
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
            foreach (var pipe in arcade.Pipes) {
                var x = pipe[0]; if (x > 480) continue;
                var top = pipe[1] - 80;
                var bottom = pipe[1] + 80;
                FlapPipe(x, 0, top, top - 15);
                FlapPipe(x, bottom, 480 - bottom, bottom);
            }
            var y = arcade.BirdY;
            var wing = arcade.FlapStarted && !ended ? Math.Sin(tick * .65) * 4 : 0;
            ArcadePolygon(canvas, Ink(0xc95750), (108, y + 2), (94, y - 4), (101, y + 8), (111, y + 8));
            Rect(105, y - 13, 29, 26, RaisedFace(Ink(0xffb64c)), 13);
            Rect(112, y + 3, 17, 7, Ink(0xffd99b), 3);
            Rect(102, y - 1 + wing, 17, 9, RaisedFace(Ink(0xef8650)), 4);
            Rect(122, y - 8, 9, 10, Ink(0xfff2d8), 4);
            Rect(126, y - 6, 4, 6, Ink(0x242139), 2);
            ArcadePolygon(canvas, Ink(0xf57547), (132, y - 1), (144, y + 2), (133, y + 6));
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
            if (running) status.Text = $"Moves {moves} · Swap two pieces";
            if (jigsawPreview.IsChecked == true) { canvas.Children.Add(JigsawScene()); return; }
            for (var i = 0; i < 16; i++) {
                var index = i; var piece = board[i];
                var viewport = new Canvas { Width = 116, Height = 116, Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 116, 116) } };
                var scene = JigsawScene(); Canvas.SetLeft(scene, -(piece % 4) * 120); Canvas.SetTop(scene, -(piece / 4) * 120); viewport.Children.Add(scene);
                var button = new Button { Style = (Style)Application.Current.Resources["KronoFlatButton"], Content = viewport, Width = 118, Height = 118, Padding = new Thickness(0), BorderBrush = arcade.Piece == index ? Accent : Ink(0x344d54), BorderThickness = new Thickness(arcade.Piece == index ? 3 : 1), CornerRadius = new CornerRadius(5) };
                AutomationProperties.SetAutomationId(button, "GameCell" + index); AutomationProperties.SetName(button, $"Tile {index + 1} · Piece {piece + 1}");
                button.Click += (_, _) => SwapPiece(index); Canvas.SetLeft(button, i % 4 * 120 + 1); Canvas.SetTop(button, i / 4 * 120 + 1); canvas.Children.Add(button);
            }
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
        sky.GradientStops.Add(new GradientStop { Color = Ink(0x272952).Color, Offset = 0 });
        sky.GradientStops.Add(new GradientStop { Color = Ink(0xe59185).Color, Offset = .77 });
        sky.GradientStops.Add(new GradientStop { Color = Ink(0xffcf98).Color, Offset = 1 });
        var scenery = new Canvas { Width = 480, Height = 480, Background = sky, IsHitTestVisible = false };
        void Disc(double x, double y, double size, Brush brush)
        {
            var disc = new Ellipse { Width = size, Height = size, Fill = brush };
            Canvas.SetLeft(disc, x); Canvas.SetTop(disc, y); scenery.Children.Add(disc);
        }
        Disc(317, 61, 120, Ink(0xb67c8c)); Disc(332, 76, 90, Ink(0xffc995));
        for (var i = 0; i < 16; i++) Disc(13 + i * 67 % 450, 18 + i * 41 % 150, 2 + i % 3, Ink(0xf4c7b5));
        ArcadePolygon(scenery, Ink(0x86709a), (0, 358), (91, 241), (162, 316), (240, 223), (340, 335), (420, 264), (480, 325), (480, 480), (0, 480));
        ArcadePolygon(scenery, Ink(0x60597e), (0, 370), (65, 335), (127, 376), (218, 300), (290, 365), (386, 313), (480, 357), (480, 480), (0, 480));
        ArcadePolygon(scenery, Ink(0x384963), (0, 405), (75, 382), (177, 415), (272, 376), (365, 421), (448, 383), (480, 393), (480, 480), (0, 480));
        return scenery;
    }
    void FlapPipe(double x, double y, double height, double collar)
    {
        Rect(x, y, 56, height, Ink(0x255d57), 0);
        Rect(x + 9, y, 30, height, Ink(0x41997a), 0);
        Rect(x + 12, y, 5, height, Ink(0x78c3a0), 0);
        Rect(x, collar, 56, 15, RaisedFace(Ink(0x61b690)), 4);
        Rect(x + 5, collar + 3, 46, 2, Ink(0x9adbbb), 1);
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
            var p = At(200, center); var label = new TextBlock { Text = ArcadeRules.DartSectors[i].ToString(), FontFamily = KronoDesign.CodeFont, FontSize = 18, Foreground = Ink(0xf1e1c7), Width = 30, TextAlignment = TextAlignment.Center }; Canvas.SetLeft(label, p.X - 15); Canvas.SetTop(label, p.Y - 12); result.Children.Add(label);
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
    static Canvas JigsawScene()
    {
        var scene = new Canvas { Width = 480, Height = 480, Background = new SolidColorBrush(Color.FromArgb(255, 21, 35, 65)), IsHitTestVisible = false };
        Circle(309, 36, 104, "EAC989"); Circle(332, 54, 20, "D3A96E"); Circle(371, 91, 24, "D3A96E");
        for (var i = 0; i < 21; i++) Circle((i * 73 + 19) % 480, (i * 41 + 11) % 180, i % 4 == 0 ? 5 : 3, "A8DAEC");
        Shape("59699A", (0, 298), (86, 166), (138, 222), (232, 104), (353, 271), (417, 156), (480, 254), (480, 480), (0, 480));
        Shape("ABB9E1", (193, 151), (232, 104), (277, 166), (250, 158), (233, 170), (219, 153));
        Shape("417C82", (0, 330), (70, 243), (171, 317), (271, 214), (369, 320), (480, 247), (480, 480), (0, 480));
        Shape("244F60", (0, 359), (90, 300), (194, 356), (310, 300), (424, 347), (480, 310), (480, 480), (0, 480));
        Shape("65BEC1", (314, 308), (249, 357), (270, 392), (139, 425), (100, 480), (244, 480), (324, 420), (305, 379), (373, 337));
        Shape("B2E1D6", (306, 381), (287, 390), (316, 411), (258, 439), (278, 444), (338, 415));
        foreach (var (x,y) in new[] { (37,367),(80,388),(411,366),(445,404),(367,414),(42,435),(466,448) }) Shape("182C40", (x,y-29), (x-15,y+4), (x-4,y+4), (x-4,y+15), (x+4,y+15), (x+4,y+4), (x+15,y+4));
        return scene;
        static SolidColorBrush Brush(string hex) => new(Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16)));
        void Circle(int x, int y, int size, string hex) { var circle = new Ellipse { Width = size, Height = size, Fill = Brush(hex) }; Canvas.SetLeft(circle, x); Canvas.SetTop(circle, y); scene.Children.Add(circle); }
        void Shape(string hex, params (int X, int Y)[] points) { var polygon = new Polygon { Fill = Brush(hex) }; foreach (var (x, y) in points) polygon.Points.Add(new Windows.Foundation.Point(x, y)); scene.Children.Add(polygon); }
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
        public int Piece { get; set; } = -1;
    }
    static bool ValidArcade(int game, GameSave s)
    {
        var a = s.Arcade; if (a is null) return false;
        return game switch {
            MazeChase => a.Pellets?.Length == ArcadeRules.Maze.Length && a.Pellets.All(n => n is >= 0 and <= 2) && ArcadeRules.MazeOpen(a.MazePlayer) && a.Ghosts?.Length == 2 && a.Ghosts.All(ArcadeRules.MazeOpen) && a.Lives is >= 0 and <= 3 && a.Power is >= 0 and <= 24 && a.MazeDirection is >= 0 and <= 4 && a.WantedDirection is >= 0 and <= 4,
            Flap => double.IsFinite(a.BirdY) && a.BirdY is >= 0 and <= 490 && double.IsFinite(a.BirdVelocity) && a.BirdVelocity is >= -12 and <= 12 && a.Pipes is { Count: > 0 and <= 4 } && a.Pipes.All(p => p?.Length == 3 && p[0] is >= -60 and <= 480 && p[1] is >= 140 and <= 340 && p[2] is 0 or 1),
            Darts => a.DartAxisTick >= 0 && a.DartAxisTick <= s.Tick && a.DartX is >= -187 and <= 187 && a.Hits is { Count: <= 9 } && a.Hits.All(p => p?.Length == 2 && p.All(n => n is >= -187 and <= 187)),
            Jigsaw => a.Piece is >= -1 and < 16 && s.Board.Take(16).Order().SequenceEqual(Enumerable.Range(0, 16)),
            _ => false
        };
    }
}
