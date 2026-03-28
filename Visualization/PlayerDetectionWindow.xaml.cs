using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Globalization;
using System.Windows.Markup;
using AimmyWPF.Class;
using AimmyWPF;

namespace Visualization
{
    /// <summary>
    /// Interaction logic for PlayerDetectionWindow.xaml
    /// </summary>
    public partial class PlayerDetectionWindow : Window
    {
        public PlayerDetectionWindow()
        {
            InitializeComponent();

            this.Title = System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
            SourceInitialized += (s, e) => OverlayClickThrough.Apply(this);

            AwfulPropertyChanger.ReceivePDWSize = ChangeSize;
            AwfulPropertyChanger.ReceivePDWCornerRadius = ChangeCornerRadius;
            AwfulPropertyChanger.ReceivePDWBorderThickness = ChangeBorderThickness;
            AwfulPropertyChanger.ReceivePDWOpacity = ChangeOpacity;

            UpdateWindowBounds();
        }

        private void ChangeSize(int newint)
        {
            DetectedPlayerFocus.Width = newint;
            DetectedPlayerFocus.Height = newint;

            UnfilteredPlayerFocus.Width = newint;
            UnfilteredPlayerFocus.Height = newint;

            PredictionFocus.Width = newint;
            PredictionFocus.Height = newint;
        }

        private void ChangeCornerRadius(int newint)
        {
            DetectedPlayerFocus.CornerRadius = new CornerRadius(newint);
            UnfilteredPlayerFocus.CornerRadius = new CornerRadius(newint);
            PredictionFocus.CornerRadius = new CornerRadius(newint);
        }

        void ChangeBorderThickness(double newdouble)
        {
            DetectedPlayerFocus.BorderThickness = new Thickness(newdouble);
            UnfilteredPlayerFocus.BorderThickness = new Thickness(newdouble);
            PredictionFocus.BorderThickness = new Thickness(newdouble);

        }

        private void ChangeOpacity(double newdouble)
        {
            DetectedPlayerFocus.Opacity = newdouble;
            UnfilteredPlayerFocus.Opacity = newdouble;
            PredictionFocus.Opacity = newdouble;
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWindowBounds();
            OverlayClickThrough.Apply(this);
        }

        private void UpdateWindowBounds()
        {
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var width = SystemParameters.VirtualScreenWidth;
            var height = SystemParameters.VirtualScreenHeight;

            this.Left = left;
            this.Top = top;
            this.Width = width;
            this.Height = height;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        public Point ScreenToWindow(Point screenPoint)
        {
            return PointFromScreen(screenPoint);
        }
    }

    public class HalfValueConverter : MarkupExtension, IValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d / 2;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
