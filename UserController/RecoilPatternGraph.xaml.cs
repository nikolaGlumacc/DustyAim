using AimmyWPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AimmyWPF.UserController
{
    /// <summary>
    /// Interaction logic for RecoilPatternGraph.xaml
    /// </summary>
    public partial class RecoilPatternGraph : UserControl
    {
        private readonly Polyline _curve;
        private readonly Line _axisX;
        private readonly Line _axisY;
        private List<RecoilPatternStep> _steps = new();

        public RecoilPatternGraph()
        {
            InitializeComponent();

            _axisX = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = 1
            };

            _axisY = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = 1
            };

            _curve = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(255, 62, 143, 176)),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            PlotCanvas.Children.Add(_axisX);
            PlotCanvas.Children.Add(_axisY);
            PlotCanvas.Children.Add(_curve);

            SizeChanged += (_, __) => Redraw();
            Loaded += (_, __) => Redraw();
        }

        public void SetPattern(IEnumerable<RecoilPatternStep> steps)
        {
            _steps = steps?.ToList() ?? new List<RecoilPatternStep>();
            Redraw();
        }

        private void Redraw()
        {
            if (!IsLoaded || PlotCanvas == null)
                return;

            double width = PlotCanvas.ActualWidth;
            double height = PlotCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            PlotCanvas.Children.Clear();
            PlotCanvas.Children.Add(_axisX);
            PlotCanvas.Children.Add(_axisY);
            PlotCanvas.Children.Add(_curve);

            _axisX.X1 = 0;
            _axisX.Y1 = height / 2;
            _axisX.X2 = width;
            _axisX.Y2 = height / 2;

            _axisY.X1 = width / 2;
            _axisY.Y1 = 0;
            _axisY.X2 = width / 2;
            _axisY.Y2 = height;

            if (_steps.Count == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                _curve.Points.Clear();
                return;
            }

            EmptyStateText.Visibility = Visibility.Collapsed;

            var points = new List<Point>
            {
                new Point(0, 0)
            };

            double currentX = 0;
            double currentY = 0;
            foreach (var step in _steps)
            {
                currentX += step.Dx;
                currentY -= step.Dy;
                points.Add(new Point(currentX, currentY));
            }

            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);

            double rangeX = Math.Max(1.0, maxX - minX);
            double rangeY = Math.Max(1.0, maxY - minY);
            double scale = Math.Min(width / rangeX, height / rangeY) * 0.82;
            double centerX = width / 2.0;
            double centerY = height / 2.0;
            double dataCenterX = (minX + maxX) / 2.0;
            double dataCenterY = (minY + maxY) / 2.0;

            PointCollection curvePoints = new();
            foreach (var point in points)
            {
                double px = centerX + (point.X - dataCenterX) * scale;
                double py = centerY - (point.Y - dataCenterY) * scale;
                curvePoints.Add(new Point(px, py));
            }

            _curve.Points = curvePoints;
        }
    }
}
