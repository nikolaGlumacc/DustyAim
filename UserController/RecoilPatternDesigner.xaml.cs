using AimmyWPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AimmyWPF.UserController
{
    public partial class RecoilPatternDesigner : UserControl
    {
        private readonly Polyline _stroke;
        private readonly List<Point> _points = new();
        private bool _isDrawing;

        public RecoilPatternDesigner()
        {
            InitializeComponent();

            _stroke = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(62, 143, 176)),
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            DrawCanvas.Children.Add(_stroke);
            SizeChanged += (_, __) => RedrawGuides();
            Loaded += (_, __) => RedrawGuides();
        }

        public int PointCount => _points.Count;

        public void ClearPattern()
        {
            _points.Clear();
            _stroke.Points.Clear();
            EmptyStateText.Visibility = Visibility.Visible;
            RedrawGuides();
        }

        public List<RecoilPatternStep> GetPatternSteps(double pixelsPerUnit, int delayMs)
        {
            double divisor = Math.Max(1.0, pixelsPerUnit);
            int normalizedDelay = Math.Clamp(delayMs, 1, 250);
            List<RecoilPatternStep> steps = new();

            if (_points.Count < 2)
                return steps;

            Point previous = _points[0];
            double carryX = 0;
            double carryY = 0;

            foreach (Point point in _points.Skip(1))
            {
                double rawDx = ((point.X - previous.X) / divisor) + carryX;
                double rawDy = ((point.Y - previous.Y) / divisor) + carryY;
                int dx = (int)Math.Round(rawDx);
                int dy = (int)Math.Round(rawDy);
                carryX = rawDx - dx;
                carryY = rawDy - dy;

                if (dx != 0 || dy != 0)
                    steps.Add(new RecoilPatternStep { Dx = dx, Dy = dy, DelayMs = normalizedDelay });

                previous = point;
            }

            return steps;
        }

        private void DrawCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDrawing = true;
            _points.Clear();
            _stroke.Points.Clear();
            CaptureMouse();
            AddPoint(e.GetPosition(DrawCanvas), force: true);
        }

        private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing)
                return;

            AddPoint(e.GetPosition(DrawCanvas), force: false);
        }

        private void DrawCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing)
                return;

            AddPoint(e.GetPosition(DrawCanvas), force: true);
            _isDrawing = false;
            ReleaseMouseCapture();
        }

        private void AddPoint(Point point, bool force)
        {
            point = ClampToCanvas(point);

            if (!force && _points.Count > 0)
            {
                Point last = _points[^1];
                double distance = Math.Sqrt(Math.Pow(point.X - last.X, 2) + Math.Pow(point.Y - last.Y, 2));
                if (distance < 3)
                    return;
            }

            _points.Add(point);
            _stroke.Points.Add(point);
            EmptyStateText.Visibility = Visibility.Collapsed;
        }

        private Point ClampToCanvas(Point point)
        {
            double width = Math.Max(0, DrawCanvas.ActualWidth);
            double height = Math.Max(0, DrawCanvas.ActualHeight);
            return new Point(
                Math.Clamp(point.X, 0, width),
                Math.Clamp(point.Y, 0, height));
        }

        private void RedrawGuides()
        {
            if (DrawCanvas == null || _stroke == null)
                return;

            DrawCanvas.Children.Clear();

            double width = DrawCanvas.ActualWidth;
            double height = DrawCanvas.ActualHeight;
            if (width > 0 && height > 0)
            {
                DrawCanvas.Children.Add(new Line
                {
                    X1 = width / 2,
                    X2 = width / 2,
                    Y1 = 0,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
                    StrokeThickness = 1
                });

                DrawCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = height / 2,
                    Y2 = height / 2,
                    Stroke = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
                    StrokeThickness = 1
                });
            }

            DrawCanvas.Children.Add(_stroke);
        }
    }
}
