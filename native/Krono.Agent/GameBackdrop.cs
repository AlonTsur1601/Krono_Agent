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

        Line(new(24,30), new(38,30), new(38,42), new(46,42)); Ring(51,33,1);
        Line(new(119,16), new(127,16), new(127,24), new(135,24), new(135,32), new(111,32), new(111,24), new(119,24), new(119,16));
        Line(new(119,24), new(119,32)); Line(new(127,24), new(127,32));
        Ring(229,47,8); Ring(235,41,2);
        Line(new(278,19), new(287,28)); Line(new(287,19), new(278,28)); Ring(296,24,5);
        Line(new(52,98), new(52,112)); Line(new(74,98), new(74,112)); Ring(63,103,1.5);
        Ring(159,74,10); Ring(159,74,4);
        Line(new(263,117), new(263,100), new(275,105), new(263,110)); Line(new(258,117), new(269,117));
        Line(new(24,163), new(40,163), new(40,179), new(24,179), new(24,163)); Ring(29,168,1); Ring(35,174,1);
        Line(new(101,197), new(109,189), new(117,197), new(109,205), new(101,197));
        Line(new(189,151), new(198,151), new(198,160), new(207,160), new(207,169), new(189,169), new(189,151));
        Line(new(189,160), new(198,160), new(198,169));
        Line(new(282,171), new(290,179)); Line(new(290,171), new(282,179));
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
