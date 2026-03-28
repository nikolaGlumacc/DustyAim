using AimmyWPF.Class;
using AimmyWPF;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SecondaryWindows
{
    public partial class HudOverlay : Window
    {
        public static string ModeText = "Aim: Hold";
        public static string PatternText = "none";
        public static string AimStatusText = "Aim locked";
        public static Brush AimStatusBrush = Brushes.IndianRed;

        public static double CustomX = -1;
        public static double CustomY = -1;
        public static double CustomOpacity = 1.0;
        public static double CustomScale = 1.0;
        public static Action<double, double> OnPositionSaved;

        private readonly DispatcherTimer _timer;
        private bool _isUnlocked = false;

        public HudOverlay()
        {
            InitializeComponent();
            Title = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
            SourceInitialized += (_, __) => { if (!_isUnlocked) OverlayClickThrough.Apply(this); };

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timer.Tick += (_, __) => Refresh();
            _timer.Start();

            this.LocationChanged += (s, e) =>
            {
                if (_isUnlocked && IsLoaded)
                {
                    OnPositionSaved?.Invoke(Left, Top);
                }
            };
        }

        public void SetUnlocked(bool unlocked)
        {
            _isUnlocked = unlocked;
            if (unlocked)
            {
                OverlayClickThrough.Remove(this);
                HudBorder.BorderThickness = new Thickness(2);
                HudBorder.BorderBrush = Brushes.Yellow;
            }
            else
            {
                OverlayClickThrough.Apply(this);
                HudBorder.BorderThickness = new Thickness(1);
                HudBorder.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#5544AACC");
            }
        }

        private void HudBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isUnlocked)
            {
                DragMove();
            }
        }

        private void Refresh()
        {
            ModeTextBlock.Text = ModeText;
            PatternTextBlock.Text = PatternText;
            AimStatusTextBlock.Text = AimStatusText;
            AimDot.Fill = AimStatusBrush;

            HudScale.ScaleX = CustomScale;
            HudScale.ScaleY = CustomScale;
            HudBorder.Opacity = CustomOpacity;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (CustomX != -1 && CustomY != -1)
            {
                Left = CustomX;
                Top = CustomY;
            }
            else
            {
                Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - MinWidth - 20;
                Top = SystemParameters.VirtualScreenTop + 20;
            }
            Refresh();
            if (!_isUnlocked)
            {
                OverlayClickThrough.Apply(this);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
