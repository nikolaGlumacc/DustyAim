using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Visualization
{
    public partial class DebugOverlay : Window
    {
        // Stats updated from outside
        public static bool IsLoopRunning = false;
        public static int TotalFrames = 0;
        public static int DetectionsThisSec = 0;
        public static float LastMaxConf = 0f;
        public static float CurrentThreshold = 0.5f;
        public static string ModelStyleStr = "?";
        public static int NumAnchors = 0;
        public static double DpiX = 1.0, DpiY = 1.0;
        public static int CaptureX = 0, CaptureY = 0;
        public static int CapW = 0, CapH = 0;
        public static int PhysW = 0, PhysH = 0;
        public static Action SaveFrameAction = null;

        private static DebugOverlay _instance;
        private readonly DispatcherTimer _timer;
        private int _lastFrameCount = 0;
        private readonly Queue<string> _logLines = new();
        private const int MAX_LOG_LINES = 60;

        public static DebugOverlay Instance => _instance;

        public DebugOverlay()
        {
            InitializeComponent();
            _instance = this;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Refresh;
            _timer.Start();
        }

        public static void AddLog(string msg)
        {
            _instance?.Dispatcher.InvokeAsync(() => _instance.AppendLog(msg));
        }

        private void AppendLog(string msg)
        {
            _logLines.Enqueue($"[{DateTime.Now:HH:mm:ss.ff}] {msg}");
            while (_logLines.Count > MAX_LOG_LINES) _logLines.Dequeue();
            LogText.Text = string.Join("\n", _logLines);
            LogScroller.ScrollToBottom();
        }

        private void Refresh(object sender, EventArgs e)
        {
            LoopStatus.Text = IsLoopRunning ? "YES" : "NO";
            LoopStatus.Foreground = IsLoopRunning
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Red;

            FrameCount.Text = TotalFrames.ToString();
            DetectCount.Text = DetectionsThisSec.ToString();
            MaxConf.Text = LastMaxConf.ToString("F3");
            MaxConf.Foreground = LastMaxConf >= CurrentThreshold
                ? System.Windows.Media.Brushes.LightGreen
                : (LastMaxConf > 0.1f ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White);

            Threshold.Text = CurrentThreshold.ToString("F2");
            ModelStyle.Text = ModelStyleStr;
            Anchors.Text = NumAnchors.ToString();
            DpiInfo.Text = $"{DpiX:F2}x{DpiY:F2}";
            CaptureXY.Text = $"{CaptureX}, {CaptureY}";
            CaptureSize.Text = $"{CapW}x{CapH}";
            ScreenSize.Text = $"{PhysW}x{PhysH}";

            // Calculate FPS from frame delta over 250ms
            int frameDelta = TotalFrames - _lastFrameCount;
            int fps = frameDelta * 4; // 4 ticks per second
            InferenceFPS.Text = fps.ToString();
            _lastFrameCount = TotalFrames;
        }

        private void SaveFrameBtn_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Save requested...");
            if (SaveFrameAction == null)
            {
                AddLog("Save Error: SaveFrameAction is null!");
                return;
            }
            Task.Run(() => SaveFrameAction?.Invoke());
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = Path.Combine(baseDir, "captures");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
                AddLog("Opened captures folder.");
            }
            catch (Exception ex)
            {
                AddLog($"Error opening folder: {ex.Message}");
            }
        }

        private void OpenLogBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dusty_debug.log");
                if (!File.Exists(logPath))
                {
                    AddLog("Log file does not exist yet.");
                    return;
                }
                Process.Start("notepad.exe", logPath);
            }
            catch (Exception ex)
            {
                AddLog($"Error opening log: {ex.Message}");
            }
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            _logLines.Clear();
            LogText.Text = "";
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
