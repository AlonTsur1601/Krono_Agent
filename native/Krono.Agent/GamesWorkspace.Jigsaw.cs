using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    const int EmptyPiece = 15;
    readonly FrameworkElement?[] puzzlePieces = new FrameworkElement?[16];
    readonly BitmapImage?[] puzzleArt = new BitmapImage?[10];
    int draggedPiece = -1;
    Point dragOrigin;

    void ResetJigsaw()
    {
        arcade.Picture = random.Next(puzzleArt.Length);
        arcade.SlidingPuzzle = true;
        for (var i = 0; i < 16; i++) board[i] = i;
        var empty = EmptyPiece;
        var previous = -1;
        do {
            for (var step = 0; step < 100; step++) {
                var options = Enumerable.Range(0, 16).Where(cell => cell != previous && AdjacentPieces(cell, empty)).ToArray();
                var next = options[random.Next(options.Length)];
                (board[empty], board[next]) = (board[next], board[empty]);
                previous = empty;
                empty = next;
            }
        } while (Enumerable.Range(0, 16).All(cell => board[cell] == cell));
    }

    void UpgradeJigsaw()
    {
        if (arcade.SlidingPuzzle) return;
        arcade.SlidingPuzzle = true;
        arcade.Picture = 0;
        var pieces = board.Take(16).Where(piece => piece != EmptyPiece).ToArray();
        var emptyFromBottom = 4 - Array.IndexOf(board, EmptyPiece, 0, 16) / 4;
        var inversions = pieces.SelectMany((piece, i) => pieces.Skip(i + 1).Where(other => other < piece)).Count();
        if ((inversions + emptyFromBottom) % 2 == 0) {
            var a = Array.IndexOf(board, pieces[0], 0, 16);
            var b = Array.IndexOf(board, pieces[1], 0, 16);
            (board[a], board[b]) = (board[b], board[a]);
        }
    }

    static bool AdjacentPieces(int a, int b) => Math.Abs(a % 4 - b % 4) + Math.Abs(a / 4 - b / 4) == 1;
    static int PuzzleCell(Point position) => position.X is >= 0 and < 480 && position.Y is >= 0 and < 480
        ? (int)(position.Y / 120) * 4 + (int)(position.X / 120) : -1;

    void DrawJigsaw()
    {
        Array.Clear(puzzlePieces);
        BitmapImage source = puzzleArt[arcade.Picture] ??= new BitmapImage(new Uri($"ms-appx:///Assets/Jigsaw/landscape-{arcade.Picture:00}.png"));
        Image Picture() => new() { Source = source, Width = 480, Height = 480, Stretch = Stretch.Fill, IsHitTestVisible = false };
        if (jigsawPreview.IsChecked == true) { canvas.Children.Add(Picture()); return; }
        for (var i = 0; i < 16; i++) {
            var piece = board[i];
            var view = new Canvas { Width = 116, Height = 116, Clip = new RectangleGeometry { Rect = new Rect(0, 0, 116, 116) } };
            if (piece != EmptyPiece) {
                var image = Picture();
                Canvas.SetLeft(image, -(piece % 4) * 120 - 2);
                Canvas.SetTop(image, -(piece / 4) * 120 - 2);
                view.Children.Add(image);
            }
            var tile = PassiveTile(view, i % 4 * 120 + 2, i / 4 * 120 + 2, 116, 116, Ink(0x15242e));
            tile.CornerRadius = new CornerRadius(4);
            AutomationProperties.SetAutomationId(tile, "GameCell" + i);
            AutomationProperties.SetName(tile, piece == EmptyPiece ? "Empty square" : $"Tile {i + 1} · Piece {piece + 1}");
            puzzlePieces[i] = tile;
        }
    }

    void StartJigsawDrag(object sender, PointerRoutedEventArgs args)
    {
        if (game != Jigsaw || paused || ended || jigsawPreview.IsChecked == true || draggedPiece >= 0) return;
        var pointer = args.GetCurrentPoint(canvas);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !pointer.Properties.IsLeftButtonPressed) return;
        var index = PuzzleCell(pointer.Position);
        var empty = Array.IndexOf(board, EmptyPiece, 0, 16);
        if (index < 0 || !AdjacentPieces(index, empty) || !canvas.CapturePointer(args.Pointer)) return;
        draggedPiece = index;
        dragOrigin = pointer.Position;
        if (puzzlePieces[index] is { } tile) Canvas.SetZIndex(tile, 2);
        args.Handled = true;
    }

    void MoveJigsawDrag(object sender, PointerRoutedEventArgs args)
    {
        if (draggedPiece < 0 || puzzlePieces[draggedPiece] is not { } tile) return;
        var position = args.GetCurrentPoint(canvas).Position;
        var empty = Array.IndexOf(board, EmptyPiece, 0, 16);
        var dx = empty % 4 - draggedPiece % 4;
        var dy = empty / 4 - draggedPiece / 4;
        tile.RenderTransform = new TranslateTransform {
            X = dx * Math.Clamp((position.X - dragOrigin.X) * dx, 0, 120),
            Y = dy * Math.Clamp((position.Y - dragOrigin.Y) * dy, 0, 120)
        };
    }

    void CompleteJigsawDrag(object sender, PointerRoutedEventArgs args)
    {
        if (draggedPiece < 0) return;
        var from = draggedPiece;
        var target = PuzzleCell(args.GetCurrentPoint(canvas).Position);
        CancelJigsawDrag();
        canvas.ReleasePointerCapture(args.Pointer);
        if (target >= 0 && board[target] == EmptyPiece) MoveJigsawPiece(from, target);
        args.Handled = true;
    }

    void CancelJigsawDrag()
    {
        if (draggedPiece >= 0 && puzzlePieces[draggedPiece] is { } element) {
            element.RenderTransform = null;
            Canvas.SetZIndex(element, 0);
        }
        draggedPiece = -1;
    }

    void MoveJigsawPiece(int source, int target)
    {
        if (paused || ended || jigsawPreview.IsChecked == true || source is < 0 or >= 16 || !AdjacentPieces(source, target)) return;
        (board[source], board[target]) = (board[target], board[source]);
        moves++;
        if (Enumerable.Range(0, 16).All(cell => board[cell] == cell)) { score = Math.Max(1, 1000 - moves * 15); End("Picture complete", won: true); }
        Draw();
        SaveCurrent();
    }

    bool JigsawKey(Windows.System.VirtualKey key)
    {
        var target = Array.IndexOf(board, EmptyPiece, 0, 16);
        var source = key switch {
            Windows.System.VirtualKey.Left => target + 1,
            Windows.System.VirtualKey.Right => target - 1,
            Windows.System.VirtualKey.Up => target + 4,
            Windows.System.VirtualKey.Down => target - 4,
            _ => -1
        };
        if (source < 0) return false;
        MoveJigsawPiece(source, target);
        return true;
    }
}
