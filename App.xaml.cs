using System.Windows;

namespace AimmyWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.IO.File.WriteAllText("crash.log", e.Exception.ToString());
            e.Handled = true; // Prevents the application from closing immediately if possible
            MessageBox.Show("A fatal error occurred. Check crash.log for details.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            System.IO.File.WriteAllText("crash_domain.log", e.ExceptionObject.ToString());
            MessageBox.Show("A fatal error occurred. Check crash_domain.log for details.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}