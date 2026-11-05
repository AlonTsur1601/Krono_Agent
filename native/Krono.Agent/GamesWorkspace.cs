using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace Krono_Agent;

// Native XAML controls and Canvas. No embedded web page is used for games.
public sealed partial class GamesWorkspace : Grid, IDisposable
{
    readonly Workspace workspace;
    readonly Canvas canvas = new() { Width = 480, Height = 480, Background = new SolidColorBrush(Color.FromArgb(255, 15, 21, 30)) };
    readonly TextBlock status = new() { FontSize = 16 };
    readonly StackPanel movementControls = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock gameHelp = new() { TextWrapping = TextWrapping.Wrap, Opacity = .65 };
    readonly ComboBox picker = new() { ItemsSource = names, SelectedIndex = 0, MinWidth = 180 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly Random random = new();
    int game, score, tick, dx = 1, dy, food, paddle = 220, first = -1, second = -1, hideAt, moves;
    bool paused, ended, turned;
    int[] board = new int[64]; bool[] revealed = new bool[64], flags = new bool[64];
    List<int> snake = []; List<(double X, double Y)> bricks = [];
    double ballX, ballY, ballDx, ballDy, enemy;
    Grid? playScreen;
    Grid? library;
    static readonly string[] names = ["Snake", "Tic-Tac-Toe", "2048", "Minesweeper", "Connect Four", "Breakout", "Memory Match", "Pong", "Lights Out", "Simon", "Odd Color", "Orbit", "Reversi", "Sudoku", "Stack", "Color Gauntlet", "Maze Chase", "Flap", "Darts", "Jigsaw", "Block Fit", "Falling Blocks", "Sand Blocks", "Solitaire"];
    static readonly string[] descriptions = ["Grow without hitting the walls", "An unbeaten minimax opponent", "Slide and merge matching tiles", "Reveal safe cells and flag mines", "Four connected discs to win", "Bounce through a field of bricks", "Turn cards and match pairs", "A rally against a reactive bot", "Toggle a cross. Darken every light.", "Watch the pattern. Play it back.", "Find the tile with a different shade", "Tap inside the arc. Aim for its core.", "Capture lines. Protect the corners.", "One solution. Nine rows of possibility.", "Place, trim, and build a taller tower", "Remember five shades. Rebuild them.", "Clear the maze while ghosts give chase", "Tap to fly between the towers", "Time two axes across nine throws", "Reassemble an illustrated night valley", "Fit a tray of shapes and clear lines", "Rotate, stack and clear falling pieces", "Build a color path from settled grains", "Build four suited foundations"];
    static readonly Color[] colors = [Color.FromArgb(255,143,215,199),Color.FromArgb(255,184,161,246),Color.FromArgb(255,242,201,130),Color.FromArgb(255,133,187,237),Color.FromArgb(255,244,146,159),Color.FromArgb(255,255,173,109),Color.FromArgb(255,177,165,244),Color.FromArgb(255,125,221,217),Color.FromArgb(255,242,210,134),Color.FromArgb(255,166,174,251),Color.FromArgb(255,224,154,209),Color.FromArgb(255,134,213,234),Color.FromArgb(255,245,157,140),Color.FromArgb(255,171,181,246),Color.FromArgb(255,147,218,181),Color.FromArgb(255,235,175,222),Color.FromArgb(255,145,218,243),Color.FromArgb(255,246,190,132),Color.FromArgb(255,244,151,170),Color.FromArgb(255,158,218,205),Color.FromArgb(255,184,169,243),Color.FromArgb(255,141,219,204),Color.FromArgb(255,247,189,130),Color.FromArgb(255,240,164,186)];
    SolidColorBrush Accent => new(colors[Math.Clamp(game, 0, colors.Length - 1)]);
    SolidColorBrush ReadableAccent(int index) { var color = colors[index]; return new(ActualTheme == ElementTheme.Light ? Color.FromArgb(255, (byte)(color.R * .42), (byte)(color.G * .42), (byte)(color.B * .42)) : color); }
    public GamesWorkspace(Workspace workspace) : this(workspace, -1) { }
    private GamesWorkspace(Workspace workspace, int previewIndex)
    {
        this.workspace = workspace;
        if (previewIndex >= 0) { previewOnly = true; game = previewIndex; random = new Random(4081 + game); Reset(); SeedPreview(); Draw(); return; }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "GameStatus");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(picker, "GameSelector");
        RowSpacing = 16; RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var back = new Button { Content = "‹ Game library" }; back.Click += (_, _) => { PauseAndSave(); if (library is not null && playScreen is not null) { playScreen.Visibility = Visibility.Collapsed; library.Visibility = Visibility.Visible; } }; toolbar.Children.Add(back); toolbar.Children.Add(picker);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(back, "GameLibraryBack");
        var restart = new Button { Content = "Restart" }; restart.Click += (_, _) => { Reset(); stateReady = true; SaveCurrent(); timer.Start(); }; toolbar.Children.Add(restart);
        var pause = new Button { Content = "Pause / resume" }; pause.Click += (_, _) => { if (ended) return; paused = !paused; if (!paused) timer.Start(); else timer.Stop(); Draw(); SaveCurrent(); }; toolbar.Children.Add(pause); Children.Add(toolbar);
        var header = ScoreHeader(); Grid.SetRow(header, 1); Children.Add(header);
        var stageContent = new Grid { Width = 480, Height = 480, CornerRadius = new CornerRadius(12) }; stageContent.Children.Add(canvas); stageContent.Children.Add(effects);
        var view = new Viewbox { Child = stageContent, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, MaxHeight = 510 };
        var gradient = new LinearGradientBrush { StartPoint = new(0,0), EndPoint = new(1,1) };
        gradient.GradientStops.Add(new GradientStop { Color = Ink(0x122238).Color, Offset = 0 }); gradient.GradientStops.Add(new GradientStop { Color = Ink(0x192e43).Color, Offset = 1 });
        var stage = new Border { Child = view, Background = gradient, CornerRadius = new CornerRadius(24), Padding = new Thickness(13), BorderThickness = new Thickness(1), BorderBrush = Ink(0x3c536b), HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(stage, "GameStage"); Grid.SetRow(stage, 2); Children.Add(stage);
        status.FontFamily = KronoDesign.CodeFont; status.FontSize = 18; status.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var footer = new StackPanel { Spacing = 8 };
        var arrows = movementControls;
        foreach (var (name, vx, vy) in new[] { ("Move left", -1, 0), ("Move up", 0, -1), ("Move down", 0, 1), ("Move right", 1, 0) }) { var button = new Button { Content = name }; button.Click += (_, _) => Move(vx, vy); arrows.Children.Add(button); }
        orbitTap.Click += (_, _) => HitOrbit(); footer.Children.Add(orbitTap);
        BuildAdvancedControls(footer);
        BuildArcadeControls(footer);
        BuildBoardControls(footer);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(flagMode, "MineFlagMode"); footer.Children.Add(flagMode);
        footer.Children.Add(arrows); footer.Children.Add(gameHelp); Grid.SetRow(footer, 3); Children.Add(footer);
        picker.SelectionChanged += async (_, _) => { if (!changingPicker && picker.SelectedIndex >= 0 && picker.SelectedIndex != game) { var next = picker.SelectedIndex; SetPicker(game); await ChooseGame(next); } };
        canvas.PointerPressed += (sender, args) => { if (game is 0 or 5 or 7) Click(sender, args); };
        IsTabStop = true; KeyDown += Key;
        timer.Tick += (_, _) => {
            // A window can be minimized while already inactive, with no new Activated event.
            if (MainWindow.Instance?.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) { PauseAndSave("Krono window minimized"); return; }
            Update(); if (!paused && !ended && tick % 60 == 0) SaveCurrent();
        }; Loaded += (_, _) => { Focus(FocusState.Programmatic); if (!paused && playScreen?.Visibility == Visibility.Visible) timer.Start(); }; Unloaded += (_, _) => PauseAndSave();
        Reset();
        BuildLibrary();
        ActualThemeChanged += (_, _) => { status.Foreground = ReadableAccent(game); for (var i = 0; i < saveLabels.Count; i++) saveLabels[i].Foreground = ReadableAccent(i); };
    }
    void BuildLibrary()
    {
        playScreen = new Grid { RowSpacing = RowSpacing, Visibility = Visibility.Collapsed };
        var heights = RowDefinitions.Select(row => row.Height).ToArray();
        foreach (var height in heights) playScreen.RowDefinitions.Add(new RowDefinition { Height = height });
        var children = Children.ToArray(); Children.Clear(); RowDefinitions.Clear(); foreach (var child in children) playScreen.Children.Add(child); Children.Add(playScreen);
        library = new Grid(); var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Games", FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"{names.Length} games · Your agent can keep working.", FontSize = 15, Opacity = .6, Margin = new Thickness(0, 0, 0, 6) });
        var search = new TextBox { PlaceholderText = "Search games", MinWidth = 160 };
        var category = new ComboBox { ItemsSource = new[] { "All categories", "Puzzle", "Arcade", "Strategy" }, SelectedIndex = 0, MinWidth = 138 };
        var favoritesOnly = new ToggleButton { Content = "Favorites", CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 8, 13, 8) };
        var recentOnly = new ToggleButton { Content = "Recent", CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 8, 13, 8) };
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var control in new Control[] { search, category, favoritesOnly, recentOnly }) filters.Children.Add(control);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(search, "GameSearch"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(category, "GameCategory");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(favoritesOnly, "GameFavoritesFilter"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(recentOnly, "GameRecentFilter");
        content.Children.Add(filters);
        var empty = new TextBlock { Text = "No games match these filters.", Opacity = .7, Margin = new Thickness(6, 20, 0, 0), Visibility = Visibility.Collapsed };
        content.Children.Add(empty);
        var cards = new Grid(); var buttons = new List<Grid>();
        var favorites = new HashSet<string>(StringComparer.Ordinal);
        try { if (workspace.Setting("native.gameFavorites") is { Length: > 0 } json) favorites.UnionWith(System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []); } catch (System.Text.Json.JsonException) { }
        for (var i = 0; i < names.Length; i++) {
            var index = i;
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(BoardPreview(i));
            card.Children.Add(new TextBlock { Text = names[i], FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(14, 0, 14, 0) });
            card.Children.Add(new TextBlock { Text = descriptions[i], FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Normal, Opacity = .72, TextWrapping = TextWrapping.Wrap, MinHeight = 34, Margin = new Thickness(14, 0, 14, 6) });
            var saveLabel = new TextBlock { Text = BestCaption(i), FontFamily = KronoDesign.CodeFont, FontSize = 10, CharacterSpacing = 85, Foreground = ReadableAccent(i), Margin = new Thickness(14, 0, 14, 14) }; saveLabels.Add(saveLabel); card.Children.Add(saveLabel);
            var button = new Button { Content = card, Padding = new Thickness(0), CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "GameTile" + i);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, names[i]);
            button.Click += async (_, _) => await ChooseGame(index);
            var starred = favorites.Contains(names[i]);
            var star = new ToggleButton { Content = starred ? "★" : "☆", IsChecked = starred, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), Padding = new Thickness(8, 4, 8, 4), FontSize = 18, CornerRadius = new CornerRadius(10), Background = Ink(0x293a51), Foreground = starred ? Ink(0xffd389) : White, BorderThickness = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(star, "FavoriteGame" + i);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(star, "Favorite " + names[i]);
            void SaveFavorite(object sender, RoutedEventArgs e) { if (star.IsChecked == true) favorites.Add(names[index]); else favorites.Remove(names[index]); star.Content = star.IsChecked == true ? "★" : "☆"; star.Foreground = star.IsChecked == true ? Ink(0xffd389) : White; workspace.SetSetting("native.gameFavorites", System.Text.Json.JsonSerializer.Serialize(favorites)); Arrange(); }
            star.Checked += SaveFavorite; star.Unchecked += SaveFavorite;
            var cardFrame = new Grid { Margin = new Thickness(6) }; cardFrame.Children.Add(button); cardFrame.Children.Add(star); buttons.Add(cardFrame); cards.Children.Add(cardFrame);
        }
        string Category(int i) => i is 1 or 4 or 12 or Solitaire ? "Strategy" : i is 2 or 3 or 6 or 8 or 9 or 10 or 13 or 15 or Jigsaw or BlockFit ? "Puzzle" : "Arcade";
        void Arrange() {
            if (disposed) return;
            filters.Orientation = ActualWidth is > 0 and < 520 ? Orientation.Vertical : Orientation.Horizontal;
            var recent = recentOnly.IsChecked == true ? workspace.Query("SELECT game_id FROM game_saves WHERE account_id=$p0 AND id LIKE 'native-v1:%' ORDER BY saved_at DESC,game_id LIMIT 8", workspace.AccountId).Select(row => row["game_id"]?.ToString() ?? "").ToArray() : [];
            var query = search.Text.Trim(); var chosen = category.SelectedItem?.ToString();
            var visible = Enumerable.Range(0, names.Length).Where(i => (chosen is null or "All categories" || Category(i) == chosen) && (favoritesOnly.IsChecked != true || favorites.Contains(names[i])) && (recentOnly.IsChecked != true || recent.Contains(names[i])) && (query.Length == 0 || names[i].Contains(query, StringComparison.OrdinalIgnoreCase) || descriptions[i].Contains(query, StringComparison.OrdinalIgnoreCase)));
            if (recentOnly.IsChecked == true) visible = visible.OrderBy(i => Array.IndexOf(recent, names[i]));
            var shown = visible.ToArray(); var maxColumns = ActualWidth >= 850 ? 4 : ActualWidth >= 650 ? 3 : ActualWidth >= 420 ? 2 : 1;
            var columns = Math.Clamp(shown.Length, 1, maxColumns); empty.Visibility = shown.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            cards.ColumnDefinitions.Clear(); cards.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) cards.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < (shown.Length + columns - 1) / columns; i++) cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var frame in buttons) frame.Visibility = Visibility.Collapsed;
            for (var i = 0; i < shown.Length; i++) { var frame = buttons[shown[i]]; Grid.SetColumn(frame, i % columns); Grid.SetRow(frame, i / columns); frame.Visibility = Visibility.Visible; }
        }
        search.TextChanged += (_, _) => Arrange(); category.SelectionChanged += (_, _) => Arrange(); favoritesOnly.Checked += (_, _) => Arrange(); favoritesOnly.Unchecked += (_, _) => Arrange(); recentOnly.Checked += (_, _) => Arrange(); recentOnly.Unchecked += (_, _) => Arrange();
        SizeChanged += (_, _) => Arrange(); library.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => { if (library.Visibility == Visibility.Visible) Arrange(); }); Arrange();
        content.Children.Add(cards); library.Children.Add(new ScrollViewer { Content = content }); Children.Add(library);
    }
    void Reset()
    {
        movementControls.Visibility = game is 0 or 2 or 5 or 7 or MazeChase or FallingBlocks or SandBlocks ? Visibility.Visible : Visibility.Collapsed;
        flagMode.Visibility = game == 3 ? Visibility.Visible : Visibility.Collapsed; flagMode.IsChecked = false; effects.Children.Clear(); shownScore = -1;
        gameHelp.Text = game switch { 0 => "Arrow keys / WASD · Eat the coral dots and avoid walls and your tail.", 1 => "Choose a square. Connect three marks against the minimax bot.", 2 => "Arrow keys / WASD · Merge matching tiles; each tile merges once per move.", 3 => "Choose a cell to reveal it. Right-click to flag a mine; the first move is safe.", 4 => "Choose a column. Connect four discs before the bot does.", 5 => "Left / right arrows or click to position the paddle. Clear every brick.", 6 => "Turn two cards at a time. Remember their positions and match every pair.", 7 => "Up / down arrows or click to position your paddle. Return the bot's serves.", 8 => "Each tile toggles itself and its four neighbors. Turn every light off.", 9 => "Watch the lit pads, then repeat their order. Click or press 1–4. Reach 20 rounds.", 10 => "Find the one tile with a different shade. Twenty rounds get progressively closer.", _ => "Press Space or Tap orbit inside the colored arc. The white core scores 25; the arc scores 10. Each hit increases the speed." };
        score = tick = moves = 0; paused = ended = turned = false; endMessage = ""; first = second = -1; dx = 1; dy = 0; board = new int[64]; revealed = new bool[64]; flags = new bool[64];
        snake = [210, 209, 208]; food = random.Next(400); paddle = 220; enemy = 220; ballX = 240; ballY = 300; ballDx = 3; ballDy = -3;
        while (snake.Contains(food)) food = random.Next(400);
        bricks = []; for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++) bricks.Add((x * 60 + 3, y * 25 + 30));
        if (game == 2) { SpawnTile(); SpawnTile(); }
        if (game == 6) { var cards = Enumerable.Range(1, 8).SelectMany(n => new[] { n, n }).OrderBy(_ => random.Next()).ToArray(); Array.Copy(cards, board, 16); }
        ResetPuzzles();
        ResetAdvanced();
        ResetArcade();
        ResetBoardGames();
        if (game >= 12) gameHelp.Text = game switch { 12 => "Coral moves first. Bracket ivory discs along a row, column or diagonal. Dots mark legal moves; a player with no move passes. Most discs wins when both pass.", 13 => "Select an empty square; use 1–9 or the digit buttons. Erase with Backspace/Delete. Each row, column and 3×3 box contains 1–9 once. Conflicting moves are rejected.", 14 => "Space / Place block · Keep the overlap. Near-exact alignment is perfect; every three consecutive perfects restores 8% of the original width, up to the original size.", _ => "Remember five colors in order, then recreate each with the RGB fields. Each color scores 0–100 using CIE76 Delta E in D65 CIELAB; all five total up to 500." };
        if (game >= MazeChase) gameHelp.Text = game switch { MazeChase => "Arrow keys / WASD · Clear every dot. Large dots temporarily let you catch ghosts. You have three lives.", Flap => "Space / Flap · Each tap lifts your bird. Pass between the towers without touching them or the ground.", Darts => "Space / Aim / throw · First press locks horizontal aim; second throws. Nine darts use standard singles, doubles, triples and bullseye scoring.", _ => "Select two pieces to swap them. Show reference previews the original illustration. Assemble the valley in as few moves as possible." };
        if (game >= BlockFit) gameHelp.Text = game switch { BlockFit => "Select a tray piece (1–3), then its top-left square. Full rows and columns clear together. The tray refills when emptied; the game ends when no piece fits.", FallingBlocks => "Left/right to move, up to rotate, down to lower, Space for hard drop. Clear complete rows. The outline shows the landing position.", SandBlocks => "Move and rotate as in Falling Blocks. Locked pieces turn into grains; a connected color spanning both walls clears when the sand settles.", _ => "Draw from Stock. Select a face-up card or run, then its destination. Tableau descends in alternating colors; empty columns require kings. Foundations ascend by suit from ace to king." };
        if (!previewOnly) Draw();
    }
    void End(string message)
    {
        if (ended) return;
        ended = true; endMessage = message; status.Text = message + " · Score " + score;
        if (previewOnly) return;
        workspace.Execute("INSERT INTO game_scores(id,account_id,game_id,score,achieved_at) VALUES($p0,$p1,$p2,$p3,$p4)", Guid.NewGuid().ToString(), workspace.AccountId, names[game], score, Workspace.Now);
        if (game < saveLabels.Count) saveLabels[game].Text = BestCaption(game);
        UpdateScore(); StageStatus();
        SaveCurrent();
    }
    void Update()
    {
        if (paused || ended) return; tick++;
        UpdatePuzzles();
        UpdateAdvanced();
        UpdateArcade();
        UpdateBoardGames();
        if (game == 6 && second >= 0 && tick >= hideAt) { revealed[first] = revealed[second] = false; first = second = -1; Draw(); }
        if (game == 0 && tick % 4 == 0) {
            turned = false;
            var x = snake[0] % 20 + dx; var y = snake[0] / 20 + dy; var next = y * 20 + x;
            if (x is < 0 or >= 20 || y is < 0 or >= 20 || snake.Take(snake.Count - (next == food ? 0 : 1)).Contains(next)) { End("Game over"); return; }
            snake.Insert(0, next); if (next == food) { score += 10; if (snake.Count == 400) { End("Board filled"); Draw(); return; } var empty = Enumerable.Range(0, 400).Where(cell => !snake.Contains(cell)).ToArray(); food = empty[random.Next(empty.Length)]; } else snake.RemoveAt(snake.Count - 1); Draw();
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
        if (game is Flap or Darts && args.Key == Windows.System.VirtualKey.Space) { args.Handled = true; ArcadeAction(); return; }
        if (game is FallingBlocks or SandBlocks && args.Key == Windows.System.VirtualKey.Space) { args.Handled = true; DropNow(); return; }
        if (game == BlockFit && args.Key >= Windows.System.VirtualKey.Number1 && args.Key <= Windows.System.VirtualKey.Number3) { args.Handled = true; PickFit((int)args.Key - (int)Windows.System.VirtualKey.Number1); return; }
        if (game == 14 && args.Key == Windows.System.VirtualKey.Space) { args.Handled = true; PlaceStack(); return; }
        if (game == 13) {
            var digit = args.Key >= Windows.System.VirtualKey.Number1 && args.Key <= Windows.System.VirtualKey.Number9 ? (int)args.Key - (int)Windows.System.VirtualKey.Number0 : args.Key >= Windows.System.VirtualKey.NumberPad1 && args.Key <= Windows.System.VirtualKey.NumberPad9 ? (int)args.Key - (int)Windows.System.VirtualKey.NumberPad0 : -1;
            if (args.Key is Windows.System.VirtualKey.Back or Windows.System.VirtualKey.Delete) digit = 0;
            if (digit >= 0) { args.Handled = true; EnterSudoku(digit); return; }
        }
        if (game == 11 && args.Key == Windows.System.VirtualKey.Space) { args.Handled = true; HitOrbit(); return; }
        if (game == 9 && args.Key >= Windows.System.VirtualKey.Number1 && args.Key <= Windows.System.VirtualKey.Number4) { args.Handled = true; PuzzleClick((int)args.Key - (int)Windows.System.VirtualKey.Number1); return; }
        if (game is not (0 or 2 or 5 or 7 or MazeChase or FallingBlocks or SandBlocks)) return;
        var x = 0; var y = 0;
        switch (args.Key) { case Windows.System.VirtualKey.Left: case Windows.System.VirtualKey.A: x = -1; break; case Windows.System.VirtualKey.Right: case Windows.System.VirtualKey.D: x = 1; break; case Windows.System.VirtualKey.Up: case Windows.System.VirtualKey.W: y = -1; break; case Windows.System.VirtualKey.Down: case Windows.System.VirtualKey.S: y = 1; break; default: return; }
        args.Handled = true; Move(x, y);
    }
    void Move(int x, int y)
    {
        if (ended || paused) return;
        if (game is FallingBlocks or SandBlocks) { MoveBlock(x, y); return; }
        if (game == 0 && !turned && !(dx == -x && dy == -y)) { dx = x; dy = y; turned = true; }
        if (game == 2 && !ended) MoveTiles(x, y);
        if (game == 5) paddle = Math.Clamp(paddle + x * 25, 45, 435);
        if (game == 7) paddle = Math.Clamp(paddle + y * 25, 45, 435);
        if (game == MazeChase) arcade.WantedDirection = x > 0 ? 1 : y > 0 ? 2 : x < 0 ? 3 : 4;
        Draw();
        SaveCurrent();
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
        if (game >= 8) { var columns = game == 8 ? 5 : game == 9 ? 2 : 4; PuzzleClick((int)(py / (480.0 / columns)) * columns + (int)(px / (480.0 / columns))); return; }
        if (game is 5 or 7) { paddle = (int)Math.Clamp(game == 5 ? point.X : point.Y, 45, 435); SaveCurrent(); return; }
        if (game == 1) { var index = (int)(point.Y / 160) * 3 + (int)(point.X / 160); if (index is < 0 or > 8 || board[index] != 0) return; board[index] = 1; if (!CheckBoard(3, 3, 3)) { var best = -1; var bestValue = int.MinValue; for (var i = 0; i < 9; i++) if (board[i] == 0) { board[i] = 2; var value = Minimax(false); board[i] = 0; if (value > bestValue) { best = i; bestValue = value; } } if (best >= 0) board[best] = 2; CheckBoard(3, 3, 3); } }
        if (game == 4) { var col = (int)(point.X / (480.0 / 7)); if (col is < 0 or > 6 || !DropPiece(col, 1)) return; if (!CheckBoard(7, 6, 4)) { var choice = -1; foreach (var candidate in new[] { 3, 2, 4, 1, 5, 0, 6 }) { var spot = Landing(candidate); if (spot < 0) continue; board[spot] = 2; var win = Winner(7, 6, 4) == 2; board[spot] = 0; if (win) { choice = candidate; break; } } if (choice < 0) foreach (var candidate in new[] { 3, 2, 4, 1, 5, 0, 6 }) { var spot = Landing(candidate); if (spot < 0) continue; board[spot] = 1; var danger = Winner(7, 6, 4) == 1; board[spot] = 0; if (danger) { choice = candidate; break; } } if (choice < 0) choice = new[] { 3, 2, 4, 1, 5, 0, 6 }.FirstOrDefault(c => Landing(c) >= 0); DropPiece(choice, 2); CheckBoard(7, 6, 4); } }
        if (game == 3) { if (right) return; var index = (int)(point.Y / 60) * 8 + (int)(point.X / 60); if (index is < 0 or > 63 || flags[index]) return; if (moves++ == 0) { var mines = Enumerable.Range(0, 64).Where(i => Math.Abs(i % 8 - index % 8) > 1 || Math.Abs(i / 8 - index / 8) > 1).OrderBy(_ => random.Next()).Take(10); foreach (var mine in mines) board[mine] = -1; } if (board[index] == -1) { revealed[index] = true; End("Mine hit"); } else { Reveal(index); if (revealed.Count(v => v) == 54) { score = 100; End("Minefield cleared"); } } }
        if (game == 6) { var index = (int)(point.Y / 120) * 4 + (int)(point.X / 120); if (index is < 0 or > 15 || revealed[index] || second >= 0) return; revealed[index] = true; if (first < 0) first = index; else { if (board[first] == board[index]) { score += 10; first = -1; if (revealed.Take(16).All(v => v)) End("All pairs matched"); } else { second = index; hideAt = tick + 25; } } }
        Draw();
        SaveCurrent();
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
    void Label(string text, double x, double y, double size = 26, bool dark = false) { var label = new TextBlock { Text = text, FontSize = size, FontFamily = KronoDesign.CodeFont, Foreground = dark ? Ink(0x10231f) : White, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }; Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label); }
    void Draw()
    {
        canvas.Children.Clear(); ArcBackdrop(); UpdateScore(); status.Foreground = ReadableAccent(game); status.FontFamily = KronoDesign.CodeFont; status.Text = ended ? endMessage + " · Score " + score : (paused ? "Paused · " : "") + "Score " + score;
        DrawContent(); StageStatus();
    }
    void DrawContent()
    {
        if (game == 1) { DrawTicTacToe(); return; }
        if (game == 3) { DrawMines(); return; }
        if (game >= BlockFit) { DrawBoardGames(); return; }
        if (game >= MazeChase) { DrawArcade(); return; }
        if (game >= 12) { DrawAdvanced(); return; }
        if (game >= 8) { DrawPuzzles(); return; }
        if (game == 0) {
            for (var i=0;i<snake.Count;i++) { var cell=snake[i]; Bevel(cell%20*24+2,cell/20*24+2,20,20,new SolidColorBrush(Color.FromArgb((byte)Math.Max(110,255-i*7),111,231,184)),6); }
            var head=snake[0]; var hx=head%20*24; var hy=head/20*24; Rect(hx+7+dx*5,hy+7+dy*5,4,4,Ink(0x18322e),2); Rect(hx+14+dx*2,hy+14+dy*2,4,4,Ink(0x18322e),2);
            if (snake.Count < 400) { var x=food%20*24; var y=food/20*24; Rect(x+2,y+2,20,20,new SolidColorBrush(Color.FromArgb(50,255,111,120)),10); Bevel(x+6,y+6,12,12,Ink(0xff7a83),6); Rect(x+13,y+3,5,3,Mint,2); }
        }
        if (game is 2 or 4 or 6) {
            var cols = game == 4 ? 7 : 4; var rows = game == 4 ? 6 : cols; var width = 480.0 / cols; var height = 480.0 / rows;
            for (var i = 0; i < cols * rows; i++) { var x = i % cols * width; var y = i / cols * height; var value = board[i];
                string text = game == 2 ? value > 0 ? value.ToString() : "empty" : game == 4 ? value == 1 ? "●" : value == 2 ? "○" : "empty" : revealed[i] ? value.ToString() : "?";
                var surface = game == 2 && value > 0 ? BlockColors[((int)Math.Log2(value)-1)%7] : game == 6 && revealed[i] ? DigitColors[value-1] : Ink(game == 4 ? 0x263c59 : 0x26364e);
                object content = game switch {
                    2 => new TextBlock { Text = value == 0 ? "" : text, FontFamily = KronoDesign.CodeFont, FontSize = value > 9999 ? 23 : value > 999 ? 31 : 43, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Ink(0x182a37) },
                    4 => new Ellipse { Width = 47, Height = 47, Fill = value == 1 ? Ink(0xff8d88) : value == 2 ? Ink(0xffd87c) : Ink(0x17253b), Stroke = value == 0 ? Ink(0x3b5273) : BlockShine, StrokeThickness = 3 },
                    _ => revealed[i] ? new TextBlock { Text = MemorySymbols[value-1], FontSize = 48, Foreground = Ink(0x26354a) } : new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg")), Width = 44, Height = 44, Opacity = .8 }
                };
                var cell = GameTileButton(i, x + 4, y + 4, width - 8, height - 8, content, text, surface, game == 4 ? 17 : 12);
                var clickX = x + width / 2; var clickY = y + height / 2;
                cell.Click += (_, _) => PlayAt(clickX, clickY);
            }
        }
        if (game is 5 or 7) {
            for (var i = 3; i > 0; i--) Rect(ballX - ballDx*i*2 - 5, ballY - ballDy*i*2 - 5, 10, 10, new SolidColorBrush(Color.FromArgb((byte)(110-i*25),158,238,224)),5);
            Rect(ballX - 7, ballY - 7, 14, 14, White, 7);
            if (game == 5) { Bevel(paddle - 45, 450, 90, 14, Mint); foreach (var brick in bricks) Bevel(brick.X, brick.Y, 54, 18, BlockColors[(int)((brick.Y-30)/25)],5); }
            else { for(var y=8;y<480;y+=28) Rect(239,y,2,14,Ink(0x49627b),1); Bevel(20, paddle - 45, 12, 90, Accent,6); Bevel(448, enemy - 45, 12, 90, Ink(0xf6a8c3),6); }
        }
    }
    public void PauseForAgent() => PauseAndSave();
    public void Dispose() { PauseAndSave(); disposed = true; timer.Stop(); }
}
