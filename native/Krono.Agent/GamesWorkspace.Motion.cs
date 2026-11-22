using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    readonly Dictionary<int, int> tileSources = [];
    readonly List<Button> memoryTiles = [];
    readonly Queue<(double X, double Y)> ballTrail = new();
    int[] previousSnake = [];
    int botAt;
    Canvas? movingStack;
    int botDifficulty = 1;
    readonly DispatcherTimer scoreDeltaTimer = new() { Interval = TimeSpan.FromMilliseconds(780) };
    int SnakeSlide => Math.Max(3, 7 - score / 60) * 30 - 5;
    static readonly UISettings GameMotionSettings = new();
    static readonly string[] BotLevels = ["Easy", "Normal", "Hard"];
    sealed class GameDisplayTile : Grid
    {
        protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
            new Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer(this);
    }

    readonly Border waitingPrompt = new() {
        CornerRadius = new CornerRadius(16), Background = Ink(0x172b36), Padding = new Thickness(18, 12, 18, 14),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        Child = new TextBlock { FontSize = 20, Foreground = White, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
    };

    void ResetGameMotion()
    {
        effects.Children.Clear();
        var effectVisual = ElementCompositionPreview.GetElementVisual(effects);
        effectVisual.StopAnimation("Opacity"); effectVisual.Opacity = 1;
        tileSources.Clear();
        previousSnake = [];
        memoryTiles.Clear();
        ballTrail.Clear();
        scoreDeltaTimer.Stop();
        pointsGain.Text = "";
        shownScore = -1;
        botAt = 0;
        movingStack = null;
    }

    static void SlidePiece(FrameworkElement element, double fromX, double fromY, double milliseconds)
    {
        if ((fromX == 0 && fromY == 0) || !GameMotionSettings.AnimationsEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        animation.InsertKeyFrame(0, new((float)fromX, (float)fromY, 0));
        animation.InsertKeyFrame(1, Vector3.Zero, visual.Compositor.CreateLinearEasingFunction());
        visual.StartAnimation("Translation", animation);
    }

    Grid PassiveTile(object content, double x, double y, double width, double height, Brush brush)
    {
        var tile = new GameDisplayTile { Width = width, Height = height, Background = RaisedFace(brush), CornerRadius = new CornerRadius(12) };
        tile.Children.Add(new ContentPresenter { Content = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Canvas.SetLeft(tile, x); Canvas.SetTop(tile, y); canvas.Children.Add(tile);
        return tile;
    }

    void BuildMemory()
    {
        memoryTiles.Clear();
        var cardBack = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg"));
        for (var i = 0; i < 16; i++) {
            var index = i;
            var art = new Grid { Width = 76, Height = 76, IsHitTestVisible = false };
            art.Children.Add(new Image { Source = cardBack, Width = 44, Height = 44, Opacity = .8 });
            art.Children.Add(new TextBlock { FontSize = 47, Foreground = Ink(0x26354a), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var tile = GameTileButton(i, i % 4 * 120 + 4, i / 4 * 120 + 4, 112, 112, art, "", Ink(0x26364e), 12);
            tile.Click += (_, _) => PlayAt(index % 4 * 120 + 60, index / 4 * 120 + 60);
            memoryTiles.Add(tile);
        }
        RefreshMemory();
    }

    void RefreshMemory()
    {
        for (var i = 0; i < memoryTiles.Count; i++) {
            var tile = memoryTiles[i];
            var art = (Grid)tile.Content;
            var symbol = (TextBlock)art.Children[1];
            var visible = revealed[i];
            tile.IsEnabled = !visible;
            symbol.Text = MemorySymbols[board[i] - 1];
            symbol.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            art.Children[0].Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            tile.Background = RaisedFace(visible ? DigitColors[board[i] - 1] : Ink(0x26364e));
            AutomationProperties.SetName(tile, $"Cell {i + 1}: {(visible ? board[i].ToString() : "?")}");
        }
    }

    bool PlayerBoardMove(int px, int py)
    {
        if (botAt != 0) return false;
        if (game == 1) {
            var index = py / 160 * 3 + px / 160;
            if (index is < 0 or > 8 || board[index] != 0) return false;
            board[index] = 1;
            if (!CheckBoard(3, 3, 3)) QueueBot();
            return true;
        }
        var col = (int)(px / (480.0 / 7));
        if (col is < 0 or > 6 || !DropPiece(col, 1)) return false;
        if (!CheckBoard(7, 6, 4)) QueueBot();
        return true;
    }

    void QueueBot() => botAt = tick + random.Next(10, 21);
    bool BotMakesMistake => random.NextDouble() < (botDifficulty switch { 0 => .8, 2 => .17, _ => .40 });

    void UpdateBot()
    {
        if (botAt == 0 || tick < botAt) return;
        botAt = 0;
        if (game == 1) {
            var legal = Enumerable.Range(0, 9).Where(i => board[i] == 0).ToArray();
            if (legal.Length > 0) {
                var choice = BotMakesMistake ? legal[random.Next(legal.Length)] : legal.MaxBy(i => {
                    board[i] = 2; var value = Minimax(false); board[i] = 0; return value;
                });
                board[choice] = 2;
                CheckBoard(3, 3, 3);
            }
        } else if (game == 4) {
            var legal = new[] { 3, 2, 4, 1, 5, 0, 6 }.Where(col => Landing(col) >= 0).ToArray();
            if (legal.Length > 0) {
                var choice = BotMakesMistake ? legal[random.Next(legal.Length)] : legal.MaxBy(col => {
                    var spot = Landing(col);
                    board[spot] = 2; var win = Winner(7, 6, 4) == 2;
                    board[spot] = 1; var block = Winner(7, 6, 4) == 1;
                    board[spot] = 0; return win ? 2 : block ? 1 : 0;
                });
                DropPiece(choice, 2);
                CheckBoard(7, 6, 4);
            }
        } else if (game == 12) {
            var legal = NativeGameRules.ReversiMoves(board, 2);
            if (legal.Length > 0) NativeGameRules.ReversiPlace(board, BotMakesMistake ? legal[random.Next(legal.Length)] : NativeGameRules.ReversiBot(board), 2);
            var yours = board.Count(value => value == 1); var theirs = board.Count(value => value == 2); score = yours;
            if (NativeGameRules.ReversiMoves(board, 1).Length == 0) {
                if (NativeGameRules.ReversiMoves(board, 2).Length == 0) End(yours == theirs ? "Draw" : yours > theirs ? "You won" : "Bot won", won: yours > theirs);
                else QueueBot();
            } else gameMessage = "Your turn";
        }
        Draw();
        SaveCurrent(false);
    }
}
