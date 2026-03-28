using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AimmyWPF.UserController
{
    public partial class AModeSelector : UserControl
    {
        public event Action<string> ModeChanged;
        private string _currentMode = "Hold";

        // Height the row animates to when shown
        private const double ExpandedHeight = 60.0;

        public AModeSelector()
        {
            InitializeComponent();
            // Start collapsed
            AnimationRoot.Height = 0;
        }

        public string CurrentMode
        {
            get => _currentMode;
            set
            {
                _currentMode = value;
                UpdateUI();
            }
        }

        // Override Visibility to drive animation instead
        public new Visibility Visibility
        {
            get => base.Visibility;
            set
            {
                // Always keep the UserControl itself visible so animation works
                base.Visibility = Visibility.Visible;
                if (value == Visibility.Visible)
                    AnimateOpen();
                else
                    AnimateClose();
            }
        }

        private void AnimateOpen()
        {
            var anim = new DoubleAnimation
            {
                To = ExpandedHeight,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            AnimationRoot.BeginAnimation(HeightProperty, anim);
        }

        private void AnimateClose()
        {
            var anim = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            AnimationRoot.BeginAnimation(HeightProperty, anim);
        }

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string mode)
            {
                CurrentMode = mode;
                ModeChanged?.Invoke(mode);
            }
        }

        private void UpdateUI()
        {
            Brush activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3E8FB0"));
            Brush inactiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF323232"));

            HoldButton.Background = (_currentMode == "Hold") ? activeBrush : inactiveBrush;
            ToggleButton.Background = (_currentMode == "Toggle") ? activeBrush : inactiveBrush;
        }
    }
}
