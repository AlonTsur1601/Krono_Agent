using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    const int BlockFit = 20, FallingBlocks = 21, SandBlocks = 22, Solitaire = 23;
    readonly StackPanel fitTray = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Button hardDrop = new() { Content = "Hard drop", HorizontalAlignment = HorizontalAlignment.Center };
    BlockRound blocks = new();
    KlondikeRound cards = new();
    static readonly int[] linePoints = [0,100,300,500,800];
    const int LegacySandWidth = 20;
    const int SandWidth = 40;
    int FallingWidth => game == SandBlocks ? SandWidth : 10;
    int FallingHeight => FallingWidth * 2;
    int BlockScale => FallingWidth / 10;
    static readonly string[] suits = ["♠", "♥", "♣", "♦"];
    static readonly string[] suitNames = ["spades", "hearts", "clubs", "diamonds"];
    void BuildBoardControls(StackPanel footer)
    {
        hardDrop.Click += (_, _) => DropNow(); footer.Children.Add(fitTray); movementControls.Children.Add(hardDrop);
        AutomationProperties.SetAutomationId(hardDrop, "HardDrop");
    }
    void ResetBoardGames()
    {
        blocks = new BlockRound(); cards = new KlondikeRound();
        fitTray.Visibility = game == BlockFit ? Visibility.Visible : Visibility.Collapsed;
        hardDrop.Visibility = game is FallingBlocks or SandBlocks ? Visibility.Visible : Visibility.Collapsed;
        if (game == BlockFit) FillTray();
        if (game is FallingBlocks or SandBlocks) { blocks.Grid = new int[FallingWidth * FallingHeight]; SpawnBlock(); }
        if (game == Solitaire) cards = KlondikeRound.Deal(random);
    }
    void FillTray() { blocks.Tray = Enumerable.Range(0, 3).Select(_ => random.Next(BlockRules.FitCount)).ToArray(); blocks.Selected = 0; }
    void PickFit(int slot) { if (paused || ended || slot is < 0 or >= 3 || blocks.Tray[slot] < 0) return; blocks.Selected = slot; gameMessage = ""; Draw(); SaveCurrent(); }
    void PlaceFit(int index)
    {
        if (paused || ended) return;
        var id = blocks.Tray[blocks.Selected]; if (id < 0) return;
        var shape = BlockRules.Fit(id); var x = index % 8; var y = index / 8;
        if (!BlockRules.CanPlace(board, 8, 8, shape, x, y)) { gameMessage = "That piece does not fit here"; Draw(); return; }
        BlockRules.Place(board, 8, shape, x, y, id % 7 + 1); blocks.Tray[blocks.Selected] = -1; moves++;
        var glow = Enumerable.Range(0, 8).Where(row => Enumerable.Range(0, 8).All(col => board[row*8+col] > 0)).Select(row => (0.0, row*60.0, 480.0, 60.0))
            .Concat(Enumerable.Range(0, 8).Where(col => Enumerable.Range(0, 8).All(row => board[row*8+col] > 0)).Select(col => (col*60.0, 0.0, 60.0, 480.0))).ToArray();
        var lines = BlockRules.ClearFit(board); score += shape.Length + lines * 100;
        if (lines > 0) ClearGlow(glow);
        gameMessage = lines > 0 ? $"Cleared {lines} {(lines == 1 ? "line" : "lines")}" : "";
        if (blocks.Tray.All(piece => piece < 0)) FillTray(); else blocks.Selected = Array.FindIndex(blocks.Tray, piece => piece >= 0);
        if (!BlockRules.HasFit(board, blocks.Tray)) End("No pieces fit");
        Draw(); SaveCurrent();
    }
    int BagPiece()
    {
        if (blocks.Bag.Count == 0) { var pieces = Enumerable.Range(0, 7).ToArray(); random.Shuffle(pieces.AsSpan()); blocks.Bag.AddRange(pieces); }
        var piece = blocks.Bag[^1]; blocks.Bag.RemoveAt(blocks.Bag.Count - 1); return piece;
    }
    void SpawnBlock()
    {
        blocks.Active = blocks.Next < 0 ? BagPiece() : blocks.Next; blocks.Next = BagPiece();
        blocks.X = 3 * BlockScale; blocks.Y = blocks.Rotation = 0; blocks.Settling = false;
        if (!FitsBlock(blocks.X, blocks.Y, 0)) End("Stack reached the top");
    }
    bool FitsBlock(int x, int y, int rotation) => BlockRules.CanPlace(blocks.Grid, FallingWidth, FallingHeight, BlockRules.Falling(blocks.Active, rotation), x, y, BlockScale);
    bool StepBlock(bool soft = false)
    {
        if (blocks.Settling || ended || paused) return false;
        if (FitsBlock(blocks.X, blocks.Y + 1, blocks.Rotation)) { blocks.Y++; if (soft && (game != SandBlocks || blocks.Y % 2 == 0)) score++; return true; }
        LockBlock(); return false;
    }
    void LockBlock()
    {
        var piece = BlockRules.Falling(blocks.Active, blocks.Rotation);
        BlockRules.Place(blocks.Grid, FallingWidth, piece, blocks.X, blocks.Y, blocks.Active % (game == SandBlocks ? 4 : 7) + 1, BlockScale);
        moves++;
        if (game == SandBlocks) { blocks.Settling = true; return; }
        var filled = Enumerable.Range(0, FallingHeight).Where(row => Enumerable.Range(0, FallingWidth).All(col => blocks.Grid[row*FallingWidth+col] > 0)).Select(row => (120.0,row*24.0,240.0,24.0)).ToArray();
        var lines = BlockRules.ClearRows(blocks.Grid, FallingWidth, FallingHeight); score += linePoints[Math.Min(4, lines)] * (1 + blocks.Lines / 10); blocks.Lines += lines;
        if (lines > 0) ClearGlow(filled);
        SpawnBlock();
    }
    void MoveBlock(int x, int y)
    {
        if (blocks.Settling || paused || ended) return;
        if (x != 0 && FitsBlock(blocks.X + x * BlockScale, blocks.Y, blocks.Rotation)) blocks.X += x * BlockScale;
        if (y > 0) StepBlock(true);
        if (y < 0) {
            var rotation = (blocks.Rotation + 1) % 4;
            foreach (var (dx,dy) in new[] { (0,0),(-1,0),(1,0),(-2,0),(2,0),(0,-1),(-1,-1),(1,-1) }) {
                var cx = blocks.X + dx * BlockScale; var cy = blocks.Y + dy * BlockScale;
                if (!FitsBlock(cx, cy, rotation)) continue;
                blocks.X = cx; blocks.Y = cy; blocks.Rotation = rotation; break;
            }
        }
        Draw(); SaveCurrent();
    }
    void DropNow()
    {
        if (game is not (FallingBlocks or SandBlocks) || paused || ended || blocks.Settling) return;
        while (FitsBlock(blocks.X, blocks.Y + 1, blocks.Rotation)) { blocks.Y++; score += game == SandBlocks ? 1 : 2; }
        LockBlock(); Draw(); SaveCurrent();
    }
    void UpdateBoardGames()
    {
        if (game is not (FallingBlocks or SandBlocks)) return;
        if (blocks.Settling) {
            if (!BlockRules.SettleSand(blocks.Grid, FallingWidth, FallingHeight, true) &&
                !BlockRules.SettleSand(blocks.Grid, FallingWidth, FallingHeight, false)) {
                var cleared = BlockRules.ClearSandSpans(blocks.Grid, FallingWidth, FallingHeight);
                if (cleared == 0) SpawnBlock(); else { score += cleared * 5 / 4 + 100; blocks.Lines++; ClearGlow([(120, 448, 240, 32)]); }
            } else {
                BlockRules.SettleSand(blocks.Grid, FallingWidth, FallingHeight, tick % 2 == 0);
            }
            Draw(); return;
        }
        if (tick % Math.Max(3, (game == SandBlocks ? 5 : 12) - blocks.Lines / 3) == 0) { StepBlock(); Draw(); }
    }
    void SolitaireClick(int kind, int pile = 0, int index = 0)
    {
        if (ended || paused) return;
        var moved = false;
        if (kind == -1) moved = cards.DrawStock();
        if (kind == KlondikeRound.WasteKind) cards.Select(kind);
        if (kind == KlondikeRound.FoundationKind) { moved = cards.ToFoundation(pile); if (!moved) cards.Select(kind, pile); }
        if (kind == KlondikeRound.TableauKind) {
            var column = cards.Tableau[pile];
            if (index >= column.Count - 1) moved = cards.ToTableau(pile);
            if (!moved) cards.Select(kind, pile, index);
        }
        if (moved) moves++;
        score = cards.Foundations.Sum() * 10;
        if (score == 520) End("Solitaire complete");
        Draw(); SaveCurrent();
    }
    void DrawBoardGames()
    {
        if (game == BlockFit) { DrawFit(); return; }
        if (game == Solitaire) { DrawSolitaire(); return; }
        if (!paused && !ended) status.Text = $"Score {score} · {(game == SandBlocks ? "Spans" : "Lines")} {blocks.Lines}{(blocks.Settling ? " · Sand settling" : "")}";
        var unit = 480.0 / FallingHeight; const int left = 120;
        Rect(left - 4, 0, 248, 480, Ink(0x435a78), 8); Rect(left, 0, 240, 480, Ink(0x18253b), 6);
        for (var x = 1; x < 10; x++) Rect(left+x*24, 0, 1, 480, Ink(0x263650), 0);
        for (var y = 1; y < 20; y++) Rect(left, y*24, 240, 1, Ink(0x263650), 0);
        if (game == SandBlocks) DrawSand(left, (int)unit);
        else for (var i = 0; i < blocks.Grid.Length; i++) if (blocks.Grid[i] > 0) {
            var x = left + i % FallingWidth * unit; var y = i / FallingWidth * unit; var color = PieceBrush(blocks.Grid[i]);
            Bevel(x+1, y+1, unit-2, unit-2, color, 4);
        }
        if (!ended && !blocks.Settling) {
            var piece = BlockRules.Falling(blocks.Active, blocks.Rotation); var ghost = blocks.Y;
            while (FitsBlock(blocks.X, ghost + 1, blocks.Rotation)) ghost++;
            foreach (var cell in piece) {
                var x = left + (blocks.X + cell.X * BlockScale) * unit; var size = unit * BlockScale;
                Rect(x + 1, (ghost + cell.Y * BlockScale) * unit + 1, size - 2, size - 2, Ink(0x425370), 5);
                Bevel(x + 1, (blocks.Y + cell.Y * BlockScale) * unit + 1, size - 2, size - 2, PieceBrush(blocks.Active % (game == SandBlocks ? 4 : 7) + 1), 5);
            }
        }
        Rect(374, 10, 100, 97, Ink(0x213350), 12); Label("NEXT", 389, 22, 16);
        foreach (var cell in BlockRules.Falling(blocks.Next, 0)) Bevel(386 + cell.X * 18, 53 + cell.Y * 18, 16, 16, PieceBrush(blocks.Next % (game == SandBlocks ? 4 : 7) + 1), 4);
    }
    void DrawSand(int left, int unit)
    {
        var clusters = new GeometryGroup?[7, 3];
        for (var i = 0; i < blocks.Grid.Length; i++) {
            var value = blocks.Grid[i];
            if (value == 0) continue;
            var tone = (i * 13 ^ i / 7) % 3;
            var x = left + i % FallingWidth * unit;
            var y = i / FallingWidth * unit;
            var group = clusters[value - 1, tone] ??= new GeometryGroup();
            group.Children.Add(new RectangleGeometry { Rect = new(x + .2, y + .2, unit - .4, unit - .4) });
        }
        for (var i = 0; i < 7; i++) for (var tone = 0; tone < 3; tone++) {
            if (clusters[i, tone] is not { } group) continue;
            var color = BlockColors[i].Color;
            var shade = 9 * (tone - 1);
            byte Tint(byte channel) => (byte)Math.Clamp(channel + shade, 0, 255);
            canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = group, Fill = new SolidColorBrush(Color.FromArgb(255, Tint(color.R), Tint(color.G), Tint(color.B))), IsHitTestVisible = false });
        }
    }
    static void UpgradeSandGrid(BlockRound blocks)
    {
        var old = blocks.Grid;
        if (old?.Length != LegacySandWidth * LegacySandWidth * 2 ||
            blocks.X is < 0 or >= LegacySandWidth || blocks.Y is < 0 or >= LegacySandWidth * 2) return;
        blocks.Grid = new int[SandWidth * SandWidth * 2];
        for (var i = 0; i < old.Length; i++) {
            var top = i / LegacySandWidth * 2 * SandWidth + i % LegacySandWidth * 2;
            blocks.Grid[top] = blocks.Grid[top + 1] = blocks.Grid[top + SandWidth] = blocks.Grid[top + SandWidth + 1] = old[i];
        }
        blocks.X *= 2; blocks.Y *= 2;
    }
    static SolidColorBrush PieceBrush(int color) => BlockColors[Math.Clamp(color - 1, 0, 6)];
    void DrawFit()
    {
        if (!paused && !ended) status.Text = $"Score {score} · Piece {blocks.Selected + 1}{(gameMessage.Length > 0 ? " · " + gameMessage : "")}";
        for (var i = 0; i < 64; i++) {
            var index = i; var button = new Button { Width = 56, Height = 56, Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Background = board[i] == 0 ? Ink(0x23364f) : PieceBrush(board[i]), BorderBrush = board[i] == 0 ? Ink(0x344b67) : BlockShine, BorderThickness = new Thickness(board[i] == 0 ? 1 : 3) };
            AutomationProperties.SetAutomationId(button, "GameCell" + i); AutomationProperties.SetName(button, $"Square {i + 1}: {(board[i] > 0 ? "filled" : "empty")}"); button.Click += (_, _) => PlaceFit(index);
            Canvas.SetLeft(button, i % 8 * 60 + 2); Canvas.SetTop(button, i / 8 * 60 + 2); canvas.Children.Add(button);
        }
        fitTray.Children.Clear();
        for (var i = 0; i < 3; i++) {
            var slot = i; var piece = blocks.Tray[i]; var preview = new Canvas { Width = 70, Height = 58 };
            if (piece >= 0) foreach (var cell in BlockRules.Fit(piece)) { var square = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 12, Height = 12, RadiusX = 3, RadiusY = 3, Fill = PieceBrush(piece % 7 + 1) }; Canvas.SetLeft(square, 6 + cell.X * 14); Canvas.SetTop(square, 2 + cell.Y * 14); preview.Children.Add(square); }
            var button = new Button { Content = preview, IsEnabled = !paused && !ended && piece >= 0, BorderThickness = new Thickness(2), BorderBrush = slot == blocks.Selected ? ReadableAccent : Muted };
            AutomationProperties.SetAutomationId(button, "FitPiece" + i); AutomationProperties.SetName(button, $"Piece {i + 1}{(i == blocks.Selected ? " selected" : "")}{(piece < 0 ? " used" : "")}"); button.Click += (_, _) => PickFit(slot); fitTray.Children.Add(button);
        }
    }
    void DrawSolitaire()
    {
        if (!paused && !ended) status.Text = $"Foundations {cards.Foundations.Sum()}/52 · Stock {cards.Stock.Count} · Moves {moves}";
        Put(7, 12, "SolStock", cards.Stock.Count > 0 ? $"Stock, {cards.Stock.Count} cards" : "Recycle waste", cards.Stock.Count > 0 ? "▦" : "↻", null, false, false, () => SolitaireClick(-1));
        var waste = cards.Waste.Count > 0 ? cards.Waste[^1] : -1;
        Put(73, 12, "SolWaste", waste < 0 ? "Empty waste" : "Waste: " + CardName(waste), waste < 0 ? "–" : Face(waste), waste < 0 ? null : waste, cards.SelectedKind == KlondikeRound.WasteKind, false, () => SolitaireClick(KlondikeRound.WasteKind));
        for (var i = 0; i < 4; i++) {
            var suit = i; var rank = cards.Foundations[i]; var card = i * 13 + rank - 1;
            Put(210 + i * 66, 12, "SolFoundation" + i, $"{suitNames[i]} foundation: {(rank == 0 ? "empty" : CardName(card))}", rank == 0 ? suits[i] : Face(card), rank == 0 ? null : card, cards.SelectedKind == KlondikeRound.FoundationKind && cards.SelectedPile == i, false, () => SolitaireClick(KlondikeRound.FoundationKind, suit));
        }
        var spacing = Math.Min(25, 280.0 / Math.Max(1, cards.Tableau.Max(column => column.Count - 1)));
        for (var i = 0; i < 7; i++) {
            var column = i; var pile = cards.Tableau[i];
            if (pile.Count == 0) Put(7 + i * 67, 118, $"SolTableau{i}_0", $"Empty column {i + 1}", "–", null, false, false, () => SolitaireClick(KlondikeRound.TableauKind, column, 0));
            for (var j = 0; j < pile.Count; j++) {
                var row = j; var card = pile[j]; var hidden = j < cards.FaceDown[i];
                var selected = !hidden && cards.SelectedKind == KlondikeRound.TableauKind && cards.SelectedPile == i && j >= cards.SelectedIndex;
                Put(7 + i * 67, 118 + j * spacing, $"SolTableau{i}_{j}", hidden ? $"Face down in column {i + 1}" : $"Column {i + 1}: {CardName(card)}", hidden ? "·" : Face(card), hidden ? null : card, selected, hidden, () => SolitaireClick(KlondikeRound.TableauKind, column, row));
            }
        }
        void Put(double x, double y, string id, string name, string text, int? card, bool selected, bool hidden, Action click) {
            object face = text;
            if (hidden) face = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg")), Width = 26, Height = 26, Opacity = .72 };
            else if (card is { } faceCard) {
                var art = new Canvas { Width = 48, Height = 68, IsHitTestVisible = false };
                var paint = KlondikeRound.Red(faceCard) ? Ink(0xa63350) : Ink(0x28364c);
                var corner = new TextBlock { Text = Face(faceCard), FontFamily = KronoDesign.CodeFont, FontSize = 15, Foreground = paint, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
                Canvas.SetLeft(corner, 0); Canvas.SetTop(corner, 0); art.Children.Add(corner);
                var emblem = new TextBlock { Text = suits[KlondikeRound.Suit(faceCard)], FontSize = 35, Foreground = paint, Opacity = .75 };
                Canvas.SetLeft(emblem, 9); Canvas.SetTop(emblem, 23); art.Children.Add(emblem); face = art;
            }
            var button = new Button { Width = 58, Height = 76, CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 2, 2, 0), HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
                Content = face, FontFamily = KronoDesign.CodeFont, FontSize = 17, Tag = "game-code", Background = card is not null ? Ink(0xf6eada) : Ink(0x294b56),
                Foreground = card is { } value ? new SolidColorBrush(KlondikeRound.Red(value) ? Color.FromArgb(255, 153, 39, 67) : Color.FromArgb(255, 21, 34, 49)) : Accent,
                IsEnabled = !hidden, BorderThickness = new Thickness(selected ? 3 : 1), BorderBrush = selected ? Accent : Muted };
            AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, name); button.Click += (_, _) => click();
            Canvas.SetLeft(button, x); Canvas.SetTop(button, y); canvas.Children.Add(button);
        }
        static string Rank(int card) => KlondikeRound.Rank(card) switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", var rank => rank.ToString() };
        static string Face(int card) => Rank(card) + suits[KlondikeRound.Suit(card)];
        static string CardName(int card) => Rank(card) + " of " + suitNames[KlondikeRound.Suit(card)];
    }
    public sealed class BlockRound
    {
        public int[] Grid { get; set; } = [];
        public int[] Tray { get; set; } = [];
        public int Selected { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Rotation { get; set; }
        public int Active { get; set; }
        public int Next { get; set; } = -1;
        public int Lines { get; set; }
        public bool Settling { get; set; }
        public List<int> Bag { get; set; } = [];
    }
    bool ValidBoardGame(GameSave s)
    {
        if (game == Solitaire) return s.Cards?.IsValid() == true;
        var b = s.Blocks; if (b is null) return false;
        if (game == BlockFit) return b.Tray?.Length == 3 && b.Tray.All(id => id >= -1 && id < BlockRules.FitCount) && b.Selected is >= 0 and < 3 && b.Tray[b.Selected] >= 0 && s.Board.All(n => n is >= 0 and <= 7);
        return b.Grid?.Length == FallingWidth * FallingHeight && b.Grid.All(n => n is >= 0 and <= 7) && b.X >= 0 && b.X < FallingWidth && b.Y >= 0 && b.Y < FallingHeight && b.Rotation is >= 0 and < 4 && b.Active is >= 0 and < 7 && b.Next is >= 0 and < 7 && b.Lines >= 0 && b.Bag is { Count: <= 7 } && b.Bag.All(n => n is >= 0 and < 7) && b.Bag.Distinct().Count() == b.Bag.Count && (s.Ended || b.Settling && game == SandBlocks || BlockRules.CanPlace(b.Grid, FallingWidth, FallingHeight, BlockRules.Falling(b.Active, b.Rotation), b.X, b.Y, BlockScale));
    }
}
