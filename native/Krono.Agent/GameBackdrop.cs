using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace Krono_Agent;

public sealed class GameBackdrop : Grid
{
    const int TileWidth = 320, TileHeight = 224;
    readonly Canvas tiles = new();


    static Geometry MakeTile()
    {
        var drawing = new GeometryGroup();
        void Line(params Point[] points)
        {
            for (var i = 1; i < points.Length; i++)
                drawing.Children.Add(new LineGeometry { StartPoint = points[i - 1], EndPoint = points[i] });
        }
        void Ring(double x, double y, double radius) =>
            drawing.Children.Add(new EllipseGeometry { Center = new Point(x, y), RadiusX = radius, RadiusY = radius });

        // Snake, with a head and food pellet.
        Line(new(22,32), new(34,32), new(34,43), new(47,43));
        drawing.Children.Add(new RectangleGeometry { Rect = new Rect(46, 38, 10, 10) });
        Ring(53,41,1); Ring(62,43,2);

        // Tetris and Maze Chase.
        Line(new(119,16), new(127,16), new(127,24), new(135,24), new(135,32), new(111,32), new(111,24), new(119,24), new(119,16));
        Line(new(119,24), new(119,32)); Line(new(127,24), new(127,32));
        Point[] mouth = Enumerable.Range(0, 17).Select(i => {
            var angle = (.2 + 1.6 * i / 16) * Math.PI;
            return new Point(224 + 11 * Math.Cos(angle), 47 + 11 * Math.Sin(angle));
        }).ToArray();
        Line([new(224,47), ..mouth, new(224,47)]);
        Ring(241,47,1.5); Ring(247,47,1.5);

        // Tic-tac-toe and Pong.
        Line(new(284,12), new(284,39)); Line(new(293,12), new(293,39));
        Line(new(275,21), new(302,21)); Line(new(275,30), new(302,30));
        Line(new(277,14), new(282,19)); Line(new(282,14), new(277,19)); Ring(289,25.5,2.5);
        Line(new(52,98), new(52,112)); Line(new(74,98), new(74,112)); Ring(63,103,1.5);

        // Darts and Minesweeper.
        Ring(159,74,10); Ring(159,74,4);
        Line(new(159,74), new(172,61), new(179,60), new(178,66), new(172,61));
        Line(new(263,117), new(263,100), new(275,105), new(263,110)); Line(new(258,117), new(269,117));
        Ring(34,171,7);
        Line(new(23,171), new(27,171)); Line(new(41,171), new(45,171));
        Line(new(34,160), new(34,164)); Line(new(34,178), new(34,182));
        Line(new(26,163), new(29,166)); Line(new(39,176), new(42,179));

        // Solitaire, Sand Blocks and a stack in motion.
        drawing.Children.Add(new RectangleGeometry { Rect = new Rect(99,183,19,26) });
        Line(new(102,196), new(108.5,189), new(115,196), new(108.5,203), new(102,196));
        Line(new(189,151), new(198,151), new(198,160), new(207,160), new(207,169), new(189,169), new(189,151));
        Line(new(189,160), new(198,160), new(198,169));
        Ring(195,175,1); Ring(202,177,1); Ring(190,181,1); Ring(198,183,1);
        drawing.Children.Add(new RectangleGeometry { Rect = new Rect(272,181,27,5) });
        drawing.Children.Add(new RectangleGeometry { Rect = new Rect(276,176,20,5) });
        drawing.Children.Add(new RectangleGeometry { Rect = new Rect(287,166,17,5) });
        return drawing;
    }

    int columns, rows;

    public GameBackdrop()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Opacity = .13;
        Children.Add(tiles);
        SizeChanged += (_, e) => ArrangePattern(e.NewSize);
        ActualThemeChanged += (_, _) => UpdateInk();
        Loaded += (_, _) => UpdateInk();
    }

    void ArrangePattern(Size size)
    {
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, size.Width, size.Height) };
        var wide = (int)Math.Ceiling(size.Width / TileWidth);
        var tall = (int)Math.Ceiling(size.Height / TileHeight);
        if (columns == wide && rows == tall) return;
        columns = wide; rows = tall;
        tiles.Children.Clear();
        for (var row = 0; row < rows; row++)
            for (var column = 0; column <= columns; column++) {
                var mark = new Path { Data = MakeTile(), StrokeThickness = 1.4 };
                Canvas.SetLeft(mark, column * TileWidth - (row % 2) * 120);
                Canvas.SetTop(mark, row * TileHeight);
                tiles.Children.Add(mark);
            }
        UpdateInk();
    }

    void UpdateInk()
    {
        var ink = KronoDesign.ThemeBrush("KronoAccentForeground", ActualTheme);
        foreach (Path mark in tiles.Children) mark.Stroke = ink;
    }
}
