using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Krono_Agent;

// Native XAML controls and Canvas. No embedded web page is used for games.
public sealed class GamesWorkspace : Grid, IDisposable
{
    readonly Workspace workspace;
    readonly Canvas canvas = new() { Width = 480, Height = 480, Background = new SolidColorBrush(Color.FromArgb(255, 15, 21, 30)) };
    readonly TextBlock status = new() { FontSize = 16 };
    readonly ComboBox picker = new() { ItemsSource = new[] { "Snake", "Tic-Tac-Toe", "2048", "Minesweeper", "Connect Four", "Breakout", "Memory Match", "Pong" }, SelectedIndex = 0, MinWidth = 180 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly Random random = new();
    int game, score, tick, dx = 1, dy, food, paddle = 220, first = -1, second = -1, hideAt, moves;
    bool paused, ended, turned;
    int[] board = new int[64]; bool[] revealed = new bool[64], flags = new bool[64];
    List<int> snake = []; List<(double X, double Y)> bricks = [];
    double ballX, ballY, ballDx, ballDy, enemy;
    Grid? playScreen;
    Grid? library;
    readonly string[] names = ["Snake", "Tic-Tac-Toe", "2048", "Minesweeper", "Connect Four", "Breakout", "Memory Match", "Pong"];
    static readonly string[] descriptions = ["Grow without hitting the walls", "An unbeaten minimax opponent", "Slide and merge matching tiles", "Reveal safe cells and flag mines", "Four connected discs to win", "Bounce through a field of bricks", "Turn cards and match pairs", "A rally against a reactive bot"];
    static readonly Color[] colors = [Color.FromArgb(255,143,215,199),Color.FromArgb(255,184,161,246),Color.FromArgb(255,242,201,130),Color.FromArgb(255,133,187,237),Color.FromArgb(255,244,146,159),Color.FromArgb(255,255,173,109),Color.FromArgb(255,177,165,244),Color.FromArgb(255,125,221,217)];
    SolidColorBrush Accent => new(colors[Math.Clamp(game, 0, 7)]);
    public GamesWorkspace(Workspace workspace)
    {
        this.workspace = workspace;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "GameStatus");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(picker, "GameSelector");
        RowSpacing = 16; RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var back = new Button { Content = "‹ Game library" }; back.Click += (_, _) => { if (library is not null && playScreen is not null) { timer.Stop(); playScreen.Visibility = Visibility.Collapsed; library.Visibility = Visibility.Visible; } }; toolbar.Children.Add(back); toolbar.Children.Add(picker);
        var restart = new Button { Content = "Restart" }; restart.Click += (_, _) => Reset(); toolbar.Children.Add(restart);
        var pause = new Button { Content = "Pause / resume" }; pause.Click += (_, _) => { paused = !paused; Draw(); }; toolbar.Children.Add(pause); Children.Add(toolbar);
        Grid.SetRow(status, 1); Children.Add(status);
        var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, MaxHeight = 510 };
        var stage = new Border { Child = view, Background = new SolidColorBrush(Color.FromArgb(255, 10, 18, 28)), CornerRadius = new CornerRadius(24), Padding = new Thickness(16), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 42, 60, 78)), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(stage, 2); Children.Add(stage);
        status.FontFamily = new FontFamily("Cascadia Mono"); status.FontSize = 25; status.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var footer = new StackPanel { Spacing = 8 };
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (name, vx, vy) in new[] { ("Move left", -1, 0), ("Move up", 0, -1), ("Move down", 0, 1), ("Move right", 1, 0) }) { var button = new Button { Content = name }; button.Click += (_, _) => Move(vx, vy); arrows.Children.Add(button); }
        footer.Children.Add(arrows); footer.Children.Add(new TextBlock { Text = "Arrow keys / WASD to move · Click to play board games · Right-click to flag mines", TextWrapping = TextWrapping.Wrap, Opacity = .65 }); Grid.SetRow(footer, 3); Children.Add(footer);
        picker.SelectionChanged += (_, _) => { game = picker.SelectedIndex; Reset(); };
        canvas.PointerPressed += (sender, args) => { if (game is 0 or 5 or 7) Click(sender, args); }; canvas.RightTapped += (_, e) => { if (game == 3 && !ended) { var point = e.GetPosition(canvas); var index = (int)(point.Y / 60) * 8 + (int)(point.X / 60); if (index is >= 0 and < 64 && !revealed[index]) flags[index] = !flags[index]; Draw(); } };
        IsTabStop = true; KeyDown += Key;
        timer.Tick += (_, _) => Update(); Loaded += (_, _) => { Focus(FocusState.Programmatic); if (playScreen?.Visibility == Visibility.Visible) timer.Start(); }; Unloaded += (_, _) => timer.Stop();
        Reset();
        BuildLibrary();
    }
    void BuildLibrary()
    {
        playScreen = new Grid { RowSpacing = RowSpacing, Visibility = Visibility.Collapsed };
        var heights = RowDefinitions.Select(row => row.Height).ToArray();
        foreach (var height in heights) playScreen.RowDefinitions.Add(new RowDefinition { Height = height });
        var children = Children.ToArray(); Children.Clear(); RowDefinitions.Clear(); foreach (var child in children) playScreen.Children.Add(child); Children.Add(playScreen);
        library = new Grid(); var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Games", FontFamily = new FontFamily("Segoe UI Variable Display"), FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Choose a game. Your agent can keep working.", FontSize = 15, Opacity = .6, Margin = new Thickness(0, 0, 0, 16) });
        var cards = new Grid(); var buttons = new List<Button>();
        for (var i = 0; i < names.Length; i++) {
            var index = i;
            var card = new StackPanel { Spacing = 10 };
            var poster = new Canvas { Width = 200, Height = 104 }; Poster(poster, i);
            card.Children.Add(new Viewbox { Child = poster, Stretch = Stretch.Uniform, MaxHeight = 130 });
            card.Children.Add(new TextBlock { Text = names[i], FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(14, 0, 14, 0) });
            card.Children.Add(new TextBlock { Text = descriptions[i], FontSize = 12, Opacity = .65, Margin = new Thickness(14, 0, 14, 6) });
            card.Children.Add(new TextBlock { Text = i is 1 or 4 or 7 ? "VS BOT    ↗" : "SOLO    ↗", FontSize = 10, CharacterSpacing = 160, Foreground = new SolidColorBrush(colors[i]), Margin = new Thickness(14, 0, 14, 14) });
            var button = new Button { Content = card, Padding = new Thickness(0), Margin = new Thickness(6), CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "GameTile" + i);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, names[i]);
            button.Click += (_, _) => { picker.SelectedIndex = index; Reset(); library.Visibility = Visibility.Collapsed; playScreen.Visibility = Visibility.Visible; timer.Start(); Focus(FocusState.Programmatic); };
            buttons.Add(button); cards.Children.Add(button);
        }
        void Arrange() { var columns = ActualWidth >= 850 ? 4 : ActualWidth >= 420 ? 2 : 1; cards.ColumnDefinitions.Clear(); cards.RowDefinitions.Clear(); for (var i = 0; i < columns; i++) cards.ColumnDefinitions.Add(new ColumnDefinition()); for (var i = 0; i < 8 / columns; i++) cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); for (var i = 0; i < buttons.Count; i++) { Grid.SetColumn(buttons[i], i % columns); Grid.SetRow(buttons[i], i / columns); } }
        SizeChanged += (_, _) => Arrange(); Arrange();
        content.Children.Add(cards); library.Children.Add(new ScrollViewer { Content = content }); Children.Add(library);
    }
    static void Poster(Canvas canvas, int game)
    {
        var accent = new SolidColorBrush(colors[game]); var faint = new SolidColorBrush(Color.FromArgb(255, 40, 53, 71));
        canvas.Background = new SolidColorBrush(Color.FromArgb(255, 15, 25, 38));
        void R(double x, double y, double w, double h, Brush color, double radius = 4) { var r = new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = color }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); canvas.Children.Add(r); }
        void O(double x, double y, double size, Brush color) { var circle = new Ellipse { Width = size, Height = size, Fill = color }; Canvas.SetLeft(circle, x); Canvas.SetTop(circle, y); canvas.Children.Add(circle); }
        if (game == 0) { foreach (var (x,y) in new[] {(40,24),(58,24),(76,24),(76,42),(76,60),(94,60),(112,60)}) R(x,y,15,15,accent,5); O(150,28,9,new SolidColorBrush(Color.FromArgb(255,244,146,159))); }
        if (game == 1) { var a = new Line { X1=44,Y1=30,X2=79,Y2=73,Stroke=accent,StrokeThickness=6 };var b = new Line { X1=79,Y1=30,X2=44,Y2=73,Stroke=accent,StrokeThickness=6 };canvas.Children.Add(a);canvas.Children.Add(b); var ring = new Ellipse { Width = 42, Height = 42, Stroke = accent, StrokeThickness = 6 }; Canvas.SetLeft(ring,119);Canvas.SetTop(ring,30);canvas.Children.Add(ring); }
        if (game == 2 || game == 6) { R(48,18,46,32,accent,7); R(100,18,46,32,faint,7); R(48,56,46,32,faint,7); R(100,56,46,32,accent,7); }
        if (game == 3 || game == 4) { for (var y=0;y<3;y++) for(var x=0;x<5;x++) if (game == 4) O(47+x*23,19+y*23,16,(x+y)%3==0?accent:faint); else R(47+x*23,19+y*23,17,17,(x+y)%4==0?accent:faint); }
        if (game == 5) { for(var y=0;y<2;y++) for(var x=0;x<5;x++) R(30+x*29,18+y*15,25,10,accent,3); R(82,82,40,5,accent); O(126,57,7,accent); }
        if (game == 7) { R(44,28,5,45,accent); R(151,37,5,45,accent); for(var i=0;i<5;i++)R(99,12+i*18,2,9,faint,1); O(113,41,9,accent); }
    }
    void Reset()
    {
        score = tick = moves = 0; paused = ended = turned = false; first = second = -1; dx = 1; dy = 0; board = new int[64]; revealed = new bool[64]; flags = new bool[64];
        snake = [210, 209, 208]; food = random.Next(400); paddle = 220; enemy = 220; ballX = 240; ballY = 300; ballDx = 3; ballDy = -3;
        while (snake.Contains(food)) food = random.Next(400);
        bricks = []; for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++) bricks.Add((x * 60 + 3, y * 25 + 30));
        if (game == 2) { SpawnTile(); SpawnTile(); }
        if (game == 6) { var cards = Enumerable.Range(1, 8).SelectMany(n => new[] { n, n }).OrderBy(_ => random.Next()).ToArray(); Array.Copy(cards, board, 16); }
        Draw();
    }
    void End(string message)
    {
        ended = true; status.Text = message + " · Score " + score;
        workspace.Execute("INSERT INTO game_scores(id,account_id,game_id,score,achieved_at) VALUES($p0,$p1,$p2,$p3,$p4)", Guid.NewGuid().ToString(), workspace.AccountId, picker.SelectedItem!.ToString(), score, Workspace.Now);
    }
    void Update()
    {
        if (paused || ended) return; tick++;
        if (game == 6 && second >= 0 && tick >= hideAt) { revealed[first] = revealed[second] = false; first = second = -1; Draw(); }
        if (game == 0 && tick % 4 == 0) {
            turned = false;
            var x = snake[0] % 20 + dx; var y = snake[0] / 20 + dy; var next = y * 20 + x;
            if (x is < 0 or >= 20 || y is < 0 or >= 20 || snake.Take(snake.Count - (next == food ? 0 : 1)).Contains(next)) { End("Game over"); return; }
            snake.Insert(0, next); if (next == food) { score += 10; do { food = random.Next(400); } while (snake.Contains(food)); } else snake.RemoveAt(snake.Count - 1); Draw();
        }
        if (game is 5 or 7) {
            ballX += ballDx; ballY += ballDy;
            if (game == 5) {
                if (ballX < 7 || ballX > 473) ballDx *= -1; if (ballY < 7) ballDy = Math.Abs(ballDy);
                if (ballY is > 438 and < 453 && ballX >= paddle - 45 && ballX <= paddle + 45 && ballDy > 0) { ballDy = -Math.Abs(ballDy); ballDx = (ballX - paddle) / 14; }
                var brick = bricks.FindIndex(b => ballX >= b.X && ballX <= b.X + 54 && ballY >= b.Y && ballY <= b.Y + 18);
                if (brick >= 0) { bricks.RemoveAt(brick); ballDy *= -1; score += 10; }
                if (ballY > 480) { End("Game over"); return; } if (bricks.Count == 0) { End("You cleared the board"); return; }
            } else {
                if (ballY < 7 || ballY > 473) ballDy *= -1;
                enemy += Math.Clamp(ballY - enemy, -2.5, 2.5);
                if (ballX is > 20 and < 35 && Math.Abs(ballY - paddle) < 45 && ballDx < 0) { ballDx = Math.Abs(ballDx) + .1; score++; }
                if (ballX is > 445 and < 460 && Math.Abs(ballY - enemy) < 45 && ballDx > 0) ballDx = -Math.Abs(ballDx);
                if (ballX < 0) { End("Game over"); return; } if (ballX > 480) { score += 5; ballX = ballY = 240; ballDx = -3; }
            }
            Draw();
        }
    }
    void Key(object sender, KeyRoutedEventArgs args)
    {
        var x = 0; var y = 0;
        switch (args.Key) { case Windows.System.VirtualKey.Left: case Windows.System.VirtualKey.A: x = -1; break; case Windows.System.VirtualKey.Right: case Windows.System.VirtualKey.D: x = 1; break; case Windows.System.VirtualKey.Up: case Windows.System.VirtualKey.W: y = -1; break; case Windows.System.VirtualKey.Down: case Windows.System.VirtualKey.S: y = 1; break; default: return; }
        args.Handled = true; Move(x, y);
    }
    void Move(int x, int y)
    {
        if (ended || paused) return;
        if (game == 0 && !turned && !(dx == -x && dy == -y)) { dx = x; dy = y; turned = true; }
        if (game == 2 && !ended) MoveTiles(x, y);
        if (game == 5) paddle = Math.Clamp(paddle + x * 25, 45, 435);
        if (game == 7) paddle = Math.Clamp(paddle + y * 25, 45, 435);
        Draw();
    }
    void Click(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(canvas).Position;
        PlayAt(point.X, point.Y, args.GetCurrentPoint(canvas).Properties.IsRightButtonPressed);
    }
    void PlayAt(double px, double py, bool right = false)
    {
        Focus(FocusState.Pointer); if (ended || paused) return;
        var point = new Windows.Foundation.Point(px, py);
        if (game is 5 or 7) { paddle = (int)Math.Clamp(game == 5 ? point.X : point.Y, 45, 435); return; }
        if (game == 1) { var index = (int)(point.Y / 160) * 3 + (int)(point.X / 160); if (index is < 0 or > 8 || board[index] != 0) return; board[index] = 1; if (!CheckBoard(3, 3, 3)) { var best = -1; var bestValue = int.MinValue; for (var i = 0; i < 9; i++) if (board[i] == 0) { board[i] = 2; var value = Minimax(false); board[i] = 0; if (value > bestValue) { best = i; bestValue = value; } } if (best >= 0) board[best] = 2; CheckBoard(3, 3, 3); } }
        if (game == 4) { var col = (int)(point.X / (480.0 / 7)); if (col is < 0 or > 6 || !DropPiece(col, 1)) return; if (!CheckBoard(7, 6, 4)) { var choice = -1; foreach (var candidate in new[] { 3, 2, 4, 1, 5, 0, 6 }) { var spot = Landing(candidate); if (spot < 0) continue; board[spot] = 2; var win = Winner(7, 6, 4) == 2; board[spot] = 0; if (win) { choice = candidate; break; } } if (choice < 0) foreach (var candidate in new[] { 3, 2, 4, 1, 5, 0, 6 }) { var spot = Landing(candidate); if (spot < 0) continue; board[spot] = 1; var danger = Winner(7, 6, 4) == 1; board[spot] = 0; if (danger) { choice = candidate; break; } } if (choice < 0) choice = new[] { 3, 2, 4, 1, 5, 0, 6 }.FirstOrDefault(c => Landing(c) >= 0); DropPiece(choice, 2); CheckBoard(7, 6, 4); } }
        if (game == 3) { if (right) return; var index = (int)(point.Y / 60) * 8 + (int)(point.X / 60); if (index is < 0 or > 63 || flags[index]) return; if (moves++ == 0) { var mines = Enumerable.Range(0, 64).Where(i => Math.Abs(i % 8 - index % 8) > 1 || Math.Abs(i / 8 - index / 8) > 1).OrderBy(_ => random.Next()).Take(10); foreach (var mine in mines) board[mine] = -1; } if (board[index] == -1) { revealed[index] = true; End("Mine hit"); } else { Reveal(index); if (revealed.Count(v => v) == 54) { score = 100; End("Minefield cleared"); } } }
        if (game == 6) { var index = (int)(point.Y / 120) * 4 + (int)(point.X / 120); if (index is < 0 or > 15 || revealed[index] || second >= 0) return; revealed[index] = true; if (first < 0) first = index; else { if (board[first] == board[index]) { score += 10; first = -1; if (revealed.Take(16).All(v => v)) End("All pairs matched"); } else { second = index; hideAt = tick + 25; } } }
        Draw();
    }
    int Landing(int col) { for (var row = 5; row >= 0; row--) if (board[row * 7 + col] == 0) return row * 7 + col; return -1; }
    bool DropPiece(int col, int player) { var index = Landing(col); if (index < 0) return false; board[index] = player; return true; }
    int Winner(int width, int height, int length)
    {
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (board[y * width + x] != 0) foreach (var (vx, vy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) }) { var count = 1; while (count < length && x + vx * count >= 0 && x + vx * count < width && y + vy * count >= 0 && y + vy * count < height && board[(y + vy * count) * width + x + vx * count] == board[y * width + x]) count++; if (count == length) return board[y * width + x]; }
        return 0;
    }
    bool CheckBoard(int width, int height, int length) { var winner = Winner(width, height, length); if (winner > 0) { score = winner == 1 ? 100 : 0; End(winner == 1 ? "You won" : "Bot won"); return true; } if (board.Take(width * height).All(n => n > 0)) { End("Draw"); return true; } return false; }
    int Minimax(bool bot) { var winner = Winner(3, 3, 3); if (winner > 0) return winner == 2 ? 10 : -10; if (board.Take(9).All(n => n != 0)) return 0; var best = bot ? -100 : 100; for (var i = 0; i < 9; i++) if (board[i] == 0) { board[i] = bot ? 2 : 1; var value = Minimax(!bot); board[i] = 0; best = bot ? Math.Max(best, value) : Math.Min(best, value); } return best; }
    IEnumerable<int> Neighbors(int index) { for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++) if ((x != 0 || y != 0) && index % 8 + x is >= 0 and < 8 && index / 8 + y is >= 0 and < 8) yield return index + x + y * 8; }
    void Reveal(int index) { if (revealed[index] || flags[index] || board[index] == -1) return; revealed[index] = true; score++; if (Neighbors(index).All(i => board[i] != -1)) foreach (var neighbor in Neighbors(index)) Reveal(neighbor); }
    void SpawnTile() { var empty = Enumerable.Range(0, 16).Where(i => board[i] == 0).ToArray(); if (empty.Length > 0) board[empty[random.Next(empty.Length)]] = random.Next(10) == 0 ? 4 : 2; }
    void MoveTiles(int x, int y)
    {
        var previous = board.Take(16).ToArray();
        for (var line = 0; line < 4; line++) { var indices = Enumerable.Range(0, 4).Select(n => x < 0 ? line * 4 + n : x > 0 ? line * 4 + 3 - n : y < 0 ? n * 4 + line : (3 - n) * 4 + line).ToArray(); var values = indices.Select(i => board[i]).Where(n => n > 0).ToList(); for (var i = 0; i < values.Count - 1; i++) if (values[i] == values[i + 1]) { values[i] *= 2; score += values[i]; values.RemoveAt(i + 1); } for (var i = 0; i < 4; i++) board[indices[i]] = i < values.Count ? values[i] : 0; }
        if (!previous.SequenceEqual(board.Take(16))) SpawnTile();
        if (!board.Take(16).Contains(0) && !Enumerable.Range(0, 16).Any(i => i % 4 < 3 && board[i] == board[i + 1] || i / 4 < 3 && board[i] == board[i + 4])) End("No moves left");
    }
    static readonly SolidColorBrush Mint = new(Color.FromArgb(255, 143, 215, 199));
    static readonly SolidColorBrush Muted = new(Color.FromArgb(255, 38, 48, 62));
    static readonly SolidColorBrush White = new(Color.FromArgb(255, 240, 244, 250));
    void Rect(double x, double y, double width, double height, Brush brush, double radius = 4) { var shape = new Rectangle { Width = width, Height = height, Fill = brush, RadiusX = radius, RadiusY = radius }; Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape); }
    void Label(string text, double x, double y, double size = 26, bool dark = false) { var label = new TextBlock { Text = text, FontSize = size, Foreground = dark ? new SolidColorBrush(Color.FromArgb(255, 16, 35, 31)) : White, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }; Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label); }
    void Draw()
    {
        canvas.Children.Clear(); status.Foreground = Accent; if (!ended) status.Text = (paused ? "Paused · " : "") + "Score " + score;
        if (game == 0) {
            for (var i=0;i<20;i++) { Rect(i*24,0,1,480,new SolidColorBrush(Color.FromArgb(18,143,215,199)),0); Rect(0,i*24,480,1,new SolidColorBrush(Color.FromArgb(18,143,215,199)),0); }
            for (var i=0;i<snake.Count;i++) { var cell=snake[i]; Rect(cell%20*24+2,cell/20*24+2,20,20,new SolidColorBrush(Color.FromArgb((byte)Math.Max(90,255-i*7),143,215,199)),7); }
            Rect(food%20*24+1,food/20*24+1,22,22,new SolidColorBrush(Color.FromArgb(40,244,146,159)),11); Rect(food%20*24+6,food/20*24+6,12,12,new SolidColorBrush(colors[4]),6);
        }
        if (game is 1 or 2 or 3 or 4 or 6) {
            var cols = game == 1 ? 3 : game == 3 ? 8 : game == 4 ? 7 : 4; var rows = game == 4 ? 6 : cols; var width = 480.0 / cols; var height = 480.0 / rows;
            for (var i = 0; i < cols * rows; i++) { var x = i % cols * width; var y = i / cols * height; Rect(x + 3, y + 3, width - 6, height - 6, game == 3 && revealed[i] || game == 6 && revealed[i] ? Mint : Muted, 8);
                string text = game switch { 1 => board[i] == 1 ? "X" : board[i] == 2 ? "O" : "", 2 => board[i] > 0 ? board[i].ToString() : "", 3 => revealed[i] ? board[i] == -1 ? "✹" : Neighbors(i).Count(n => board[n] == -1).ToString() : flags[i] ? "⚑" : "", 4 => board[i] == 1 ? "●" : board[i] == 2 ? "○" : "", 6 => revealed[i] ? board[i].ToString() : "?", _ => "" };
                var cell = new Button { Content = text, Width = width - 6, Height = height - 6, FontSize = game == 3 ? 21 : 32, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
                cell.Background = (game == 3 || game == 6) && revealed[i] ? Accent : game == 2 && board[i]>0 ? new SolidColorBrush(colors[((int)Math.Log2(board[i])-1)%8]) : Muted;
                cell.Foreground = ((game == 3 || game == 6) && revealed[i]) || game == 2 && board[i]>0 ? new SolidColorBrush(Color.FromArgb(255, 16, 35, 31)) : White;
                cell.CornerRadius = new CornerRadius(game == 4 ? 40 : game == 6 ? 14 : 9);
                cell.BorderThickness = new Thickness(0);
                if (game == 4) { cell.Content = null; cell.Background = board[i]==1 ? Accent : board[i]==2 ? White : Muted; }
                if (game == 6 && !revealed[i]) cell.Content = new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg")), Width = 42, Height = 42, Opacity = .6 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(cell, "GameCell" + i);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, $"Cell {i + 1}: {(text.Length == 0 ? "empty" : text)}");
                var clickX = x + width / 2; var clickY = y + height / 2;
                cell.Click += (_, _) => PlayAt(clickX, clickY);
                Canvas.SetLeft(cell, x + 3); Canvas.SetTop(cell, y + 3); canvas.Children.Add(cell);
            }
        }
        if (game is 5 or 7) { Rect(ballX - 6, ballY - 6, 12, 12, White, 6); if (game == 5) { Rect(paddle - 45, 450, 90, 12, Mint); foreach (var brick in bricks) Rect(brick.X, brick.Y, 54, 18, new SolidColorBrush(colors[(int)((brick.Y-30)/25)+1]),5); } else { for(var y=8;y<480;y+=28) Rect(239,y,2,14,Muted,1); Rect(20, paddle - 45, 12, 90, Accent,6); Rect(448, enemy - 45, 12, 90, White,6); } }
    }
    public void Dispose() => timer.Stop();
}
