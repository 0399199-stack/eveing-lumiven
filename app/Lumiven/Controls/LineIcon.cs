using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Lumiven.Controls;

public sealed class LineIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(LineIcon), new PropertyMetadata("Download", OnKindChanged));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public LineIcon() { IsHitTestVisible = false; Draw(); }
    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LineIcon)sender).Draw();

    private void Draw()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        void Add(Shape shape, bool filled = false)
        {
            shape.StrokeThickness = 1.7;
            shape.StrokeLineJoin = PenLineJoin.Round;
            shape.StrokeStartLineCap = PenLineCap.Round;
            shape.StrokeEndLineCap = PenLineCap.Round;
            shape.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            if (filled) shape.SetBinding(Shape.FillProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            canvas.Children.Add(shape);
        }
        void Line(double x1, double y1, double x2, double y2) => Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
        void Stroke(params double[] xy)
        {
            var line = new Polyline();
            for (int i = 0; i < xy.Length; i += 2) line.Points.Add(new Point(xy[i], xy[i + 1]));
            Add(line);
        }
        void Circle(double x, double y, double radius, bool filled = false)
        {
            var circle = new Ellipse { Width = radius * 2, Height = radius * 2 };
            Canvas.SetLeft(circle, x - radius); Canvas.SetTop(circle, y - radius); Add(circle, filled);
        }
        void Play(double x = 9, double y = 7, double size = 10, bool filled = false)
        {
            var triangle = new Polygon();
            triangle.Points.Add(new Point(x, y)); triangle.Points.Add(new Point(x + size * .75, y + size * .5)); triangle.Points.Add(new Point(x, y + size)); Add(triangle, filled);
        }
        switch (Kind)
        {
            case "Link": Stroke(9, 14, 6, 17, 3, 17, 1, 15, 1, 12, 6, 7, 9, 7, 11, 9); Stroke(13, 15, 15, 17, 18, 17, 23, 12, 23, 9, 21, 7, 18, 7, 15, 10); Line(8, 16, 16, 8); break;
            case "Douyin": Line(14, 3, 14, 16); Stroke(14, 3, 17, 6, 21, 7, 21, 10, 17, 9, 14, 7); Circle(10, 17, 4); break;
            case "Xiaohongshu": Stroke(12, 5, 8, 3, 3, 3, 3, 19, 8, 19, 12, 21, 16, 19, 21, 19, 21, 3, 16, 3, 12, 5, 12, 21); Line(6, 8, 9, 9); Line(15, 9, 18, 8); break;
            case "Clean": Stroke(3, 14, 12, 5, 19, 12, 10, 21, 6, 21, 3, 18, 3, 14); Line(7, 10, 14, 17); Line(10, 21, 21, 21); Line(20, 2, 20, 6); Line(18, 4, 22, 4); break;
            case "More": Circle(5, 12, 1, true); Circle(12, 12, 1, true); Circle(19, 12, 1, true); break;
            case "Select": Stroke(9, 3, 3, 3, 3, 9); Stroke(15, 3, 21, 3, 21, 9); Stroke(21, 15, 21, 21, 15, 21); Stroke(9, 21, 3, 21, 3, 15); Stroke(7, 12, 10, 15, 17, 8); break;
            case "Library": Stroke(5, 4, 19, 4, 21, 6, 21, 18, 19, 20, 5, 20, 3, 18, 3, 6, 5, 4); Play(9, 8, 8, true); break;
            case "Settings": Line(3, 7, 6, 7); Line(12, 7, 21, 7); Circle(9, 7, 3); Line(3, 17, 12, 17); Line(18, 17, 21, 17); Circle(15, 17, 3); break;
            case "About": Circle(12, 12, 9); Circle(12, 7.5, .6, true); Line(12, 11, 12, 17); break;
            case "Folder": Stroke(3, 7, 3, 19, 21, 19, 21, 8, 12, 8, 10, 5, 3, 5, 3, 7); break;
            case "Import": Stroke(3, 7, 3, 19, 12, 19); Stroke(3, 7, 3, 5, 10, 5, 12, 8, 21, 8, 21, 13); Stroke(17, 17, 21, 17, 21, 21); Line(17, 21, 21, 17); break;
            case "Refresh":
                var figure = new PathFigure { StartPoint = new Point(20, 8) };
                figure.Segments.Add(new ArcSegment { Point = new Point(19, 18), Size = new Size(8, 8), IsLargeArc = true, SweepDirection = SweepDirection.Counterclockwise });
                var geometry = new PathGeometry(); geometry.Figures.Add(figure); Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry });
                Stroke(20, 3, 20, 8, 15, 8); break;
            case "Search": Circle(10, 10, 7); Line(15, 15, 21, 21); break;
            case "Play": Play(8, 5, 14, true); break;
            case "Back": Stroke(13, 5, 6, 12, 13, 19); Line(6, 12, 21, 12); break;
            case "Fullscreen": Stroke(3, 9, 3, 3, 9, 3); Stroke(15, 3, 21, 3, 21, 9); Stroke(21, 15, 21, 21, 15, 21); Stroke(9, 21, 3, 21, 3, 15); break;
            default: Line(12, 3, 12, 15); Stroke(7, 10, 12, 15, 17, 10); Stroke(3, 14, 3, 21, 21, 21, 21, 14); break;
        }
    }
}
