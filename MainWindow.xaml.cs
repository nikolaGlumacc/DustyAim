using AimmyAimbot;
using AimmyWPF.Class;
using AimmyWPF.UserController;
using Class;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Ports;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Visualization;
using static AimmyWPF.PredictionManager;

namespace AimmyWPF
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private PredictionManager predictionManager;
        private OverlayWindow FOVOverlay;
        private PlayerDetectionWindow DetectedPlayerOverlay;
        private Visualization.DebugOverlay debugOverlay;
        private FileSystemWatcher fileWatcher;
        private FileSystemWatcher ConfigfileWatcher;

        private string lastLoadedModel = "N/A";
        private string lastLoadedConfig = "N/A";

        private readonly BrushConverter brushcolor = new();
        private const string DefaultMenuAccentColor = "#3e8fb0";

        private DateTime LastClickTime = DateTime.MinValue;
        private readonly string SessionStatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "configs", "SessionState.cfg");
        private DateTime _lastInputInjectionErrorLog = DateTime.MinValue;
        private int _lastInputInjectionErrorCode = int.MinValue;
        private bool _hasSmoothedAimTarget = false;
        private double _smoothedAimX = 0;
        private double _smoothedAimY = 0;
        private double _moveRemainderX = 0;
        private double _moveRemainderY = 0;
        private DateTime _lastAimTargetUpdateUtc = DateTime.MinValue;
        private DateTime _lastAimTargetSeenUtc = DateTime.MinValue;
        private bool _isApplyingAimStylePreset = false;
        private readonly object _recoilPatternLock = new();
        private List<RecoilPatternStep> _recoilPatternSteps = new();
        private string _loadedRecoilPatternPath = string.Empty;
        private int _recoilPatternIndex = 0;
        private DateTime _nextRecoilStepUtc = DateTime.MinValue;
        private bool _wasLeftMouseDown = false;
        private double _recoilRemainderX = 0;
        private double _recoilRemainderY = 0;
        private DateTime _nextRapidFireClickUtc = DateTime.MinValue;
        private bool _wasRapidFireLeftMouseDown = false;
        private readonly SemaphoreSlim _rapidFireClickGate = new(1, 1);
        private CancellationTokenSource _rapidFireLoopCts;
        private MenuPosition _selectedMenuPosition = MenuPosition.AimMenu;
        private bool _isRecordingRecoil = false;
        private bool _isRecoilRecordingEnabled = false;
        private List<RecoilPatternStep> _recordedRecoilSteps = new();
        private InputBindingManager _recoilRecordingBindingManager;
        private InputBindingManager _patternCycleBindingManager;
        private TextBlock _recoilStatusTextBlock;
        private RecoilPatternGraph _recoilPatternGraph;
        private SecondaryWindows.HudOverlay _hudOverlay;
        private System.Drawing.Point _lastRecordMousePos;
        private int _captureLoopErrorCount = 0;
        private DateTime _captureLoopErrorWindowStartUtc = DateTime.MinValue;
        private int _rapidFireLoopErrorCount = 0;
        private DateTime _rapidFireLoopErrorWindowStartUtc = DateTime.MinValue;
        private int _recoilRecordingLoopErrorCount = 0;
        private DateTime _recoilRecordingLoopErrorWindowStartUtc = DateTime.MinValue;


        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001; // Movement flag
        private const uint MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint INPUT_MOUSE = 0;

        private static int ScreenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
        private static int ScreenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

        private int _logCounter = 0;
        private DateTime _lastIdleHeartbeatLog = DateTime.MinValue;
        private void Log(string message)
        {
            try {
                File.AppendAllText("dusty_debug.log", $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            } catch { }
        }

        private AIModel _onnxModel;
        private readonly InputBindingManager[] _bindingManagers = new InputBindingManager[3];
        private InputBindingManager bindingManager => _bindingManagers[0]; // kept as alias for existing code
        private bool IsHolding_Binding = false;
        private CancellationTokenSource cts;
        private readonly SemaphoreSlim _modelAccessGate = new(1, 1);
        private readonly SemaphoreSlim _triggerClickGate = new(1, 1);

        private sealed class SessionStateData
        {
            public string LastLoadedModel { get; set; } = "N/A";
            public string LastLoadedConfig { get; set; } = "N/A";
            public string Binding { get; set; } = "Right";
            public string[] AimBindingSlots { get; set; } = new[] { "Right", "None", "None" };
            public int ActiveBindingSlot { get; set; } = 0;
            public Dictionary<string, bool> ToggleState { get; set; } = new();
        }

        private enum MenuPosition
        {
            AimMenu,
            TriggerMenu,
            SelectorMenu,
            RecoilMenu,
            SettingsMenu
        }

        // Changed to Dynamic from Double because it was making the Config System hard to rework :/
        private string RecoilFolderPath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string currentDir = Directory.GetCurrentDirectory(); 
                
                string[] possiblePaths = {
                    Path.Combine(currentDir, "recoil"), // Check next to shortcut/working dir first
                    Path.Combine(baseDir, "recoil"),    // Check next to actual exe
                    Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\recoil")), // Main Folder (Dev)
                    Path.Combine(baseDir, "bin", "recoil")
                };

                foreach (string path in possiblePaths)
                    if (Directory.Exists(path)) return path;

                return possiblePaths[1]; // Default to next to exe
            }
        }

        internal static Dictionary<string, dynamic> aimmySettings = new()
        {
            { "Suggested_Model", ""},
            { "FOV_Size", 320 },
            { "Mouse_Sens", 0.82 },
            { "Mouse_Jitter", 1 },
            { "Aim_Smoothness", 0.88 },
            { "Aim_MaxStep", 9.5 },
            { "Aim_Deadzone", 1.35 },
            { "Aim_StylePreset", "Premium Smooth" },
            { "Y_Offset", 0 },
            { "X_Offset", 0 },
            { "Aim_HeadRatio", 0.15 },
            { "Aim_PredictionStrength", 1.0 },
            { "Aim_BoxFormat", "center" },
            { "Trigger_Delay", 0.1 },
            { "Recoil_PatternPath", "" },
            { "Recoil_Scale", 1.0 },
            { "Recoil_SpeedMultiplier", 1.0 },
            { "Recoil_DefaultStepDelay", 16.0 },
            { "Recoil_ControlEnabled", false },
            { "Recoil_LoopPattern", true },
            { "Recoil_AdsOnly", false },
            { "Recoil_RapidFire", false },
            { "Recoil_RapidFireDelayMs", 90.0 },
            { "Recoil_RecordingKey", "F12" },
            { "Recoil_CycleKey", "F11" },
            { "Hardware_UseArduino", false },
            { "Hardware_ComPort", "COM3" },
            { "Show_MiniHud", false },
            { "GUI_AccentColor", DefaultMenuAccentColor },
            { "AI_Min_Conf", 5 },
            { "MiniHud_UnlockPosition", false },
            { "MiniHud_Opacity", 1.0 },
            { "MiniHud_Scale", 1.0 },
            { "MiniHud_X", -1.0 },
            { "MiniHud_Y", -1.0 }
        };

        private Dictionary<string, bool> toggleState = new()
        {
            { "AimbotToggle", false },
            { "AlwaysOn", false },
            { "PredictionToggle", false },
            { "AimViewToggle", false },
            { "TriggerBot", false },
            { "CollectData", false },
            { "TopMost", false },
            { "ConstantAITracking", false },
            { "ShowDebugOverlay", false },
            { "AimOnlyWhenBindingHeld", false },
            { "ShowDetectedPlayerWindow", false },
            { "ShowCurrentDetectedPlayer", false },
            { "ShowUnfilteredDetectedPlayer", false },
            { "ShowAIPrediction", false },
            { "RecoilControl", false },
            { "RecoilLoopPattern", true },
            { "RecoilAdsOnly", false },
            { "RecoilRapidFire", false }
        };

        private readonly Dictionary<string, AToggle> toggleRegistry = new();
        private readonly Dictionary<string, Action<bool>> toggleActions = new();

        private bool ToggleStateIsActive(string key)
        {
            return toggleState.TryGetValue(key, out bool value) && value;
        }

        private double GetSettingDouble(string key, double fallback = 0)
        {
            if (!aimmySettings.TryGetValue(key, out var value) || value == null)
                return fallback;

            try
            {
                if (value is string text)
                {
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantParsed))
                        return invariantParsed;

                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentParsed))
                        return currentParsed;
                }

                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                try
                {
                    return Convert.ToDouble(value);
                }
                catch
                {
                    return fallback;
                }
            }
        }

        private int GetSettingInt(string key, int fallback = 0)
        {
            return (int)Math.Round(GetSettingDouble(key, fallback));
        }

        private bool GetSettingBool(string key, bool fallback = false)
        {
            if (!aimmySettings.TryGetValue(key, out var value) || value == null)
                return fallback;

            try
            {
                if (value is bool boolValue)
                    return boolValue;

                if (value is string text && bool.TryParse(text, out bool parsedBool))
                    return parsedBool;

                return Convert.ToBoolean(value);
            }
            catch
            {
                try
                {
                    return Convert.ToBoolean(value);
                }
                catch
                {
                    return fallback;
                }
            }
        }

        private string GetSettingString(string key, string fallback = "")
        {
            if (!aimmySettings.TryGetValue(key, out var value) || value == null)
                return fallback;

            string text = value.ToString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static double Lerp(double start, double end, double amount)
        {
            return start + ((end - start) * amount);
        }

        private void ResetAimMovementState()
        {
            _hasSmoothedAimTarget = false;
            _moveRemainderX = 0;
            _moveRemainderY = 0;
            _lastAimTargetUpdateUtc = DateTime.MinValue;
            _lastAimTargetSeenUtc = DateTime.MinValue;
        }

        private (double X, double Y) GetSmoothedAimDelta(double rawDeltaX, double rawDeltaY)
        {
            DateTime now = DateTime.UtcNow;
            if (!_hasSmoothedAimTarget || (now - _lastAimTargetSeenUtc).TotalMilliseconds > 175)
            {
                _smoothedAimX = rawDeltaX;
                _smoothedAimY = rawDeltaY;
                _hasSmoothedAimTarget = true;
                _lastAimTargetUpdateUtc = now;
                _lastAimTargetSeenUtc = now;
                return (_smoothedAimX, _smoothedAimY);
            }

            double elapsedSeconds = _lastAimTargetUpdateUtc == DateTime.MinValue
                ? (1.0 / 120.0)
                : Math.Clamp((now - _lastAimTargetUpdateUtc).TotalSeconds, 1.0 / 240.0, 0.05);

            double smoothness = Math.Clamp(GetSettingDouble("Aim_Smoothness", 0.78), 0.05, 0.98);
            double jumpX = rawDeltaX - _smoothedAimX;
            double jumpY = rawDeltaY - _smoothedAimY;
            double jumpDistance = Math.Sqrt((jumpX * jumpX) + (jumpY * jumpY));
            double retargetSnapDistance = Math.Max(80.0, GetSettingDouble("FOV_Size", 320) * 0.45);

            if (jumpDistance >= retargetSnapDistance)
            {
                _smoothedAimX = rawDeltaX;
                _smoothedAimY = rawDeltaY;
            }
            else
            {
                double catchUpBoost = 1.0 + Math.Clamp(jumpDistance / 160.0, 0.0, 1.5);
                double blend = 1.0 - Math.Pow(smoothness, elapsedSeconds * 120.0 * catchUpBoost);
                blend = Math.Clamp(blend, 0.08, 0.90);

                _smoothedAimX += jumpX * blend;
                _smoothedAimY += jumpY * blend;
            }

            _lastAimTargetUpdateUtc = now;
            _lastAimTargetSeenUtc = now;
            return (_smoothedAimX, _smoothedAimY);
        }

        private sealed class AimStylePreset
        {
            public AimStylePreset(
                string name,
                double mouseSensitivity,
                double smoothness,
                double maxStep,
                double deadzone,
                double jitter,
                double headRatio)
            {
                Name = name;
                MouseSensitivity = mouseSensitivity;
                Smoothness = smoothness;
                MaxStep = maxStep;
                Deadzone = deadzone;
                Jitter = jitter;
                HeadRatio = headRatio;
            }

            public string Name { get; }
            public double MouseSensitivity { get; }
            public double Smoothness { get; }
            public double MaxStep { get; }
            public double Deadzone { get; }
            public double Jitter { get; }
            public double HeadRatio { get; }
        }

        private static readonly Dictionary<string, AimStylePreset> AimStylePresets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Premium Smooth"] = new AimStylePreset("Premium Smooth", 0.82, 0.88, 9.5, 1.35, 1, 0.10),
            ["Balanced"] = new AimStylePreset("Balanced", 0.72, 0.76, 13.5, 1.0, 1, 0.12),
            ["Aggressive"] = new AimStylePreset("Aggressive", 0.54, 0.56, 24.0, 0.35, 0, 0.14)
        };

        private void MarkAimStyleAsCustomIfNeeded()
        {
            if (_isApplyingAimStylePreset)
                return;

            aimmySettings["Aim_StylePreset"] = "Custom";
        }

        private void SyncAimSettingsWithPresetIfSelected()
        {
            if (!aimmySettings.TryGetValue("Aim_StylePreset", out var styleValue) || styleValue == null)
                return;

            string presetName = styleValue.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(presetName) || presetName.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                return;

            ApplyAimStylePreset(presetName);
        }

        private void ApplyAimStylePreset(
            string presetName,
            Slider mouseSensitivitySlider = null,
            Slider smoothnessSlider = null,
            Slider maxStepSlider = null,
            Slider deadzoneSlider = null,
            Slider jitterSlider = null,
            Slider headRatioSlider = null)
        {
            if (!AimStylePresets.TryGetValue(presetName, out var preset))
                return;

            aimmySettings["Aim_StylePreset"] = preset.Name;
            aimmySettings["Mouse_Sens"] = preset.MouseSensitivity;
            aimmySettings["Aim_Smoothness"] = preset.Smoothness;
            aimmySettings["Aim_MaxStep"] = preset.MaxStep;
            aimmySettings["Aim_Deadzone"] = preset.Deadzone;
            aimmySettings["Mouse_Jitter"] = preset.Jitter;
            aimmySettings["Aim_HeadRatio"] = preset.HeadRatio;

            _isApplyingAimStylePreset = true;
            try
            {
                if (mouseSensitivitySlider != null) mouseSensitivitySlider.Value = preset.MouseSensitivity;
                if (smoothnessSlider != null) smoothnessSlider.Value = preset.Smoothness;
                if (maxStepSlider != null) maxStepSlider.Value = preset.MaxStep;
                if (deadzoneSlider != null) deadzoneSlider.Value = preset.Deadzone;
                if (jitterSlider != null) jitterSlider.Value = preset.Jitter;
                if (headRatioSlider != null) headRatioSlider.Value = preset.HeadRatio * 100.0;
            }
            finally
            {
                _isApplyingAimStylePreset = false;
            }

            ResetAimMovementState();
        }

        private bool IsAimbotEnabled()
        {
            return ToggleStateIsActive("AimbotToggle") || Bools.AIAimAligner;
        }

        private bool IsTriggerEnabled()
        {
            return ToggleStateIsActive("TriggerBot") || Bools.Triggerbot;
        }

        private bool IsConstantTrackingEnabled()
        {
            return ToggleStateIsActive("ConstantAITracking") || Bools.ConstantTracking;
        }

        private bool IsCollectDataEnabled()
        {
            return ToggleStateIsActive("CollectData") || Bools.CollectDataWhilePlaying;
        }

        private bool IsRecoilEnabled()
        {
            return GetSettingBool("Recoil_ControlEnabled", ToggleStateIsActive("RecoilControl") || Bools.RecoilControl);
        }

        private bool IsRecoilLoopEnabled()
        {
            return GetSettingBool("Recoil_LoopPattern", ToggleStateIsActive("RecoilLoopPattern"));
        }

        private bool IsRecoilAdsOnlyEnabled()
        {
            return GetSettingBool("Recoil_AdsOnly", ToggleStateIsActive("RecoilAdsOnly") || Bools.RecoilAdsOnly);
        }

        private bool IsRapidFireEnabled()
        {
            return GetSettingBool("Recoil_RapidFire", ToggleStateIsActive("RecoilRapidFire") || Bools.RecoilRapidFire);
        }

        private bool IsAimHoldRequired()
        {
            return ToggleStateIsActive("AimOnlyWhenBindingHeld") || Bools.AimOnlyWhenBindingHeld;
        }

        private static AIModel.BoxFormat ParseBoxFormat(dynamic value)
        {
            if (value == null) return AIModel.BoxFormat.Auto;

            if (value is string text)
            {
                string normalized = text.Trim().ToLowerInvariant();
                if (normalized == "center" || normalized == "centre") return AIModel.BoxFormat.Center;
                if (normalized == "top_left" || normalized == "top-left" || normalized == "topleft") return AIModel.BoxFormat.TopLeft;
                if (normalized == "auto") return AIModel.BoxFormat.Auto;
            }
            else if (value is int i)
            {
                if (i == 1) return AIModel.BoxFormat.Center;
                if (i == 2) return AIModel.BoxFormat.TopLeft;
                if (i == 0) return AIModel.BoxFormat.Auto;
            }
            else if (value is long l)
            {
                if (l == 1) return AIModel.BoxFormat.Center;
                if (l == 2) return AIModel.BoxFormat.TopLeft;
                if (l == 0) return AIModel.BoxFormat.Auto;
            }
            else if (value is double d)
            {
                int parsed = (int)Math.Round(d);
                if (parsed == 1) return AIModel.BoxFormat.Center;
                if (parsed == 2) return AIModel.BoxFormat.TopLeft;
                if (parsed == 0) return AIModel.BoxFormat.Auto;
            }
            else if (value is float f)
            {
                int parsed = (int)Math.Round(f);
                if (parsed == 1) return AIModel.BoxFormat.Center;
                if (parsed == 2) return AIModel.BoxFormat.TopLeft;
                if (parsed == 0) return AIModel.BoxFormat.Auto;
            }

            return AIModel.BoxFormat.Auto;
        }

        private void ApplyModelSettings(AIModel model)
        {
            if (model == null)
                return;

            model.FovSize = GetSettingInt("FOV_Size", 320);
            model.ConfidenceThreshold = (float)(GetSettingDouble("AI_Min_Conf", 5.0) / 100.0);
            model.OutputBoxFormat = ParseBoxFormat(GetSettingString("Aim_BoxFormat", "center"));
        }

        private Dictionary<string, bool> BuildToggleSnapshot()
        {
            Dictionary<string, bool> snapshot = new(toggleState);

            foreach (var (name, toggle) in toggleRegistry)
            {
                if (toggle?.Reader?.Tag is bool state)
                    snapshot[name] = state;
            }

            return snapshot;
        }

        private void ApplyToggleSnapshot(Dictionary<string, bool> snapshot)
        {
            if (snapshot == null) return;

            foreach (var (toggleName, state) in snapshot)
            {
                if (toggleRegistry.ContainsKey(toggleName))
                    ForceToggle(toggleName, state);
                else
                    toggleState[toggleName] = state;
            }
        }

        private static readonly object _sessionLock = new object();

        private string ReadSessionStateSafe()
        {
            lock (_sessionLock)
            {
                for (int i = 0; i < 5; i++)
                {
                    try { return File.ReadAllText(SessionStatePath); }
                    catch (IOException) { Thread.Sleep(50); }
                }
                return File.ReadAllText(SessionStatePath);
            }
        }

        private async Task RestoreSessionStateAsync()
        {
            // Always ensure the selected model is actually initialized at startup.
            LoadModelsIntoListBox();
            InitializeModel();

            if (!File.Exists(SessionStatePath))
                return;

            try
            {
                string json = ReadSessionStateSafe();
                SessionStateData session = JsonConvert.DeserializeObject<SessionStateData>(json);
                if (session == null) return;

                if (!string.IsNullOrWhiteSpace(session.LastLoadedModel))
                    lastLoadedModel = session.LastLoadedModel;
                if (!string.IsNullOrWhiteSpace(session.LastLoadedConfig))
                    lastLoadedConfig = session.LastLoadedConfig;

                if (session.AimBindingSlots != null && session.AimBindingSlots.Length > 0)
                {
                    for (int i = 0; i < _bindingManagers.Length; i++)
                    {
                        string binding = i < session.AimBindingSlots.Length
                            ? session.AimBindingSlots[i]
                            : (i == 0 ? session.Binding : "None");

                        binding = string.IsNullOrWhiteSpace(binding)
                            ? (i == 0 ? "Right" : "None")
                            : binding;

                        _bindingManagers[i]?.SetBinding(binding);
                    }

                    Bools.AimBindingSlots = session.AimBindingSlots.Length >= 3
                        ? session.AimBindingSlots.Take(3).ToArray()
                        : new[]
                        {
                            session.AimBindingSlots.ElementAtOrDefault(0) ?? "Right",
                            session.AimBindingSlots.ElementAtOrDefault(1) ?? "None",
                            session.AimBindingSlots.ElementAtOrDefault(2) ?? "None"
                        };
                }
                else if (!string.IsNullOrWhiteSpace(session.Binding))
                {
                    bindingManager?.SetBinding(session.Binding);
                    Bools.AimBindingSlots = new[]
                    {
                        bindingManager?.CurrentBinding ?? session.Binding ?? "Right",
                        _bindingManagers[1]?.CurrentBinding ?? "None",
                        _bindingManagers[2]?.CurrentBinding ?? "None"
                    };
                }

                Bools.ActiveBindingSlot = Math.Clamp(session.ActiveBindingSlot, 0, _bindingManagers.Length - 1);

                LoadModelsIntoListBox();
                InitializeModel();

                if (!string.IsNullOrWhiteSpace(lastLoadedConfig) && lastLoadedConfig != "N/A")
                {
                    string configPath = Path.Combine("bin/configs", lastLoadedConfig);
                    if (File.Exists(configPath))
                        await LoadConfigAsync(configPath);
                }

                ApplyToggleSnapshot(session.ToggleState);
            }
            catch (Exception ex)
            {
                Log($"Session restore failed: {ex.Message}");
            }
        }

        private void SaveSessionState()
        {
            try
            {
                lock (_sessionLock)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            string sessionDirectory = Path.GetDirectoryName(SessionStatePath);
                            if (!string.IsNullOrWhiteSpace(sessionDirectory))
                                Directory.CreateDirectory(sessionDirectory);

                            SessionStateData session = new()
                            {
                                LastLoadedModel = lastLoadedModel,
                                LastLoadedConfig = lastLoadedConfig,
                                Binding = bindingManager?.CurrentBinding ?? "Right",
                                AimBindingSlots = CaptureBindingSlotsSnapshot(),
                                ActiveBindingSlot = Bools.ActiveBindingSlot,
                                ToggleState = BuildToggleSnapshot()
                            };

                            string json = JsonConvert.SerializeObject(session, Formatting.Indented);
                            File.WriteAllText(SessionStatePath, json);
                            break;
                        }
                        catch (IOException)
                        {
                            Thread.Sleep(50);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Session save failed: {ex.Message}");
            }
        }

        // PDW == PlayerDetectionWindow
        public Dictionary<string, dynamic> OverlayProperties = new()
        {
            { "FOV_Color", "#ff0000"},
            { "PDW_Size", 50 },
            { "PDW_CornerRadius", 0 },
            { "PDW_BorderThickness", 1 },
            { "PDW_Opacity", 1 }
        };

        private Thickness WinTooLeft = new(-1680, 0, 1680, 0);
        private Thickness WinVeryLeft = new(-1120, 0, 1120, 0);
        private Thickness WinLeft = new(-560, 0, 560, 0);

        private Thickness WinCenter = new(0, 0, 0, 0);

        private Thickness WinRight = new(560, 0, -560, 0);
        private Thickness WinVeryRight = new(1120, 0, -1120, 0);
        private Thickness WinTooRight = new(1680, 0, -1680, 0);
        private Thickness WinFarLeft = new(-2240, 0, 2240, 0);
        private Thickness WinFarRight = new(2240, 0, -2240, 0);

        public MainWindow()
        {
            InitializeComponent();
            this.Title = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);

            // Check to see if certain items are installed
            RequirementsManager RM = new();
            if (!RM.IsVCRedistInstalled())
            {
                MessageBox.Show("Visual C++ Redistributables x64 are not installed on this device, please install them before using DustyAim to avoid issues.", "Load Error");
                //Process.Start("https://aka.ms/vs/17/release/vc_redist.x64.exe");
                //Application.Current.Shutdown();
            }

            // Check for required folders
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] dirs = { "bin", "bin/models", "bin/images", "bin/configs", "bin/recoil" };

            try
            {
                foreach (string dir in dirs)
                {
                    string fullPath = Path.Combine(baseDir, dir);
                    if (!Directory.Exists(fullPath))
                    {
                        // Create the directory
                        Directory.CreateDirectory(fullPath);
                    }
                }
                try
                {
                    DpiScale dpi = VisualTreeHelper.GetDpi(this);
                    AIModel.DpiScaleX = dpi.DpiScaleX;
                    AIModel.DpiScaleY = dpi.DpiScaleY;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error initializing AI model: {ex.Message}", "Model Initialization Error");
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating a required directory: {ex}");
                Application.Current.Shutdown(); // We don't want to continue running without that folder.
            }

            // Setup key/mouse hook — 3 configurable binding slots
            _bindingManagers[0] = new InputBindingManager();
            _bindingManagers[0].SetupDefault("Right");
            _bindingManagers[1] = new InputBindingManager();
            _bindingManagers[1].SetupDefault("None");
            _bindingManagers[2] = new InputBindingManager();
            _bindingManagers[2].SetupDefault("None");
            _patternCycleBindingManager = new InputBindingManager();
            _patternCycleBindingManager.SetupDefault("F11");
            _patternCycleBindingManager.OnBindingPressed += _ =>
            {
                CycleRecoilPattern();
            };
            _recoilRecordingBindingManager = new InputBindingManager();
            _recoilRecordingBindingManager.SetupDefault("F12");
            _recoilRecordingBindingManager.OnBindingPressed += _ =>
            {
                if (_isRecoilRecordingEnabled)
                    ToggleRecoilRecording();
            };

            for (int _slot = 0; _slot < 3; _slot++)
            {
                int slotIndex = _slot;
                var mgr = _bindingManagers[slotIndex];
                mgr.OnBindingPressed += (binding) =>
                {
                    if (binding == "None") return;
                    Bools.ActiveBindingSlot = slotIndex;
                    if (Bools.AimHoldMode == "Toggle")
                        IsHolding_Binding = !IsHolding_Binding;
                    else
                        IsHolding_Binding = true;
                };
                mgr.OnBindingReleased += (binding) =>
                {
                    if (binding == "None") return;
                    if (Bools.AimHoldMode != "Toggle")
                    {
                        // Only release if NO other slot is still physically held
                        bool anyHeld = false;
                        foreach (var m in _bindingManagers)
                            if (m != null && m.IsBindingHeld) { anyHeld = true; break; }
                        if (!anyHeld) IsHolding_Binding = false;
                    }
                };
                mgr.OnBindingSet += (binding) => { SaveSessionState(); };
            }
            Bools.AimBindingSlots = new[]
            {
                _bindingManagers[0].CurrentBinding ?? "Right",
                _bindingManagers[1].CurrentBinding ?? "None",
                _bindingManagers[2].CurrentBinding ?? "None"
            };

            // Setup F10 minimize hook
            var globalHook = Gma.System.MouseKeyHook.Hook.GlobalEvents();
            globalHook.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == System.Windows.Forms.Keys.F10)
                {
                    if (this.WindowState == WindowState.Minimized)
                    {
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                    }
                    else
                    {
                        this.WindowState = WindowState.Minimized;
                    }
                }
            };

            // Load UI
            InitializeMenuPositions();
            //LoadAimMenu();
            //LoadTriggerMenu();
            //LoadSettingsMenu();
            ReloadMenu();
            ApplyMenuAccentColor(GetSettingString("GUI_AccentColor", DefaultMenuAccentColor));
            InitializeFileWatcher();
            InitializeConfigWatcher();

            // Load PredictionManager
            predictionManager = new PredictionManager();

            // Load all models into listbox
            LoadModelsIntoListBox();
            LoadConfigsIntoListBox();

            SelectorListBox.SelectionChanged += new SelectionChangedEventHandler(SelectorListBox_SelectionChanged);
            ConfigSelectorListBox.SelectionChanged += new SelectionChangedEventHandler(ConfigSelectorListBox_SelectionChanged);

            // Create FOV Overlay
            FOVOverlay = new OverlayWindow();
            FOVOverlay.Hide();
            FOVOverlay.FovSize = GetSettingInt("FOV_Size", 320);
            AwfulPropertyChanger.PostNewFOVSize();
            AwfulPropertyChanger.PostTravellingFOV(false);

            // Create Current Detected Player Overlay
            DetectedPlayerOverlay = new PlayerDetectionWindow();
            DetectedPlayerOverlay.Hide();
            SecondaryWindows.HudOverlay.OnPositionSaved = (x, y) =>
            {
                aimmySettings["MiniHud_X"] = x;
                aimmySettings["MiniHud_Y"] = y;
            };
            SecondaryWindows.HudOverlay.CustomX = GetSettingDouble("MiniHud_X", -1);
            SecondaryWindows.HudOverlay.CustomY = GetSettingDouble("MiniHud_Y", -1);
            SecondaryWindows.HudOverlay.CustomOpacity = GetSettingDouble("MiniHud_Opacity", 1.0);
            SecondaryWindows.HudOverlay.CustomScale = GetSettingDouble("MiniHud_Scale", 1.0);
            _hudOverlay = new SecondaryWindows.HudOverlay();
            _hudOverlay.SetUnlocked(GetSettingBool("MiniHud_UnlockPosition", false));
            _hudOverlay.Hide();

            // Create Debug Overlay
            debugOverlay = new Visualization.DebugOverlay();
            debugOverlay.Hide();
            ConfigureSaveFrameAction();

            // Start the loops
            Task.Run(() => StartModelCaptureLoop());
            Task.Run(() => StartRapidFireLoop());
            Task.Run(() => StartRecoilRecordingLoop());
        }

        private readonly List<DownloadItem> AvailableModels = new();
        private readonly List<DownloadItem> AvailableConfigs = new();

        private static readonly (string Owner, string Repo, string Branch)[] DownloadSources = new[]
        {
            ("Babyhamsta", "Aimmy", "Aimmy-V1"),
            ("whoswhip", "aimmy-models", "main")
        };

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadOverlayPropertiesAsync("bin/Overlay.cfg");
            await LoadConfigAsync("bin/configs/Default.cfg");
            await RestoreSessionStateAsync();

            // Rebuild menu with correct saved numbers, then re-apply toggle states on top
            ReloadMenu();
            if (File.Exists(SessionStatePath))
            {
                try
                {
                    string json = ReadSessionStateSafe();
                    var session = JsonConvert.DeserializeObject<SessionStateData>(json);
                    if (session?.ToggleState != null)
                        ApplyToggleSnapshot(session.ToggleState);
                }
                catch (Exception ex)
                {
                    Log($"Toggle restore failed: {ex.Message}");
                }
            }

            try
            {
                await PopulateDownloadSourcesAsync();
                LoadStoreMenu();
            }
            catch
            {
                MessageBox.Show("Github is irretrieveable right now, the Downloadable Model menu will not work right now, sorry!");
            }
            System.IO.File.WriteAllText("startup.log", "Window_Loaded completed successfully.");
        }

        private async Task PopulateDownloadSourcesAsync()
        {
            var tasks = new List<Task>();
            foreach (var source in DownloadSources)
            {
                tasks.Add(RetrieveAndAddFilesAsync(source.Owner, source.Repo, source.Branch, "models", AvailableModels));
                tasks.Add(RetrieveAndAddFilesAsync(source.Owner, source.Repo, source.Branch, "configs", AvailableConfigs));
            }
            await Task.WhenAll(tasks);
        }

        private async Task RetrieveAndAddFilesAsync(string owner, string repo, string branch, string repositoryPath, List<DownloadItem> availableFiles)
        {
            IEnumerable<DownloadItem> results = await RetrieveGithubFiles.ListContents(owner, repo, repositoryPath, branch);

            foreach (var file in results)
            {
                if (availableFiles.Any(existing => existing.SourceKey == file.SourceKey))
                    continue;

                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", repositoryPath, file.Name);
                if (File.Exists(localPath))
                    continue;

                availableFiles.Add(file);
            }
        }

        #region Mouse Movement / Clicking Handler

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private static Random MouseRandom = new();
        private static readonly Dictionary<string, System.Windows.Forms.Keys> MouseBindingToKey = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Left", System.Windows.Forms.Keys.LButton },
            { "Right", System.Windows.Forms.Keys.RButton },
            { "Middle", System.Windows.Forms.Keys.MButton },
            { "XButton1", System.Windows.Forms.Keys.XButton1 },
            { "XButton2", System.Windows.Forms.Keys.XButton2 },
            { "LButton", System.Windows.Forms.Keys.LButton },
            { "RButton", System.Windows.Forms.Keys.RButton },
            { "MButton", System.Windows.Forms.Keys.MButton }
        };

        private bool IsBindingCurrentlyHeld()
        {
            string binding = bindingManager?.CurrentBinding;
            if (string.IsNullOrWhiteSpace(binding))
                return IsHolding_Binding;

            if (MouseBindingToKey.TryGetValue(binding, out var mouseKey))
            {
                bool held = (GetAsyncKeyState((int)mouseKey) & 0x8000) != 0;
                if (!held) IsHolding_Binding = false;
                return held;
            }

            if (Enum.TryParse(binding, true, out System.Windows.Forms.Keys keyCode))
            {
                bool held = (GetAsyncKeyState((int)keyCode) & 0x8000) != 0;
                if (!held) IsHolding_Binding = false;
                return held;
            }

            return IsHolding_Binding;
        }

        private bool SendMouseInputSafe(uint flags, int dx = 0, int dy = 0, uint mouseData = 0)
        {
            if (Bools.UseHardwareMouse && HardwareMouse.IsConnected)
            {
                if ((flags & MOUSEEVENTF_MOVE) != 0) HardwareMouse.Move(dx, dy);
                if ((flags & MOUSEEVENTF_LEFTDOWN) != 0) HardwareMouse.PressLeft();
                if ((flags & MOUSEEVENTF_LEFTUP) != 0) HardwareMouse.ReleaseLeft();
                if ((flags & MOUSEEVENTF_RIGHTDOWN) != 0) HardwareMouse.PressRight();
                if ((flags & MOUSEEVENTF_RIGHTUP) != 0) HardwareMouse.ReleaseRight();
                return true;
            }

            INPUT[] inputs = new INPUT[1];
            inputs[0] = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = mouseData,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            uint sent = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent == 1)
                return true;

            int lastError = Marshal.GetLastWin32Error();
            DateTime now = DateTime.UtcNow;
            if (lastError != _lastInputInjectionErrorCode || (now - _lastInputInjectionErrorLog).TotalMilliseconds >= 1000)
            {
                _lastInputInjectionErrorCode = lastError;
                _lastInputInjectionErrorLog = now;
                Log($"SendInput failed. Win32={lastError}. Falling back to mouse_event.");
            }

            // Fallback path for environments where SendInput intermittently fails.
            mouse_event(flags, dx, dy, mouseData, 0);
            return false;
        }

        private static Point CubicBezier(Point start, Point end, Point control1, Point control2, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            double x = uuu * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + ttt * end.X;
            double y = uuu * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + ttt * end.Y;

            return new Point((int)x, (int)y);
        }

        private async Task DoTriggerClick()
        {
            if (!await _triggerClickGate.WaitAsync(0))
                return;

            try
            {
                int TimeSinceLastClick = (int)(DateTime.Now - LastClickTime).TotalMilliseconds;
                int Trigger_Delay_Milliseconds = (int)(GetSettingDouble("Trigger_Delay", 0.1) * 1000);

                if (TimeSinceLastClick >= Trigger_Delay_Milliseconds || LastClickTime == DateTime.MinValue)
                {
                    SendMouseInputSafe(MOUSEEVENTF_LEFTDOWN);
                    await Task.Delay(20);
                    SendMouseInputSafe(MOUSEEVENTF_LEFTUP);
                    LastClickTime = DateTime.Now;
                }
            }
            finally
            {
                _triggerClickGate.Release();
            }
        }

        private void MoveCrosshair(double targetOffsetLimitX, double targetOffsetLimitY)
        {
            var smoothedDelta = GetSmoothedAimDelta(targetOffsetLimitX, targetOffsetLimitY);

            double targetX = smoothedDelta.X;
            double targetY = smoothedDelta.Y;

            if (_logCounter % 50 == 0)
                Log($"Movement Diagnostic: Offset=({targetOffsetLimitX:F1},{targetOffsetLimitY:F1}) Smoothed=({targetX:F1},{targetY:F1}) Screen={ScreenWidth}x{ScreenHeight}");

            double distance = Math.Sqrt(targetX * targetX + targetY * targetY);
            double deadzone = Math.Clamp(GetSettingDouble("Aim_Deadzone", 1.5), 0.0, 10.0);
            if (distance <= deadzone)
            {
                _moveRemainderX = 0;
                _moveRemainderY = 0;
                return;
            }

            double alpha = Math.Clamp(GetSettingDouble("Mouse_Sens", 0.80), 0.01, 0.99);
            double baseMoveFactor = Math.Clamp(1.0 - alpha, 0.02, 0.85);
            double distanceFactor = Lerp(0.55, 1.0, Math.Clamp(distance / 180.0, 0.0, 1.0));

            double moveX = targetX * baseMoveFactor * distanceFactor;
            double moveY = targetY * baseMoveFactor * distanceFactor;

            int mouseJitter = Math.Clamp(GetSettingInt("Mouse_Jitter", 4), 0, 15);
            if (mouseJitter > 0)
            {
                double jitterStrength = mouseJitter * 0.15 * Math.Clamp(distance / 240.0, 0.0, 1.0);
                moveX += (MouseRandom.NextDouble() * 2.0 - 1.0) * jitterStrength;
                moveY += (MouseRandom.NextDouble() * 2.0 - 1.0) * jitterStrength;
            }

            moveX += _moveRemainderX;
            moveY += _moveRemainderY;

            double moveLength = Math.Sqrt(moveX * moveX + moveY * moveY);
            double maxStep = Math.Clamp(GetSettingDouble("Aim_MaxStep", 14.0), 2.0, 64.0);
            if (moveLength > maxStep)
            {
                double clampScale = maxStep / moveLength;
                moveX *= clampScale;
                moveY *= clampScale;
                moveLength = maxStep;
            }

            double maxSingleEventDistance = Math.Max(1.0, Math.Min(6.0, maxStep * 0.45));
            int steps = (int)Math.Max(1.0, Math.Ceiling(moveLength / maxSingleEventDistance));

            double stepX = moveX / steps;
            double stepY = moveY / steps;

            double accumX = 0;
            double accumY = 0;

            for (int i = 0; i < steps; i++)
            {
                accumX += stepX;
                accumY += stepY;
                
                int iX = (int)Math.Round(accumX);
                int iY = (int)Math.Round(accumY);
                
                accumX -= iX;
                accumY -= iY;
                
                if (iX != 0 || iY != 0)
                {
                    SendMouseInputSafe(MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE, iX, iY);
                }
            }

            _moveRemainderX = accumX;
            _moveRemainderY = accumY;
        }

        private static bool IsLeftMouseButtonHeld()
        {
            return (GetAsyncKeyState((int)System.Windows.Forms.Keys.LButton) & 0x8000) != 0;
        }

        private static bool IsRightMouseButtonHeld()
        {
            return (GetAsyncKeyState((int)System.Windows.Forms.Keys.RButton) & 0x8000) != 0;
        }

        private void ResetRecoilPlaybackState()
        {
            _recoilPatternIndex = 0;
            _nextRecoilStepUtc = DateTime.MinValue;
            _wasLeftMouseDown = false;
            _recoilRemainderY = 0;
        }

        private void ResetRapidFireState()
        {
            _nextRapidFireClickUtc = DateTime.MinValue;
            _wasRapidFireLeftMouseDown = false;
        }

        private static bool TryParseDoubleFlexible(string value, out double result)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private static bool TryGetTokenDouble(JToken token, out double value)
        {
            if (token == null)
            {
                value = 0;
                return false;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return true;
            }

            if (token.Type == JTokenType.String)
                return TryParseDoubleFlexible(token.Value<string>(), out value);

            value = 0;
            return false;
        }

        private static bool TryGetObjectDouble(JObject obj, out double value, params string[] candidateKeys)
        {
            foreach (string key in candidateKeys)
            {
                if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token)
                    && TryGetTokenDouble(token, out value))
                {
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static int NormalizeRecoilDelay(double rawDelayMs, int fallbackDelayMs)
        {
            int fallback = Math.Clamp(fallbackDelayMs, 1, 500);
            if (double.IsNaN(rawDelayMs) || double.IsInfinity(rawDelayMs))
                return fallback;

            return Math.Clamp((int)Math.Round(rawDelayMs), 1, 500);
        }

        private static List<RecoilPatternStep> ParseJsonRecoilPattern(string json, int fallbackDelayMs)
        {
            List<RecoilPatternStep> steps = new();
            JToken root = JToken.Parse(json);

            JArray stepArray = root as JArray;
            if (stepArray == null && root is JObject rootObject)
            {
                if (rootObject.TryGetValue("steps", StringComparison.OrdinalIgnoreCase, out JToken stepsToken)
                    && stepsToken is JArray nestedArray)
                {
                    stepArray = nestedArray;
                }
            }

            if (stepArray == null)
                return steps;

            foreach (JToken token in stepArray)
            {
                bool validStep = false;
                double dx = 0;
                double dy = 0;
                int delayMs = fallbackDelayMs;

                if (token is JObject stepObject)
                {
                    bool hasDx = TryGetObjectDouble(stepObject, out dx, "dx", "x", "moveX", "horizontal", "deltaX");
                    bool hasDy = TryGetObjectDouble(stepObject, out dy, "dy", "y", "moveY", "vertical", "deltaY");
                    if (hasDx && hasDy)
                    {
                        if (TryGetObjectDouble(stepObject, out double rawDelay, "delay", "delayMs", "ms", "time", "wait"))
                            delayMs = NormalizeRecoilDelay(rawDelay, fallbackDelayMs);
                        validStep = true;
                    }
                }
                else if (token is JArray stepValues && stepValues.Count >= 2)
                {
                    if (TryGetTokenDouble(stepValues[0], out dx) && TryGetTokenDouble(stepValues[1], out dy))
                    {
                        if (stepValues.Count >= 3 && TryGetTokenDouble(stepValues[2], out double rawDelay))
                            delayMs = NormalizeRecoilDelay(rawDelay, fallbackDelayMs);
                        validStep = true;
                    }
                }

                if (!validStep)
                    continue;

                steps.Add(new RecoilPatternStep
                {
                    Dx = dx,
                    Dy = dy,
                    DelayMs = delayMs
                });
            }

            return steps;
        }

        private static List<RecoilPatternStep> ParseTextRecoilPattern(IEnumerable<string> lines, int fallbackDelayMs)
        {
            List<RecoilPatternStep> steps = new();
            char[] separators = { ',', ';', '\t', ' ' };

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (!TryParseDoubleFlexible(parts[0], out double dx) || !TryParseDoubleFlexible(parts[1], out double dy))
                    continue;

                int delayMs = fallbackDelayMs;
                if (parts.Length >= 3 && TryParseDoubleFlexible(parts[2], out double rawDelay))
                    delayMs = NormalizeRecoilDelay(rawDelay, fallbackDelayMs);

                steps.Add(new RecoilPatternStep
                {
                    Dx = dx,
                    Dy = dy,
                    DelayMs = delayMs
                });
            }

            return steps;
        }

        private List<RecoilPatternStep> ParseImportedPattern(string text)
        {
            List<RecoilPatternStep> steps = new();
            // Match things like [0, 0], [-2, 5], 10, 20, etc.
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int defaultDelay = GetSettingInt("Recoil_DefaultStepDelay", 100);

            foreach (string line in lines)
            {
                // Clean up the line: remove brackets, spaces, handle comments after ';'
                string content = line;
                int commentIndex = content.IndexOf(';');
                if (commentIndex >= 0) content = content.Substring(0, commentIndex);

                string clean = content.Trim().Replace("[", "").Replace("]", "").Replace("(", "").Replace(")", "").Replace("{", "").Replace("}", "");
                if (string.IsNullOrWhiteSpace(clean)) continue;

                string[] parts = clean.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    if (TryParseDoubleFlexible(parts[0], out double dx) && TryParseDoubleFlexible(parts[1], out double dy))
                    {
                        int delay = defaultDelay;
                        if (parts.Length >= 3 && TryParseDoubleFlexible(parts[2], out double rawDelay))
                            delay = NormalizeRecoilDelay(rawDelay, defaultDelay);

                        steps.Add(new RecoilPatternStep { Dx = dx, Dy = dy, DelayMs = delay });
                    }
                }
            }
            return steps;
        }

        private string ResolveRecoilPatternPath(string rawPath)
        {
            string trimmed = rawPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            string basePath = Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(RecoilFolderPath, trimmed.StartsWith("bin/recoil/") ? trimmed.Substring(11) : trimmed);

            return Path.GetFullPath(basePath);
        }

        private bool TryLoadRecoilPatternFromFile(string rawPath, out string failureReason)
        {
            failureReason = string.Empty;
            string resolvedPath;
            try
            {
                resolvedPath = ResolveRecoilPatternPath(rawPath);
            }
            catch (Exception ex)
            {
                failureReason = $"Invalid path: {ex.Message}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                failureReason = "No recoil file selected.";
                return false;
            }

            if (!File.Exists(resolvedPath))
            {
                failureReason = "The selected recoil file does not exist.";
                return false;
            }

            List<RecoilPatternStep> loadedPattern;
            int fallbackDelayMs = Math.Clamp(GetSettingInt("Recoil_DefaultStepDelay", 16), 1, 250);

            try
            {
                string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
                if (extension == ".json")
                {
                    string json = File.ReadAllText(resolvedPath);
                    loadedPattern = ParseJsonRecoilPattern(json, fallbackDelayMs);

                    // Restore settings if present
                    try
                    {
                        JObject root = JObject.Parse(json);
                        if (root.TryGetValue("settings", StringComparison.OrdinalIgnoreCase, out JToken settingsToken) && settingsToken is JObject settingsObj)
                        {
                            foreach (var property in settingsObj.Properties())
                            {
                                if (aimmySettings.ContainsKey(property.Name))
                                {
                                    aimmySettings[property.Name] = property.Value.ToObject<object>();
                                }
                            }
                            
                            // Refresh UI to show the new settings
                            Dispatcher.Invoke(ReloadMenu);
                        }
                    }
                    catch { /* Ignore errors in optional settings block */ }
                }
                else
                {
                    string[] lines = File.ReadAllLines(resolvedPath);
                    loadedPattern = ParseTextRecoilPattern(lines, fallbackDelayMs);
                }
            }
            catch (Exception ex)
            {
                failureReason = $"Failed to parse recoil pattern: {ex.Message}";
                return false;
            }

            if (loadedPattern.Count == 0)
            {
                failureReason = "No valid recoil steps were found in the file.";
                return false;
            }

            lock (_recoilPatternLock)
            {
                _recoilPatternSteps = loadedPattern;
                _loadedRecoilPatternPath = resolvedPath;
            }

            aimmySettings["Recoil_PatternPath"] = resolvedPath;
            ResetRecoilPlaybackState();
            UpdateHudOverlayState();
            RefreshRecoilPatternGraph();
            return true;
        }

        private bool TryLoadConfiguredRecoilPattern(out string failureReason)
        {
            failureReason = string.Empty;
            string configuredPath = aimmySettings.TryGetValue("Recoil_PatternPath", out var pathValue)
                ? pathValue?.ToString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                lock (_recoilPatternLock)
                {
                    _recoilPatternSteps = new List<RecoilPatternStep>();
                    _loadedRecoilPatternPath = string.Empty;
                }

                ResetRecoilPlaybackState();
                UpdateHudOverlayState();
                RefreshRecoilPatternGraph();
                failureReason = "No recoil file loaded.";
                return false;
            }

            return TryLoadRecoilPatternFromFile(configuredPath, out failureReason);
        }

        private string BuildRecoilPatternStatusText()
        {
            int stepCount;
            string path;
            lock (_recoilPatternLock)
            {
                stepCount = _recoilPatternSteps.Count;
                path = _loadedRecoilPatternPath;
            }

            if (stepCount <= 0 || string.IsNullOrWhiteSpace(path))
                return "Pattern: none loaded.";

            return $"Pattern: {Path.GetFileName(path)} ({stepCount} steps)";
        }

        private List<RecoilPatternStep> GetLoadedRecoilPatternSnapshot()
        {
            lock (_recoilPatternLock)
            {
                return _recoilPatternSteps?.Select(step => new RecoilPatternStep
                {
                    Dx = step.Dx,
                    Dy = step.Dy,
                    DelayMs = step.DelayMs
                }).ToList() ?? new List<RecoilPatternStep>();
            }
        }

        private string[] CaptureBindingSlotsSnapshot()
        {
            string[] snapshot = new string[_bindingManagers.Length];
            for (int i = 0; i < _bindingManagers.Length; i++)
            {
                snapshot[i] = _bindingManagers[i]?.CurrentBinding ?? (i == 0 ? "Right" : "None");
            }

            Bools.AimBindingSlots = snapshot.ToArray();
            return snapshot;
        }

        private void RefreshRecoilPatternGraph()
        {
            if (_recoilPatternGraph == null)
                return;

            Action refresh = () => _recoilPatternGraph.SetPattern(GetLoadedRecoilPatternSnapshot());
            if (Dispatcher.CheckAccess())
                refresh();
            else
                Dispatcher.BeginInvoke(refresh);
        }

        private void UpdateHudOverlayState()
        {
            SecondaryWindows.HudOverlay.CustomOpacity = GetSettingDouble("MiniHud_Opacity", 1.0);
            SecondaryWindows.HudOverlay.CustomScale = GetSettingDouble("MiniHud_Scale", 1.0);
            if (_hudOverlay != null) {
               _hudOverlay.SetUnlocked(GetSettingBool("MiniHud_UnlockPosition", false));
            }
            bool aimUnlocked = IsAimbotEnabled() && (!IsAimHoldRequired() || IsHolding_Binding);
            SecondaryWindows.HudOverlay.ModeText = IsAimHoldRequired()
                ? $"Aim: {Bools.AimHoldMode ?? "Hold"}"
                : "Aim: Toggle";
            SecondaryWindows.HudOverlay.PatternText = string.IsNullOrWhiteSpace(_loadedRecoilPatternPath)
                ? "none"
                : Path.GetFileName(_loadedRecoilPatternPath);
            SecondaryWindows.HudOverlay.AimStatusText = aimUnlocked ? "Aim unlocked" : "Aim locked";
            SecondaryWindows.HudOverlay.AimStatusBrush = aimUnlocked ? Brushes.LimeGreen : Brushes.IndianRed;
        }

        private bool RegisterLoopFailure(string loopName, Exception ex, ref int errorCount, ref DateTime firstErrorUtc)
        {
            DateTime now = DateTime.UtcNow;
            if (errorCount == 0 || (now - firstErrorUtc).TotalSeconds > 5)
            {
                firstErrorUtc = now;
                errorCount = 0;
            }

            errorCount++;
            Log($"{loopName} Loop ERROR: {ex.Message}");
            Visualization.DebugOverlay.AddLog($"{loopName} loop error: {ex.Message}");

            if (errorCount >= 10 && (now - firstErrorUtc).TotalSeconds <= 5)
            {
                Log($"{loopName} Loop CRITICAL: {errorCount} consecutive errors within 5 seconds. Stopping loop.");
                Visualization.DebugOverlay.AddLog($"{loopName} loop critical: stopping after repeated errors.");
                return true;
            }

            return false;
        }

        private void ResetLoopFailureState(ref int errorCount, ref DateTime firstErrorUtc)
        {
            errorCount = 0;
            firstErrorUtc = DateTime.MinValue;
        }

        private void CycleRecoilPattern()
        {
            try
            {
                string[] files = Directory.GetFiles(RecoilFolderPath)
                    .Where(path =>
                    {
                        string ext = Path.GetExtension(path);
                        return ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                {
                    Log("CycleRecoilPattern: no recoil files found.");
                    Dispatcher.BeginInvoke(new Action(() =>
                        MessageBox.Show("No recoil pattern files were found in the recoil folder.", "Recoil Control")));
                    return;
                }

                string currentPath = string.Empty;
                lock (_recoilPatternLock)
                {
                    currentPath = _loadedRecoilPatternPath;
                }

                int currentIndex = Array.FindIndex(files, path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
                int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % files.Length;
                string nextPath = files[nextIndex];

                if (!TryLoadRecoilPatternFromFile(nextPath, out string error))
                {
                    Log($"CycleRecoilPattern failed: {error}");
                    Dispatcher.BeginInvoke(new Action(() =>
                        MessageBox.Show(error, "Recoil Pattern Error")));
                    return;
                }

                RefreshRecoilPatternGraph();
                UpdateHudOverlayState();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_recoilStatusTextBlock != null)
                        _recoilStatusTextBlock.Text = BuildRecoilPatternStatusText();
                }));
            }
            catch (Exception ex)
            {
                Log($"CycleRecoilPattern error: {ex.Message}");
            }
        }

        private void TickRecoilControl()
        {
            if (!IsRecoilEnabled())
            {
                ResetRecoilPlaybackState();
                return;
            }

            if (IsRecoilAdsOnlyEnabled() && !IsRightMouseButtonHeld())
            {
                ResetRecoilPlaybackState();
                return;
            }

            List<RecoilPatternStep> pattern;
            lock (_recoilPatternLock)
            {
                pattern = _recoilPatternSteps;
            }

            if (pattern == null || pattern.Count == 0)
            {
                ResetRecoilPlaybackState();
                return;
            }

            bool isLeftDown = IsLeftMouseButtonHeld();
            if (!isLeftDown)
            {
                ResetRecoilPlaybackState();
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!_wasLeftMouseDown)
            {
                _recoilPatternIndex = 0;
                _nextRecoilStepUtc = now;
                _recoilRemainderX = 0;
                _recoilRemainderY = 0;
            }
            _wasLeftMouseDown = true;

            if (now < _nextRecoilStepUtc)
                return;

            bool loopPattern = IsRecoilLoopEnabled();
            if (_recoilPatternIndex >= pattern.Count)
            {
                if (!loopPattern)
                    return;

                _recoilPatternIndex = 0;
            }

            RecoilPatternStep currentStep = pattern[_recoilPatternIndex];
            double recoilScale = Math.Clamp(GetSettingDouble("Recoil_Scale", 1.0), 1.0, 10.0);
            double scaledX = (-currentStep.Dx * recoilScale) + _recoilRemainderX;
            double scaledY = (-currentStep.Dy * recoilScale) + _recoilRemainderY;

            int moveX = (int)Math.Round(scaledX);
            int moveY = (int)Math.Round(scaledY);

            _recoilRemainderX = scaledX - moveX;
            _recoilRemainderY = scaledY - moveY;

            if (moveX != 0 || moveY != 0)
                SendMouseInputSafe(MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE, moveX, moveY);

            _recoilPatternIndex++;

            int fallbackDelayMs = Math.Clamp(GetSettingInt("Recoil_DefaultStepDelay", 16), 1, 250);
            int rawDelayMs = currentStep.DelayMs > 0 ? currentStep.DelayMs : fallbackDelayMs;
            double speedMultiplier = Math.Clamp(GetSettingDouble("Recoil_SpeedMultiplier", 1.0), 0.1, 4.0);
            int adjustedDelayMs = Math.Max(1, (int)Math.Round(rawDelayMs / speedMultiplier));
            _nextRecoilStepUtc = now.AddMilliseconds(adjustedDelayMs);
        }

        private async Task TickRapidFireControl()
        {
            if (!IsRapidFireEnabled())
            {
                ResetRapidFireState();
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!IsLeftMouseButtonHeld())
            {
                ResetRapidFireState();
                return;
            }

            if (!_wasRapidFireLeftMouseDown)
                _nextRapidFireClickUtc = now;

            _wasRapidFireLeftMouseDown = true;

            if (now < _nextRapidFireClickUtc)
                return;

            if (!await _rapidFireClickGate.WaitAsync(0))
                return;

            try
            {
                int delayMs = Math.Clamp(GetSettingInt("Recoil_RapidFireDelayMs", 90), 25, 250);
                SendMouseInputSafe(MOUSEEVENTF_LEFTDOWN);
                await Task.Delay(40);
                SendMouseInputSafe(MOUSEEVENTF_LEFTUP);
                _nextRapidFireClickUtc = now.AddMilliseconds(delayMs);
            }
            finally
            {
                _rapidFireClickGate.Release();
            }
        }

        private async Task StartRapidFireLoop()
        {
            _rapidFireLoopCts = new CancellationTokenSource();

            while (!_rapidFireLoopCts.Token.IsCancellationRequested)
            {
                try
                {
                    if (!IsRapidFireEnabled())
                    {
                        ResetRapidFireState();
                        await Task.Delay(25, _rapidFireLoopCts.Token);
                        ResetLoopFailureState(ref _rapidFireLoopErrorCount, ref _rapidFireLoopErrorWindowStartUtc);
                        continue;
                    }

                    await TickRapidFireControl();
                    UpdateHudOverlayState();
                    ResetLoopFailureState(ref _rapidFireLoopErrorCount, ref _rapidFireLoopErrorWindowStartUtc);
                    await Task.Delay(1, _rapidFireLoopCts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    bool shouldStop = RegisterLoopFailure("RapidFire", ex, ref _rapidFireLoopErrorCount, ref _rapidFireLoopErrorWindowStartUtc);
                    if (shouldStop)
                        break;

                    await Task.Delay(500);
                }
            }
        }

        #endregion Mouse Movement / Clicking Handler

        #region Aim Aligner Main and Loop

        public async Task ModelCapture(bool TriggerOnly = false)
        {
            _logCounter++;
            AIModel activeModel;
            AIModel.Prediction closestPrediction;
            System.Drawing.Rectangle physicalDetectionBox;
            System.Drawing.Rectangle captureScreenBounds = System.Drawing.Rectangle.Empty;
            System.Drawing.Point cursorPos = System.Drawing.Point.Empty;

            await _modelAccessGate.WaitAsync();
            try
            {
                activeModel = _onnxModel;
                if (activeModel == null)
                {
                    if (_logCounter % 200 == 0) Visualization.DebugOverlay.AddLog("AI: Skip capture (No model loaded)");
                    return;
                }

                cursorPos = System.Windows.Forms.Cursor.Position;
                captureScreenBounds = System.Windows.Forms.Screen.FromPoint(cursorPos).Bounds;
                activeModel.CaptureScreenBounds = captureScreenBounds;
                physicalDetectionBox = activeModel.GetPhysicalCaptureBox();
                activeModel.CurrentCursorOffset = new System.Drawing.Point(
                    cursorPos.X - physicalDetectionBox.X,
                    cursorPos.Y - physicalDetectionBox.Y);
                closestPrediction = await activeModel.GetClosestPredictionToCenterAsync();
            }
            finally
            {
                _modelAccessGate.Release();
            }
             
            this.Dispatcher.Invoke(() =>
            {
                if (captureScreenBounds.Width <= 0 || captureScreenBounds.Height <= 0)
                    captureScreenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

                ScreenWidth = captureScreenBounds.Width;
                ScreenHeight = captureScreenBounds.Height;
                int minScreenX = captureScreenBounds.Left;
                int minScreenY = captureScreenBounds.Top;
                int maxScreenX = captureScreenBounds.Right - 1;
                int maxScreenY = captureScreenBounds.Bottom - 1;

                if (closestPrediction == null)
                {
                    ResetAimMovementState();
                    UpdateHudOverlayState();
                    DetectedPlayerOverlay.DetectedPlayerFocus.Visibility = Visibility.Collapsed;
                    DetectedPlayerOverlay.UnfilteredPlayerFocus.Visibility = Visibility.Collapsed;
                    DetectedPlayerOverlay.PredictionFocus.Visibility = Visibility.Collapsed;
                    return;
                }

                double YOffset = GetSettingDouble("Y_Offset");
                double XOffset = GetSettingDouble("X_Offset");

                float mappedBoxX = physicalDetectionBox.X + closestPrediction.Rectangle.X;
                float mappedBoxY = physicalDetectionBox.Y + closestPrediction.Rectangle.Y;
                float mappedBoxWidth = Math.Max(1f, closestPrediction.Rectangle.Width);
                float mappedBoxHeight = Math.Max(1f, closestPrediction.Rectangle.Height);




                float anchorX = mappedBoxX + (mappedBoxWidth / 2.0f);
                float headRatio = (float)Math.Clamp(GetSettingDouble("Aim_HeadRatio", 0.15), 0.0, 1.0);
                float anchorY = mappedBoxY + (mappedBoxHeight * headRatio);

                int unfilteredX = Math.Clamp((int)anchorX, minScreenX, maxScreenX);
                int unfilteredY = Math.Clamp((int)anchorY, minScreenY, maxScreenY);
                int detectedX = Math.Clamp((int)(anchorX + XOffset), minScreenX, maxScreenX);
                int detectedY = Math.Clamp((int)(anchorY + YOffset), minScreenY, maxScreenY);

                double baseMarkerSize = Convert.ToDouble(OverlayProperties["PDW_Size"]);
                double minBoxSizeDip = Math.Max(6.0, baseMarkerSize * 0.25);

                System.Windows.Point unfilteredTopLeftDipPoint = DetectedPlayerOverlay.ScreenToWindow(new System.Windows.Point(mappedBoxX, mappedBoxY));
                System.Windows.Point unfilteredBottomRightDipPoint = DetectedPlayerOverlay.ScreenToWindow(new System.Windows.Point(mappedBoxX + mappedBoxWidth, mappedBoxY + mappedBoxHeight));

                double unfilteredLeftDip = Math.Min(unfilteredTopLeftDipPoint.X, unfilteredBottomRightDipPoint.X);
                double unfilteredTopDip = Math.Min(unfilteredTopLeftDipPoint.Y, unfilteredBottomRightDipPoint.Y);
                double unfilteredBoxWidthDip = Math.Abs(unfilteredBottomRightDipPoint.X - unfilteredTopLeftDipPoint.X);
                double unfilteredBoxHeightDip = Math.Abs(unfilteredBottomRightDipPoint.Y - unfilteredTopLeftDipPoint.Y);

                // Draw the red box around the actual detected rectangle.
                // Anchor offsets are for aiming and triggering only.
                System.Windows.Point detectedTopLeftDipPoint = DetectedPlayerOverlay.ScreenToWindow(new System.Windows.Point(mappedBoxX, mappedBoxY));
                System.Windows.Point detectedBottomRightDipPoint = DetectedPlayerOverlay.ScreenToWindow(new System.Windows.Point(mappedBoxX + mappedBoxWidth, mappedBoxY + mappedBoxHeight));

                double detectedLeftDip = Math.Min(detectedTopLeftDipPoint.X, detectedBottomRightDipPoint.X);
                double detectedTopDip = Math.Min(detectedTopLeftDipPoint.Y, detectedBottomRightDipPoint.Y);
                double detectedBoxWidthDip = Math.Abs(detectedBottomRightDipPoint.X - detectedTopLeftDipPoint.X);
                double detectedBoxHeightDip = Math.Abs(detectedBottomRightDipPoint.Y - detectedTopLeftDipPoint.Y);

                unfilteredBoxWidthDip = Math.Max(minBoxSizeDip, unfilteredBoxWidthDip);
                unfilteredBoxHeightDip = Math.Max(minBoxSizeDip, unfilteredBoxHeightDip);
                detectedBoxWidthDip = Math.Max(1.0, detectedBoxWidthDip);
                detectedBoxHeightDip = Math.Max(1.0, detectedBoxHeightDip);

                double overlayWidthDip = DetectedPlayerOverlay.ActualWidth > 0 ? DetectedPlayerOverlay.ActualWidth : DetectedPlayerOverlay.Width;
                double overlayHeightDip = DetectedPlayerOverlay.ActualHeight > 0 ? DetectedPlayerOverlay.ActualHeight : DetectedPlayerOverlay.Height;
                if (double.IsNaN(overlayWidthDip) || overlayWidthDip <= 0) overlayWidthDip = SystemParameters.VirtualScreenWidth;
                if (double.IsNaN(overlayHeightDip) || overlayHeightDip <= 0) overlayHeightDip = SystemParameters.VirtualScreenHeight;

                unfilteredLeftDip = Math.Clamp(unfilteredLeftDip, 0, Math.Max(0, overlayWidthDip - unfilteredBoxWidthDip));
                unfilteredTopDip = Math.Clamp(unfilteredTopDip, 0, Math.Max(0, overlayHeightDip - unfilteredBoxHeightDip));
                detectedLeftDip = Math.Clamp(detectedLeftDip, 0, Math.Max(0, overlayWidthDip - detectedBoxWidthDip));
                detectedTopDip = Math.Clamp(detectedTopDip, 0, Math.Max(0, overlayHeightDip - detectedBoxHeightDip));

                int physicalTargetX = detectedX;
                int physicalTargetY = detectedY;

                bool overlayActive = IsDetectionOverlayActive();
                EnsureDetectionOverlayVisibility(overlayActive);

                Detection detection = new()
                {
                    X = unfilteredX,
                    Y = unfilteredY,
                    Timestamp = DateTime.UtcNow
                };
                predictionManager.UpdateKalmanFilter(detection);
                predictionManager.PredictionStrength = Math.Clamp(GetSettingDouble("Aim_PredictionStrength", 1.0), 0.0, 1.0);
                var predictedPosition = predictionManager.GetEstimatedPosition();

                int predictedAimX = Math.Clamp((int)(predictedPosition.X + XOffset), minScreenX, maxScreenX);
                int predictedAimY = Math.Clamp((int)(predictedPosition.Y + YOffset), minScreenY, maxScreenY);

                bool bindingHeld = IsBindingCurrentlyHeld();
                bool aimHoldRequired = IsAimHoldRequired();
                bool isConstantOn = IsConstantTrackingEnabled();
                bool shouldMoveAim = IsAimbotEnabled() && (isConstantOn || (aimHoldRequired && bindingHeld));

                if (shouldMoveAim)
                {
                    double aimTargetX;
                    double aimTargetY;

                    if (toggleState["PredictionToggle"])
                    {
                        // Prediction still uses absolute screen coordinates for its own internal logic
                        aimTargetX = predictedAimX;
                        aimTargetY = predictedAimY;
                        
                        // We convert it back to relative for the smoothed move
                        MoveCrosshair(aimTargetX - cursorPos.X, aimTargetY - cursorPos.Y);
                    }
                    else
                    {
                        // DIRECT MODE: Calculate offsets relative to the CAPTURE FRAME CENTER
                        // This avoids absolute screen coordinates and Cursor.Position entirely
                        float localCenterX = closestPrediction.Rectangle.X + (closestPrediction.Rectangle.Width / 2.0f);
                        float localCenterY = closestPrediction.Rectangle.Y + (closestPrediction.Rectangle.Height / 2.0f);
                        float captureCenterX = physicalDetectionBox.Width / 2.0f;
                        float captureCenterY = physicalDetectionBox.Height / 2.0f;

                        aimTargetX = localCenterX - captureCenterX + XOffset;
                        aimTargetY = localCenterY - captureCenterY + YOffset;
                        
                        MoveCrosshair(aimTargetX, aimTargetY);
                    }
                }
                else
                {
                    ResetAimMovementState();
                }

                UpdateHudOverlayState();

                bool allowTrigger = !aimHoldRequired || bindingHeld;
                int crosshairX = cursorPos.X;
                int crosshairY = cursorPos.Y;
                // TRIGGER CONFIRMATION uses the same anchor point logic
                // Confirm trigger if cursor is within box OR near anchor point for extra precision
                bool crosshairInBox = closestPrediction != null &&
                    crosshairX >= mappedBoxX &&
                    crosshairX <= mappedBoxX + mappedBoxWidth &&
                    crosshairY >= mappedBoxY &&
                    crosshairY <= mappedBoxY + mappedBoxHeight;

                double triggerThreshold = 10.0; // pixels
                bool nearAnchor = Math.Abs(crosshairX - anchorX) < triggerThreshold && Math.Abs(crosshairY - anchorY) < triggerThreshold;

                bool triggerAllowed = allowTrigger && (crosshairInBox || nearAnchor);
                if ((IsTriggerEnabled() || TriggerOnly) && triggerAllowed)
                    _ = Task.Run(DoTriggerClick);

                if (overlayActive && Bools.ShowPrediction)
                {
                    System.Windows.Point predictionCenterDip = DetectedPlayerOverlay.ScreenToWindow(new System.Windows.Point(predictedPosition.X, predictedPosition.Y));
                    double predictionBoxWidthDip = minBoxSizeDip; double predictionBoxHeightDip = minBoxSizeDip;
                    double predictionLeftDip = predictionCenterDip.X - (predictionBoxWidthDip / 2.0);
                    double predictionTopDip = predictionCenterDip.Y - (predictionBoxHeightDip / 2.0);
                    predictionLeftDip = Math.Clamp(predictionLeftDip, 0, Math.Max(0, overlayWidthDip - predictionBoxWidthDip));
                    predictionTopDip = Math.Clamp(predictionTopDip, 0, Math.Max(0, overlayHeightDip - predictionBoxHeightDip));

                    DetectedPlayerOverlay.PredictionFocus.Visibility = Visibility.Visible;
                    DetectedPlayerOverlay.PredictionFocus.Width = predictionBoxWidthDip;
                    DetectedPlayerOverlay.PredictionFocus.Height = predictionBoxHeightDip;
                    DetectedPlayerOverlay.PredictionFocus.Margin = new Thickness(
                        predictionLeftDip,
                        predictionTopDip,
                        0, 0);
                }
                else
                {
                    DetectedPlayerOverlay.PredictionFocus.Visibility = Visibility.Collapsed;
                }

                if (overlayActive)
                {
                    if (Bools.ShowCurrentDetectedPlayer)
                    {
                        DetectedPlayerOverlay.DetectedPlayerFocus.Visibility = Visibility.Visible;
                        DetectedPlayerOverlay.DetectedPlayerFocus.Width = detectedBoxWidthDip;
                        DetectedPlayerOverlay.DetectedPlayerFocus.Height = detectedBoxHeightDip;
                        DetectedPlayerOverlay.DetectedPlayerFocus.Margin = new Thickness(
                            detectedLeftDip,
                            detectedTopDip,
                            0, 0);
                    }
                    else
                    {
                        DetectedPlayerOverlay.DetectedPlayerFocus.Visibility = Visibility.Collapsed;
                    }

                    if (Bools.ShowUnfilteredDetectedPlayer)
                    {
                        DetectedPlayerOverlay.UnfilteredPlayerFocus.Visibility = Visibility.Visible;
                        DetectedPlayerOverlay.UnfilteredPlayerFocus.Width = unfilteredBoxWidthDip;
                        DetectedPlayerOverlay.UnfilteredPlayerFocus.Height = unfilteredBoxHeightDip;
                        DetectedPlayerOverlay.UnfilteredPlayerFocus.Margin = new Thickness(
                            unfilteredLeftDip,
                            unfilteredTopDip,
                            0, 0);
                    }
                    else
                    {
                        DetectedPlayerOverlay.UnfilteredPlayerFocus.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    DetectedPlayerOverlay.DetectedPlayerFocus.Visibility = Visibility.Collapsed;
                    DetectedPlayerOverlay.UnfilteredPlayerFocus.Visibility = Visibility.Collapsed;
                    DetectedPlayerOverlay.PredictionFocus.Visibility = Visibility.Collapsed;
                }

            });
        }

        private async Task StartModelCaptureLoop()
        {
            // Create a new CancellationTokenSource
            cts = new CancellationTokenSource();
            ResetAimMovementState();

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    bool isConstantTracking = IsConstantTrackingEnabled();
                    bool isAimbotOn = IsAimbotEnabled();
                    bool needsVisualDetection = ShouldRunVisualDetection();
                    bool isBindingHeld = IsBindingCurrentlyHeld();
                    bool bindingRequired = IsAimHoldRequired();
                    bool isTriggerOn = IsTriggerEnabled();
                    bool isRecoilOn = IsRecoilEnabled();

                    TickRecoilControl();

                    Visualization.DebugOverlay.IsLoopRunning = true;

                    DateTime activeNow = DateTime.UtcNow;
                    if ((activeNow - _lastIdleHeartbeatLog).TotalMilliseconds >= 1000 && (isAimbotOn || isTriggerOn || isConstantTracking || isRecoilOn))
                    {
                        Log($"Loop Active: Aimbot={isAimbotOn} Trigger={isTriggerOn} Constant={isConstantTracking} Recoil={isRecoilOn} HoldRequired={bindingRequired} Holding={isBindingHeld} Visual={needsVisualDetection} ModelLoaded={_onnxModel != null}");
                        _lastIdleHeartbeatLog = activeNow;
                    }


                    
                    if (isConstantTracking && isAimbotOn)
                    {
                        await ModelCapture();
                    }
                    else if (isAimbotOn && bindingRequired && isBindingHeld)
                    {
                        await ModelCapture();
                    }
                    else if (isTriggerOn)
                    {
                        await ModelCapture(true);
                    }
                    else if (needsVisualDetection || IsCollectDataEnabled())
                    {
                        await ModelCapture();
                    }
                    else if (IsCollectDataEnabled())
                    {
                        // Data collection only mode (non-aiming)
                        await ModelCapture();
                    }
                    else if (needsVisualDetection)
                    {
                        // Visual debugging is enabled even without aiming features.
                        await ModelCapture();
                    }
                    else
                    {
                        // Heartbeat: ensure we still update DPI even if loop is idle
                        ResetAimMovementState();
                        DateTime now = DateTime.UtcNow;
                        if ((now - _lastIdleHeartbeatLog).TotalMilliseconds >= 1000)
                        {
                            // Log($"Loop Heartbeat: Idle (No features enabled) | Aimbot={isAimbotOn} Constant={isConstantTracking} Trigger={isTriggerOn} Recoil={isRecoilOn} Collect={IsCollectDataEnabled()} Visual={needsVisualDetection} Holding={isBindingHeld} ModelLoaded={_onnxModel != null}");
                            _lastIdleHeartbeatLog = now;
                        }
                        AIModel.DpiScaleX = VisualTreeHelper.GetDpi(this).DpiScaleX;
                        AIModel.DpiScaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
                    }
                    UpdateHudOverlayState();
                    ResetLoopFailureState(ref _captureLoopErrorCount, ref _captureLoopErrorWindowStartUtc);
                }
                catch (Exception ex)
                {
                    bool shouldStop = RegisterLoopFailure("Capture", ex, ref _captureLoopErrorCount, ref _captureLoopErrorWindowStartUtc);
                    if (shouldStop)
                        break;

                    await Task.Delay(500);
                }

                try
                {
                    await Task.Delay(1, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public void StopModelCaptureLoop()
        {
            if (cts != null)
            {
                cts?.Cancel();
                cts = null;
            }

            if (_rapidFireLoopCts != null)
            {
                _rapidFireLoopCts.Cancel();
                _rapidFireLoopCts = null;
            }

            ResetAimMovementState();
        }

        #endregion Aim Aligner Main and Loop

        #region Menu Initialization and Setup

        private void InitializeMenuPositions()
        {
            AimMenu.Margin = new Thickness(0, 0, 0, 0);
            TriggerMenu.Margin = new Thickness(560, 0, -560, 0);
            SelectorMenu.Margin = new Thickness(1120, 0, -1120, 0);
            RecoilMenu.Margin = new Thickness(1680, 0, -1680, 0);
            SettingsMenu.Margin = new Thickness(2240, 0, -2240, 0);
        }

        private void SetupToggle(AToggle toggle, Action<bool> action, bool initialState)
        {
            toggle.Reader.Tag = initialState;
            (initialState ? (Action)(() => toggle.EnableSwitch()) : () => toggle.DisableSwitch())();

            if (!string.IsNullOrEmpty(toggle.Reader.Name))
            {
                toggleRegistry[toggle.Reader.Name] = toggle;
                toggleActions[toggle.Reader.Name] = action;
            }

            toggle.Reader.Click += (s, x) =>
            {
                bool currentState = (bool)toggle.Reader.Tag;
                toggle.Reader.Tag = !currentState;
                action.Invoke(!currentState);
                SetToggleState(toggle);
            };
        }

        private void SetToggleState(AToggle toggle)
        {
            bool state = (bool)toggle.Reader.Tag;

            // Stop them from turning on anything until the model has been selected.
            if ((toggle.Reader.Name == "AimbotToggle" || toggle.Reader.Name == "AimOnlyWhenBindingHeld" || toggle.Reader.Name == "ConstantAITracking" || toggle.Reader.Name == "TriggerBot" || toggle.Reader.Name == "CollectData") && lastLoadedModel == "N/A")
            {
                toggle.Reader.Tag = false;
                toggle.DisableSwitch();
                toggleState[toggle.Reader.Name] = false;
                SetToggleStatesOnModelNotSelected();
                MessageBox.Show("Please select a model in the Model Selector before toggling.", "Toggle Error");
                return;
            }

            (state ? (Action)toggle.EnableSwitch : (Action)toggle.DisableSwitch)();

            toggleState[toggle.Reader.Name] = state;

            HandleToggleSpecificActions(toggle);
            SaveSessionState();
        }

        private void SetToggleStatesOnModelNotSelected()
        {
            Bools.AIAimAligner = false;
            Bools.Triggerbot = false;
            Bools.CollectDataWhilePlaying = false;
            Bools.ConstantTracking = false;
            Bools.AimOnlyWhenBindingHeld = false;
            toggleState["AimbotToggle"] = false;
            toggleState["TriggerBot"] = false;
            toggleState["CollectData"] = false;
            toggleState["ConstantAITracking"] = false;
            toggleState["AimOnlyWhenBindingHeld"] = false;
        }

        private void HandleToggleSpecificActions(AToggle toggle)
        {
            string toggleName = toggle.Reader.Name;

            switch (toggleName)
            {
                case "CollectData":
                    if (_onnxModel != null)
                        _onnxModel.CollectData = (bool)toggle.Reader.Tag;
                    break;

                case "ShowFOV":
                    ((bool)toggle.Reader.Tag ? (Action)FOVOverlay.Show : (Action)FOVOverlay.Hide)();
                    break;

                case "TravellingFOV":
                    AwfulPropertyChanger.PostTravellingFOV((bool)toggle.Reader.Tag);
                    break;

                case "ShowDetectedPlayerWindow":
                    ((bool)toggle.Reader.Tag ? (Action)DetectedPlayerOverlay.Show : (Action)DetectedPlayerOverlay.Hide)();
                    if ((bool)toggle.Reader.Tag)
                    {
                        bool anySubOverlayEnabled = ToggleStateIsActive("ShowCurrentDetectedPlayer")
                            || ToggleStateIsActive("ShowUnfilteredDetectedPlayer")
                            || ToggleStateIsActive("ShowAIPrediction")
                            || Bools.ShowCurrentDetectedPlayer
                            || Bools.ShowUnfilteredDetectedPlayer
                            || Bools.ShowPrediction;

                        if (!anySubOverlayEnabled)
                            ForceToggle("ShowCurrentDetectedPlayer", true);
                    }
                    else
                    {
                        ForceToggle("ShowCurrentDetectedPlayer", false);
                        ForceToggle("ShowUnfilteredDetectedPlayer", false);
                        ForceToggle("ShowAIPrediction", false);
                    }
                    break;

                case "ShowCurrentDetectedPlayer":
                    if ((bool)toggle.Reader.Tag)
                        ForceToggle("ShowDetectedPlayerWindow", true);
                    ((bool)toggle.Reader.Tag ? (Action)(() => DetectedPlayerOverlay.DetectedPlayerFocus.Visibility = Visibility.Visible) : () => DetectedPlayerOverlay.DetectedPlayerFocus.Visibility = Visibility.Collapsed)();
                    break;

                case "ShowUnfilteredDetectedPlayer":
                    if ((bool)toggle.Reader.Tag)
                        ForceToggle("ShowDetectedPlayerWindow", true);
                    ((bool)toggle.Reader.Tag ? (Action)(() => DetectedPlayerOverlay.UnfilteredPlayerFocus.Visibility = Visibility.Visible) : () => DetectedPlayerOverlay.UnfilteredPlayerFocus.Visibility = Visibility.Collapsed)();
                    break;

                case "ShowAIPrediction":
                    if ((bool)toggle.Reader.Tag)
                        ForceToggle("ShowDetectedPlayerWindow", true);
                    ((bool)toggle.Reader.Tag ? (Action)(() => DetectedPlayerOverlay.PredictionFocus.Visibility = Visibility.Visible) : () => DetectedPlayerOverlay.PredictionFocus.Visibility = Visibility.Collapsed)();
                    break;

                case "TopMost":
                    Topmost = (bool)toggle.Reader.Tag;
                    break;

                case "ShowDebugOverlay":
                    if ((bool)toggle.Reader.Tag) debugOverlay.Show(); else debugOverlay.Hide();
                    break;
            }
        }

        private void ForceToggle(string toggleName, bool targetState)
        {
            if (!toggleRegistry.TryGetValue(toggleName, out AToggle toggle))
                return;

            bool currentState = (bool)toggle.Reader.Tag;
            if (currentState == targetState)
                return;

            toggle.Reader.Tag = targetState;
            (targetState ? (Action)toggle.EnableSwitch : (Action)toggle.DisableSwitch)();

            if (toggleActions.TryGetValue(toggleName, out var action))
            {
                action(targetState);
            }

            toggleState[toggleName] = targetState;
            HandleToggleSpecificActions(toggle);
            SaveSessionState();
        }

        private bool IsDetectionOverlayActive()
        {
            return Bools.ShowDetectedPlayerWindow
                || ToggleStateIsActive("ShowDetectedPlayerWindow")
                || Bools.ShowCurrentDetectedPlayer
                || ToggleStateIsActive("ShowCurrentDetectedPlayer")
                || Bools.ShowUnfilteredDetectedPlayer
                || ToggleStateIsActive("ShowUnfilteredDetectedPlayer")
                || Bools.ShowPrediction
                || ToggleStateIsActive("ShowAIPrediction");
        }

        private void EnsureDetectionOverlayVisibility(bool shouldShow)
        {
            if (shouldShow)
            {
                if (!DetectedPlayerOverlay.IsVisible)
                    DetectedPlayerOverlay.Show();
            }
            else
            {
                if (DetectedPlayerOverlay.IsVisible)
                    DetectedPlayerOverlay.Hide();
            }
        }

        private bool ShouldRunVisualDetection()
        {
            return IsDetectionOverlayActive();
        }

        private bool HasValidAimTarget(int x, int y, int minX, int minY, int maxX, int maxY)
        {
            if (x < minX || y < minY || x > maxX || y > maxY)
                return false;

            const int cornerMargin = 40;
            int safeRight = Math.Max(minX, maxX - cornerMargin);
            int safeBottom = Math.Max(minY, maxY - cornerMargin);

            bool nearLeft = x <= minX + cornerMargin;
            bool nearRight = x >= safeRight;
            bool nearTop = y <= minY + cornerMargin;
            bool nearBottom = y >= safeBottom;

            if ((nearLeft && nearTop) || (nearLeft && nearBottom) || (nearRight && nearTop) || (nearRight && nearBottom))
                return false;

            return true;
        }

        private bool IsTargetNearScreenCenter(int x, int y)
        {
            var primaryBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int centerX = primaryBounds.Left + (primaryBounds.Width / 2);
            int centerY = primaryBounds.Top + (primaryBounds.Height / 2);
            double fovSize = Math.Max(1, GetSettingDouble("FOV_Size", 320));
            double tolerance = Math.Max(16.0, Math.Min(fovSize * 0.25, 400.0));

            return Math.Abs(x - centerX) <= tolerance && Math.Abs(y - centerY) <= tolerance;
        }

        private void ConfigureSaveFrameAction()
        {
            Visualization.DebugOverlay.SaveFrameAction = () =>
            {
                _modelAccessGate.Wait();
                try
                {
                    _onnxModel?.SaveLastFrame();
                }
                finally
                {
                    _modelAccessGate.Release();
                }
            };
        }

        #endregion Menu Initialization and Setup

        #region Menu Controls

        private async void Selection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button clickedButton)
            {
                var tag = clickedButton.Tag?.ToString();
                if (tag != null)
                {
                    MenuPosition position = (MenuPosition)Enum.Parse(typeof(MenuPosition), tag);
                    _selectedMenuPosition = position;
                    ResetMenuColors();
                    ApplyMenuAnimations(position);
                    UpdateMenuVisibility(position);
                }
            }
        }

        private void ResetMenuColors()
        {
            Brush defaultBrush = (Brush)brushcolor.ConvertFromString("#ffffff");
            Brush accentBrush = GetMenuAccentBrush();

            Selection1.Foreground = Selection2.Foreground = Selection3.Foreground = Selection4.Foreground = Selection5.Foreground = defaultBrush;

            switch (_selectedMenuPosition)
            {
                case MenuPosition.AimMenu:
                    Selection1.Foreground = accentBrush;
                    break;
                case MenuPosition.TriggerMenu:
                    Selection2.Foreground = accentBrush;
                    break;
                case MenuPosition.SelectorMenu:
                    Selection3.Foreground = accentBrush;
                    break;
                case MenuPosition.RecoilMenu:
                    Selection4.Foreground = accentBrush;
                    break;
                case MenuPosition.SettingsMenu:
                    Selection5.Foreground = accentBrush;
                    break;
            }
        }

        private Brush GetMenuAccentBrush()
        {
            string colorValue = GetSettingString("GUI_AccentColor", DefaultMenuAccentColor);
            try
            {
                return (Brush)brushcolor.ConvertFromString(colorValue);
            }
            catch
            {
                return (Brush)brushcolor.ConvertFromString(DefaultMenuAccentColor);
            }
        }

        private void ApplyMenuAccentColor(string colorValue)
        {
            if (string.IsNullOrWhiteSpace(colorValue))
                colorValue = DefaultMenuAccentColor;

            aimmySettings["GUI_AccentColor"] = colorValue;

            try
            {
                MenuHighlighter.Background = (Brush)brushcolor.ConvertFromString(colorValue);
            }
            catch
            {
                MenuHighlighter.Background = (Brush)brushcolor.ConvertFromString(DefaultMenuAccentColor);
            }

            ResetMenuColors();
        }

        private void ApplyMenuAnimations(MenuPosition position)
        {
            Thickness highlighterMargin = new(0, 30, 434, 0);
            switch (position)
            {
                case MenuPosition.AimMenu:
                    highlighterMargin = new Thickness(10, 30, 434, 0);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), MenuHighlighter, MenuHighlighter.Margin, highlighterMargin);

                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), AimMenu, AimMenu.Margin, WinCenter);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), TriggerMenu, TriggerMenu.Margin, WinRight);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SelectorMenu, SelectorMenu.Margin, WinVeryRight);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), RecoilMenu, RecoilMenu.Margin, WinTooRight);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SettingsMenu, SettingsMenu.Margin, WinFarRight);
                    break;

                case MenuPosition.TriggerMenu:
                    highlighterMargin = new Thickness(116, 30, 328, 0);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), MenuHighlighter, MenuHighlighter.Margin, highlighterMargin);

                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), AimMenu, AimMenu.Margin, WinLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), TriggerMenu, TriggerMenu.Margin, WinCenter);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SelectorMenu, SelectorMenu.Margin, WinRight);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), RecoilMenu, RecoilMenu.Margin, WinVeryRight);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SettingsMenu, SettingsMenu.Margin, WinTooRight);
                    break;

                case MenuPosition.SelectorMenu:
                    highlighterMargin = new Thickness(222, 30, 222, 0);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), MenuHighlighter, MenuHighlighter.Margin, highlighterMargin);

                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), AimMenu, AimMenu.Margin, WinVeryLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), TriggerMenu, TriggerMenu.Margin, WinLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SelectorMenu, SelectorMenu.Margin, WinCenter);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), RecoilMenu, RecoilMenu.Margin, WinRight);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SettingsMenu, SettingsMenu.Margin, WinVeryRight);
                    break;

                case MenuPosition.RecoilMenu:
                    highlighterMargin = new Thickness(328, 30, 116, 0);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), MenuHighlighter, MenuHighlighter.Margin, highlighterMargin);

                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), AimMenu, AimMenu.Margin, WinTooLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), TriggerMenu, TriggerMenu.Margin, WinVeryLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SelectorMenu, SelectorMenu.Margin, WinLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), RecoilMenu, RecoilMenu.Margin, WinCenter);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SettingsMenu, SettingsMenu.Margin, WinRight);
                    break;

                case MenuPosition.SettingsMenu:
                    highlighterMargin = new Thickness(434, 30, 10, 0);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), MenuHighlighter, MenuHighlighter.Margin, highlighterMargin);

                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), AimMenu, AimMenu.Margin, WinFarLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), TriggerMenu, TriggerMenu.Margin, WinTooLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SelectorMenu, SelectorMenu.Margin, WinVeryLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), RecoilMenu, RecoilMenu.Margin, WinLeft);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), SettingsMenu, SettingsMenu.Margin, WinCenter);
                    break;
            }
        }

        private void UpdateMenuVisibility(MenuPosition position)
        {
            AimMenu.Visibility = (position == MenuPosition.AimMenu) ? Visibility.Visible : Visibility.Collapsed;
            TriggerMenu.Visibility = (position == MenuPosition.TriggerMenu) ? Visibility.Visible : Visibility.Collapsed;
            SelectorMenu.Visibility = (position == MenuPosition.SelectorMenu) ? Visibility.Visible : Visibility.Collapsed;
            RecoilMenu.Visibility = (position == MenuPosition.RecoilMenu) ? Visibility.Visible : Visibility.Collapsed;
            SettingsMenu.Visibility = (position == MenuPosition.SettingsMenu) ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion Menu Controls

        #region More Info Function

        public void ActivateMoreInfo(string info)
        {
            SetMenuState(false);
            Animator.ObjectShift(TimeSpan.FromMilliseconds(1000), MoreInfoBox, MoreInfoBox.Margin, new Thickness(5, 0, 5, 5));
            MoreInfoBox.Visibility = Visibility.Visible;
            InfoText.Text = info;
        }

        private async void MoreInfoExit_Click(object sender, RoutedEventArgs e)
        {
            Animator.ObjectShift(TimeSpan.FromMilliseconds(1000), MoreInfoBox, MoreInfoBox.Margin, new Thickness(5, 0, 5, -180));
            await Task.Delay(1000);
            MoreInfoBox.Visibility = Visibility.Collapsed;
            SetMenuState(true);
        }

        private void SetMenuState(bool state)
        {
            AimMenu.IsEnabled = state;
            TriggerMenu.IsEnabled = state;
            SelectorMenu.IsEnabled = state;
            RecoilMenu.IsEnabled = state;
            SettingsMenu.IsEnabled = state;
        }

        #endregion More Info Function

        private void LoadAimMenu()
        {
            AToggle Enable_AIAimAligner = new(this, "Enable AI Aim Aligner",
                "This will enable the AI's ability to align the aim.");
            Enable_AIAimAligner.Reader.Name = "AimbotToggle";
            SetupToggle(Enable_AIAimAligner, state => Bools.AIAimAligner = state, Bools.AIAimAligner);
            AimScroller.Children.Add(Enable_AIAimAligner);

            AToggle Enable_ConstantAITracking = new(this, "Enable Constant AI Aligner",
    "This will let the AI run 24/7 to let Visual Debugging run.");
            Enable_ConstantAITracking.Reader.Name = "ConstantAITracking";
            SetupToggle(Enable_ConstantAITracking, state => Bools.ConstantTracking = state, Bools.ConstantTracking);
            AimScroller.Children.Add(Enable_ConstantAITracking);

            AToggle AimOnlyWhenBindingHeld = new(this, "Aim only when Trigger Button is held", // this can be simplifed with a toggle between constant and hold (toggle/hold), ill do it later.
"This will stop the AI from aiming unless the Trigger Button is held.");
            AimOnlyWhenBindingHeld.Reader.Name = "AimOnlyWhenBindingHeld";

            // Add the Hold/Toggle mode selector
            var holdModeSelector = new AModeSelector();
            holdModeSelector.CurrentMode = Bools.AimHoldMode ?? "Hold";
            holdModeSelector.Visibility = Bools.AimOnlyWhenBindingHeld ? Visibility.Visible : Visibility.Collapsed;
            holdModeSelector.ModeChanged += (mode) =>
            {
                Bools.AimHoldMode = mode;
                // If switching away from Toggle, reset the held state to avoid getting stuck
                if (mode != "Toggle")
                    IsHolding_Binding = false;
            };

            SetupToggle(AimOnlyWhenBindingHeld, state =>
            {
                Bools.AimOnlyWhenBindingHeld = state;
                holdModeSelector.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
                // Reset held state when disabling
                if (!state) IsHolding_Binding = false;
            }, Bools.AimOnlyWhenBindingHeld);

            AimScroller.Children.Add(AimOnlyWhenBindingHeld);
            AimScroller.Children.Add(holdModeSelector);

            AKeyChanger Change_KeyPress = new("Change Keybind", _bindingManagers[0]?.CurrentBinding ?? "Right");
            Change_KeyPress.Reader.Click += (s, x) =>
            {
                Change_KeyPress.KeyNotifier.Content = "Listening..";
                Bools.ActiveBindingSlot = 0;
                bindingManager.StartListeningForBinding();
            };

            bindingManager.OnBindingSet += (binding) =>
            {
                Change_KeyPress.KeyNotifier.Content = binding;
                Bools.AimBindingSlots[0] = binding;
                SaveSessionState();
            };

            AimScroller.Children.Add(Change_KeyPress);

            AKeyChanger BindingSlot2 = new("Binding Slot 2", _bindingManagers[1]?.CurrentBinding ?? "None");
            BindingSlot2.Reader.Click += (s, x) =>
            {
                BindingSlot2.KeyNotifier.Content = "Listening..";
                Bools.ActiveBindingSlot = 1;
                _bindingManagers[1].StartListeningForBinding();
            };
            _bindingManagers[1].OnBindingSet += (binding) =>
            {
                BindingSlot2.KeyNotifier.Content = binding;
                Bools.AimBindingSlots[1] = binding;
                SaveSessionState();
            };
            AimScroller.Children.Add(BindingSlot2);

            AKeyChanger BindingSlot3 = new("Binding Slot 3", _bindingManagers[2]?.CurrentBinding ?? "None");
            BindingSlot3.Reader.Click += (s, x) =>
            {
                BindingSlot3.KeyNotifier.Content = "Listening..";
                Bools.ActiveBindingSlot = 2;
                _bindingManagers[2].StartListeningForBinding();
            };
            _bindingManagers[2].OnBindingSet += (binding) =>
            {
                BindingSlot3.KeyNotifier.Content = binding;
                Bools.AimBindingSlots[2] = binding;
                SaveSessionState();
            };
            AimScroller.Children.Add(BindingSlot3);

            //AToggle Enable_AlwaysOn = new AToggle(this, "Aim Align Always On",
            //   "This will keep the aim aligner on 24/7 so you don't have to hold a toggle.");
            //Enable_AlwaysOn.Reader.Name = "AlwaysOn";
            //SetupToggle(Enable_AlwaysOn, state => Bools.AIAlwaysOn = state, Bools.AIAlwaysOn);
            //AimScroller.Children.Add(Enable_AlwaysOn);

            AToggle Enable_AIPredictions = new(this, "Enable Predictions",
               "This will use a KalmanFilter algorithm to predict aim patterns for better tracing of enemies.");
            Enable_AIPredictions.Reader.Name = "PredictionToggle";
            SetupToggle(Enable_AIPredictions, state => Bools.AIPredictions = state, Bools.AIPredictions);
            AimScroller.Children.Add(Enable_AIPredictions);

            #region FOV System

            AimScroller.Children.Add(new ALabel("FOV System"));

            AToggle Show_FOV = new(this, "Show FOV",
                "This will show a circle around your screen that show what the AI is considering on the screen at a given moment.");
            Show_FOV.Reader.Name = "ShowFOV";
            SetupToggle(Show_FOV, state => Bools.ShowFOV = state, Bools.ShowFOV);
            AimScroller.Children.Add(Show_FOV);

            AToggle Travelling_FOV = new(this, "Travelling FOV",
    "This will allow the FOV circle to travel alongside your mouse.\n" +
    "[PLEASE NOTE]: This does not have any effect on the AI's personal FOV, this feature is only for the visual effect.");
            Travelling_FOV.Reader.Name = "TravellingFOV";
            SetupToggle(Travelling_FOV, state => Bools.TravellingFOV = state, Bools.TravellingFOV);
            AimScroller.Children.Add(Travelling_FOV);

            AColorChanger Change_FOVColor = new("FOV Color");
            Change_FOVColor.ColorChangingBorder.Background = (Brush)new BrushConverter().ConvertFromString(OverlayProperties["FOV_Color"]);
            Change_FOVColor.Reader.Click += (s, x) =>
            {
                System.Windows.Forms.ColorDialog colorDialog = new();
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    Change_FOVColor.ColorChangingBorder.Background = new SolidColorBrush(Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B));
                    OverlayProperties["FOV_Color"] = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B).ToString();
                    AwfulPropertyChanger.PostColor(Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B));
                }
            };
            AimScroller.Children.Add(Change_FOVColor);

            ASlider FovSlider = new(this, "FOV Size", "Size of FOV",
                "This setting controls how much of your screen is considered in the AI's decision making and how big the circle on your screen will be.",
                1);

            FovSlider.Slider.Minimum = 10;
            FovSlider.Slider.Maximum = 640;
            FovSlider.Slider.Value = GetSettingDouble("FOV_Size", 320);
            FovSlider.Slider.TickFrequency = 1;
            FovSlider.Slider.ValueChanged += (s, x) =>
            {
                double FovSize = FovSlider.Slider.Value;
                aimmySettings["FOV_Size"] = FovSize;
                if (_onnxModel != null)
                {
                    _onnxModel.FovSize = (int)FovSize;
                }

                FOVOverlay.FovSize = (int)FovSize;
                AwfulPropertyChanger.PostNewFOVSize();
            };

            AimScroller.Children.Add(FovSlider);

            #endregion FOV System

            #region Aiming Configuration

            AimScroller.Children.Add(new ALabel("Aiming Configuration"));

            ASlider MouseSensitivty = new(this, "Mouse Sensitivty", "Sensitivty",
                "This setting controls how fast your mouse moves to a detection, if it moves too fast you need to set it to a higher number.",
                0.01);

            MouseSensitivty.Slider.Minimum = 0.01;
            MouseSensitivty.Slider.Maximum = 1;
            MouseSensitivty.Slider.Value = GetSettingDouble("Mouse_Sens", 0.80);
            MouseSensitivty.Slider.TickFrequency = 0.01;
            MouseSensitivty.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Mouse_Sens"] = MouseSensitivty.Slider.Value;
                MarkAimStyleAsCustomIfNeeded();
            };

            AimScroller.Children.Add(MouseSensitivty);

            ASlider AimSmoothness = new(this, "Aim Smoothness", "Smoothness",
                "Filters sudden target jumps before movement is applied. Higher feels silkier, lower feels snappier.",
                0.01);

            AimSmoothness.Slider.Minimum = 0.05;
            AimSmoothness.Slider.Maximum = 0.95;
            AimSmoothness.Slider.Value = GetSettingDouble("Aim_Smoothness", 0.78);
            AimSmoothness.Slider.TickFrequency = 0.01;
            AimSmoothness.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Aim_Smoothness"] = AimSmoothness.Slider.Value;
                MarkAimStyleAsCustomIfNeeded();
            };

            AimScroller.Children.Add(AimSmoothness);

            ASlider AimMaxStep = new(this, "Aim Max Step", "Pixels",
                "Caps the largest move per update. Lower values feel premium and controlled; higher values feel faster.",
                0.5);

            AimMaxStep.Slider.Minimum = 2;
            AimMaxStep.Slider.Maximum = 32;
            AimMaxStep.Slider.Value = GetSettingDouble("Aim_MaxStep", 14.0);
            AimMaxStep.Slider.TickFrequency = 0.5;
            AimMaxStep.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Aim_MaxStep"] = AimMaxStep.Slider.Value;
                MarkAimStyleAsCustomIfNeeded();
            };

            AimScroller.Children.Add(AimMaxStep);

            ASlider AimDeadzone = new(this, "Aim Deadzone", "Pixels",
                "Ignores tiny correction jitter when already on target to keep the end of tracking stable and clean.",
                0.25);

            AimDeadzone.Slider.Minimum = 0;
            AimDeadzone.Slider.Maximum = 8;
            AimDeadzone.Slider.Value = GetSettingDouble("Aim_Deadzone", 1.5);
            AimDeadzone.Slider.TickFrequency = 0.25;
            AimDeadzone.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Aim_Deadzone"] = AimDeadzone.Slider.Value;
                MarkAimStyleAsCustomIfNeeded();
            };

            AimScroller.Children.Add(AimDeadzone);

            ASlider MouseJitter = new(this, "Mouse Jitter", "Jitter",
                "This setting controls how much fake jitter is added to the mouse movements. Aim is almost never steady so this adds a nice layer of humanizing onto aim.",
                0.01);

            MouseJitter.Slider.Minimum = 0;
            MouseJitter.Slider.Maximum = 15;
            MouseJitter.Slider.Value = GetSettingDouble("Mouse_Jitter", 4);
            MouseJitter.Slider.TickFrequency = 1;
            MouseJitter.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Mouse_Jitter"] = MouseJitter.Slider.Value;
                MarkAimStyleAsCustomIfNeeded();
            };
            AimScroller.Children.Add(MouseJitter);

            ASlider YOffset = new(this, "Y Offset (Up/Down)", "Offset",
                "This setting controls how high / low you aim. A lower number will result in a higher aim. A higher number will result in a lower aim.",
                1);

            YOffset.Slider.Minimum = -150;
            YOffset.Slider.Maximum = 150;
            YOffset.Slider.Value = GetSettingDouble("Y_Offset", 0);
            YOffset.Slider.TickFrequency = 1;
            YOffset.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Y_Offset"] = YOffset.Slider.Value;
            };

            AimScroller.Children.Add(YOffset);

            ASlider XOffset = new(this, "X Offset (Left/Right)", "Offset",
                "This setting controls which way your aim leans. A lower number will result in an aim that leans to the left. A higher number will result in an aim that leans to the right",
                1);

            XOffset.Slider.Minimum = -150;
            XOffset.Slider.Maximum = 150;
            XOffset.Slider.Value = GetSettingDouble("X_Offset", 0);
            XOffset.Slider.TickFrequency = 1;
            XOffset.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["X_Offset"] = XOffset.Slider.Value;
            };

            AimScroller.Children.Add(XOffset);

            ASlider AimTargetZone = new(this, "Aim Target Zone", "%",
                "Controls how far from the top of the detection box the aim point is placed. Lower values aim higher on the target.",
                1);

            AimTargetZone.Slider.Minimum = 0;
            AimTargetZone.Slider.Maximum = 100;
            AimTargetZone.Slider.Value = Math.Round(GetSettingDouble("Aim_HeadRatio", 0.15) * 100.0);
            AimTargetZone.Slider.TickFrequency = 1;
            AimTargetZone.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Aim_HeadRatio"] = AimTargetZone.Slider.Value / 100.0;
                MarkAimStyleAsCustomIfNeeded();
            };

            AimScroller.Children.Add(AimTargetZone);

            ASlider PredictionStrength = new(this, "Prediction Strength", "Strength",
                "Scales how much of the Kalman lead is applied to the next aim estimate.",
                0.01);

            PredictionStrength.Slider.Minimum = 0;
            PredictionStrength.Slider.Maximum = 1;
            PredictionStrength.Slider.Value = GetSettingDouble("Aim_PredictionStrength", 1.0);
            PredictionStrength.Slider.TickFrequency = 0.01;
            PredictionStrength.Slider.ValueChanged += (s, x) =>
            {
                double value = Math.Clamp(PredictionStrength.Slider.Value, 0.0, 1.0);
                aimmySettings["Aim_PredictionStrength"] = value;
                if (predictionManager != null)
                    predictionManager.PredictionStrength = value;
            };

            AimScroller.Children.Add(PredictionStrength);

            AimScroller.Children.Add(new ALabel("Aim Style Presets"));

            AButton PremiumSmoothStyle = new(this, "Switch Style: Premium Smooth",
                "Maximum smoothness with controlled micro-adjustments. Best when you want polished, stable tracking.");
            PremiumSmoothStyle.Reader.Click += (s, e) =>
            {
                ApplyAimStylePreset(
                    "Premium Smooth",
                    MouseSensitivty.Slider,
                    AimSmoothness.Slider,
                    AimMaxStep.Slider,
                    AimDeadzone.Slider,
                    MouseJitter.Slider,
                    AimTargetZone.Slider);
            };
            AimScroller.Children.Add(PremiumSmoothStyle);

            AButton BalancedStyle = new(this, "Switch Style: Balanced",
                "Even blend of smoothness and responsiveness for general use.");
            BalancedStyle.Reader.Click += (s, e) =>
            {
                ApplyAimStylePreset(
                    "Balanced",
                    MouseSensitivty.Slider,
                    AimSmoothness.Slider,
                    AimMaxStep.Slider,
                    AimDeadzone.Slider,
                    MouseJitter.Slider,
                    AimTargetZone.Slider);
            };
            AimScroller.Children.Add(BalancedStyle);

            AButton AggressiveStyle = new(this, "Switch Style: Aggressive",
                "Snappier tracking with larger movement steps and jitter disabled (0) for maximum speed.");
            AggressiveStyle.Reader.Click += (s, e) =>
            {
                ApplyAimStylePreset(
                    "Aggressive",
                    MouseSensitivty.Slider,
                    AimSmoothness.Slider,
                    AimMaxStep.Slider,
                    AimDeadzone.Slider,
                    MouseJitter.Slider,
                    AimTargetZone.Slider);
            };
            AimScroller.Children.Add(AggressiveStyle);

            #endregion Aiming Configuration

            #region Visual Debugging

            AimScroller.Children.Add(new ALabel("Visual Debugging"));

            AToggle Show_DetectedPlayerWindow = new(this, "Show Detected Player Window",
                "Shows the Detected Player Overlay, the options below will not work if this is enabled!");
            Show_DetectedPlayerWindow.Reader.Name = "ShowDetectedPlayerWindow";
            SetupToggle(Show_DetectedPlayerWindow, state => Bools.ShowDetectedPlayerWindow = state, Bools.ShowDetectedPlayerWindow);
            AimScroller.Children.Add(Show_DetectedPlayerWindow);

            AToggle Show_CurrentDetectedPlayer = new(this, "Show Current Detected Player [Red]",
    "This will show a rectangle on the player that the AI is considering on the screen at a given moment.");
            Show_CurrentDetectedPlayer.Reader.Name = "ShowCurrentDetectedPlayer";
            SetupToggle(Show_CurrentDetectedPlayer, state => Bools.ShowCurrentDetectedPlayer = state, Bools.ShowCurrentDetectedPlayer);
            AimScroller.Children.Add(Show_CurrentDetectedPlayer);

            AToggle Show_UnfilteredDetectedPlayer = new(this, "Show Unflitered Version of Current Detected Player [Purple]",
                "This will show a rectangle on the player that the AI is considering on the screen at a given moment without considering the adjusted X and Y axis.");
            Show_UnfilteredDetectedPlayer.Reader.Name = "ShowUnfilteredDetectedPlayer";
            SetupToggle(Show_UnfilteredDetectedPlayer, state => Bools.ShowUnfilteredDetectedPlayer = state, Bools.ShowUnfilteredDetectedPlayer);
            AimScroller.Children.Add(Show_UnfilteredDetectedPlayer);

            AToggle Show_Prediction = new(this, "Show AI Prediction [Green]",
                "This will show a rectangle on where the AI assumes the player will be on the screen at a given moment.");
            Show_Prediction.Reader.Name = "ShowAIPrediction";
            SetupToggle(Show_Prediction, state => Bools.ShowPrediction = state, Bools.ShowPrediction);
            AimScroller.Children.Add(Show_Prediction);

            AToggle Show_DebugOverlay = new(this, "Show Debug Diagnostics Panel",
                "Shows a real-time diagnostics panel with inference stats, capture coordinates, and a frame-save button.");
            Show_DebugOverlay.Reader.Name = "ShowDebugOverlay";
            SetupToggle(Show_DebugOverlay, state => { }, false);
            AimScroller.Children.Add(Show_DebugOverlay);

            #endregion Visual Debugging

            #region Visual Debugging Customizer

            AimScroller.Children.Add(new ALabel("Visual Debugging Customization"));

            ASlider Change_PDW_Size = new(this, "Detection Window Size", "Size",
                "This setting controls the size of your Detected Player Windows.",
                1);

            Change_PDW_Size.Slider.Minimum = 10;
            Change_PDW_Size.Slider.Maximum = 100;
            Change_PDW_Size.Slider.Value = OverlayProperties["PDW_Size"];
            Change_PDW_Size.Slider.TickFrequency = 1;
            Change_PDW_Size.Slider.ValueChanged += (s, x) =>
            {
                int PDWSize = (int)Change_PDW_Size.Slider.Value;
                OverlayProperties["PDW_Size"] = PDWSize;
                AwfulPropertyChanger.PostPDWSize(PDWSize);
            };

            AimScroller.Children.Add(Change_PDW_Size);

            ASlider Change_PDW_CornerRadius = new(this, "Detection Window Corner Radius", "Corner Radius",
                "This setting controls the corner radius of your Detected Player Windows.",
                1);

            Change_PDW_CornerRadius.Slider.Minimum = 0;
            Change_PDW_CornerRadius.Slider.Maximum = 100;
            Change_PDW_CornerRadius.Slider.Value = OverlayProperties["PDW_CornerRadius"];
            Change_PDW_CornerRadius.Slider.TickFrequency = 1;
            Change_PDW_CornerRadius.Slider.ValueChanged += (s, x) =>
            {
                int CornerRadiusSize = (int)Change_PDW_CornerRadius.Slider.Value;
                OverlayProperties["PDW_CornerRadius"] = CornerRadiusSize;
                AwfulPropertyChanger.PostPDWCornerRadius(CornerRadiusSize);
            };

            AimScroller.Children.Add(Change_PDW_CornerRadius);

            ASlider Change_PDW_BorderThickness = new(this, "Detection Window Border Thickness", "Border Thickness",
                "This setting controls the Border Thickness of your Detected Player Windows.",
                1);

            Change_PDW_BorderThickness.Slider.Minimum = 0.1;
            Change_PDW_BorderThickness.Slider.Maximum = 10;
            Change_PDW_BorderThickness.Slider.Value = OverlayProperties["PDW_BorderThickness"];
            Change_PDW_BorderThickness.Slider.TickFrequency = 0.1;
            Change_PDW_BorderThickness.Slider.ValueChanged += (s, x) =>
            {
                double BorderThicknessSize = (double)Change_PDW_BorderThickness.Slider.Value;
                OverlayProperties["PDW_BorderThickness"] = BorderThicknessSize;
                AwfulPropertyChanger.PostPDWBorderThickness(BorderThicknessSize);
            };

            AimScroller.Children.Add(Change_PDW_BorderThickness);

            ASlider Change_PDW_Opacity = new(this, "Detection Window Opacity", "Opacity",
                "This setting controls the Opacity of your Detected Player Windows.",
                0.1);

            Change_PDW_Opacity.Slider.Minimum = 0;
            Change_PDW_Opacity.Slider.Maximum = 1;
            Change_PDW_Opacity.Slider.Value = OverlayProperties["PDW_Opacity"];
            Change_PDW_Opacity.Slider.TickFrequency = 0.1;
            Change_PDW_Opacity.Slider.ValueChanged += (s, x) =>
            {
                double WindowOpacity = (double)Change_PDW_Opacity.Slider.Value;
                OverlayProperties["PDW_Opacity"] = WindowOpacity;
                AwfulPropertyChanger.PostPDWOpacity(WindowOpacity);
            };

            AimScroller.Children.Add(Change_PDW_Opacity);

            #endregion Visual Debugging Customizer
        }

        private void LoadTriggerMenu()
        {
            AToggle Enable_TriggerBot = new(this, "Enable Auto Trigger",
                "This will enable the AI's ability to shoot whenever it sees a target.");
            Enable_TriggerBot.Reader.Name = "TriggerBot";
            SetupToggle(Enable_TriggerBot, state => Bools.Triggerbot = state, Bools.Triggerbot);
            TriggerScroller.Children.Add(Enable_TriggerBot);

            ASlider TriggerBot_Delay = new(this, "Auto Trigger Delay", "Seconds",
                "This slider will control how many miliseconds it will take to initiate a trigger.",
                0.1);

            TriggerBot_Delay.Slider.Minimum = 0.01;
            TriggerBot_Delay.Slider.Maximum = 1;
            TriggerBot_Delay.Slider.Value = GetSettingDouble("Trigger_Delay", 0.1);
            TriggerBot_Delay.Slider.TickFrequency = 0.01;
            TriggerBot_Delay.Slider.ValueChanged += (s, x) =>
            {
                aimmySettings["Trigger_Delay"] = TriggerBot_Delay.Slider.Value;
            };

            TriggerScroller.Children.Add(TriggerBot_Delay);
        }

        private void LoadRecoilMenu()
        {
            RecoilScroller.Children.Add(new ALabel("Recoil Control"));

            AToggle EnableRecoilControl = new(this, "Enable Recoil Control",
                "When enabled, Mouse1 replays the loaded recoil pattern.");
            EnableRecoilControl.Reader.Name = "RecoilControl";
            SetupToggle(EnableRecoilControl, state =>
            {
                Bools.RecoilControl = state;
                aimmySettings["Recoil_ControlEnabled"] = state;
            }, GetSettingBool("Recoil_ControlEnabled", Bools.RecoilControl));
            RecoilScroller.Children.Add(EnableRecoilControl);

            bool loopInitialState = GetSettingBool("Recoil_LoopPattern", toggleState.TryGetValue("RecoilLoopPattern", out bool loopState) ? loopState : true);
            AToggle LoopRecoilPattern = new(this, "Loop Pattern While Holding M1",
                "If enabled, the pattern repeats from the start while Mouse1 is held.");
            LoopRecoilPattern.Reader.Name = "RecoilLoopPattern";
            SetupToggle(LoopRecoilPattern, state =>
            {
                aimmySettings["Recoil_LoopPattern"] = state;
            }, loopInitialState);
            RecoilScroller.Children.Add(LoopRecoilPattern);

            AToggle AdsOnlyRecoil = new(this, "ADS Only",
                "Only apply the recoil pattern while right mouse button is held.");
            AdsOnlyRecoil.Reader.Name = "RecoilAdsOnly";
            SetupToggle(AdsOnlyRecoil, state =>
            {
                Bools.RecoilAdsOnly = state;
                aimmySettings["Recoil_AdsOnly"] = state;
            }, GetSettingBool("Recoil_AdsOnly", Bools.RecoilAdsOnly));
            RecoilScroller.Children.Add(AdsOnlyRecoil);

            AToggle RapidFire = new(this, "Rapid Fire",
                "Repeats left click while Mouse1 is held. Use it for semi-auto weapons only.");
            RapidFire.Reader.Name = "RecoilRapidFire";
            SetupToggle(RapidFire, state =>
            {
                Bools.RecoilRapidFire = state;
                aimmySettings["Recoil_RapidFire"] = state;
            }, GetSettingBool("Recoil_RapidFire", Bools.RecoilRapidFire));
            RecoilScroller.Children.Add(RapidFire);

            _recoilStatusTextBlock = new TextBlock()
            {
                Text = BuildRecoilPatternStatusText(),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Atkinson Hyperlegible"),
                Margin = new Thickness(13, 10, 13, 0),
                TextWrapping = TextWrapping.Wrap
            };
            RecoilScroller.Children.Add(_recoilStatusTextBlock);

            _recoilPatternGraph = new RecoilPatternGraph
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            RecoilScroller.Children.Add(_recoilPatternGraph);
            RefreshRecoilPatternGraph();

            AKeyChanger PatternCycleKey = new("Cycle Recoil Pattern", _patternCycleBindingManager?.CurrentBinding ?? "F11");
            PatternCycleKey.Reader.Click += (s, e) =>
            {
                PatternCycleKey.KeyNotifier.Content = "Listening...";
                _patternCycleBindingManager.StartListeningForBinding();
            };
            _patternCycleBindingManager.OnBindingSet += (binding) =>
            {
                PatternCycleKey.KeyNotifier.Content = binding;
                aimmySettings["Recoil_CycleKey"] = binding;
            };
            RecoilScroller.Children.Add(PatternCycleKey);

            RecoilScroller.Children.Add(new ALabel("Recoil Recording [EXPERIMENTAL]"));

            AToggle EnableRecoilRecording = new(this, "Enable Recording Feature",
                "Allows recording mouse deltas into a new pattern. Use F12 (default) to start/stop.");
            EnableRecoilRecording.Reader.Name = "RecoilRecordingEnabled";
            SetupToggle(EnableRecoilRecording, state =>
            {
                _isRecoilRecordingEnabled = state;
                aimmySettings["Recoil_RecordingEnabled"] = state;
                if (!state && _isRecordingRecoil) ToggleRecoilRecording(); // Stop if disabled
            }, GetSettingBool("Recoil_RecordingEnabled", false));
            RecoilScroller.Children.Add(EnableRecoilRecording);

            AKeyChanger RecordingKey = new("Recording Toggle Key", _recoilRecordingBindingManager?.CurrentBinding ?? "F12");
            RecordingKey.Reader.Click += (s, e) =>
            {
                RecordingKey.KeyNotifier.Content = "Listening...";
                _recoilRecordingBindingManager.StartListeningForBinding();
            };
            _recoilRecordingBindingManager.OnBindingSet += (binding) =>
            {
                RecordingKey.KeyNotifier.Content = binding;
                aimmySettings["Recoil_RecordingKey"] = binding;
            };
            RecoilScroller.Children.Add(RecordingKey);

            AButton SaveRecording = new(this, "Save Recorded Pattern",
                "Saves the current recorded steps to a new JSON file.");
            SaveRecording.Reader.Click += (s, e) =>
            {
                if (_recordedRecoilSteps.Count == 0)
                {
                    MessageBox.Show("No steps recorded. Start recording, hold M1 and move your mouse first!", "Recoil Recorder");
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog()
                {
                    Filter = "JSON files (*.json)|*.json",
                    InitialDirectory = RecoilFolderPath,
                    FileName = "NewRecordedPattern.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        var patternData = new 
                        { 
                            steps = _recordedRecoilSteps,
                            settings = new {
                                Recoil_Scale = GetSettingDouble("Recoil_Scale", 1.0),
                                Recoil_SpeedMultiplier = GetSettingDouble("Recoil_SpeedMultiplier", 1.0),
                                Recoil_DefaultStepDelay = GetSettingInt("Recoil_DefaultStepDelay", 16),
                                Recoil_AdsOnly = IsRecoilAdsOnlyEnabled(),
                                Recoil_LoopPattern = IsRecoilLoopEnabled(),
                                Recoil_RapidFire = IsRapidFireEnabled(),
                                Recoil_RapidFireDelayMs = GetSettingInt("Recoil_RapidFireDelayMs", 90)
                            }
                        };
                        File.WriteAllText(saveFileDialog.FileName, JsonConvert.SerializeObject(patternData, Formatting.Indented));
                        MessageBox.Show($"Saved {_recordedRecoilSteps.Count} steps and associated settings to {Path.GetFileName(saveFileDialog.FileName)}", "Recoil Recorder");
                        _recordedRecoilSteps.Clear();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save: {ex.Message}", "Error");
                    }
                }
            };
            RecoilScroller.Children.Add(SaveRecording);

            TextBlock recoilHint = new()
            {
                Text = "Pattern format: JSON with `steps`, or CSV/TXT as `dx,dy,delayMs` per line.",
                Foreground = (Brush)brushcolor.ConvertFromString("#A0A0A0"),
                FontFamily = new FontFamily("Atkinson Hyperlegible"),
                Margin = new Thickness(13, 4, 13, 0),
                TextWrapping = TextWrapping.Wrap
            };
            RecoilScroller.Children.Add(recoilHint);

            AButton LoadRecoilPattern = new(this, "Load Recoil Pattern File",
                "Load a recorded or downloaded recoil pattern file.");
            LoadRecoilPattern.Reader.Click += (s, e) =>
            {
                OpenFileDialog openFileDialog = new()
                {
                    Filter = "Recoil files (*.json;*.csv;*.txt)|*.json;*.csv;*.txt|JSON (*.json)|*.json|CSV (*.csv)|*.csv|Text (*.txt)|*.txt|All files (*.*)|*.*",
                    InitialDirectory = RecoilFolderPath,
                    CheckFileExists = true
                };

                bool? pickedFile = openFileDialog.ShowDialog();
                if (pickedFile != true)
                    return;

                if (!TryLoadRecoilPatternFromFile(openFileDialog.FileName, out string error))
                {
                    MessageBox.Show(error, "Recoil Pattern Error");
                    return;
                }

                _recoilStatusTextBlock.Text = BuildRecoilPatternStatusText();
                RefreshRecoilPatternGraph();
            };
            RecoilScroller.Children.Add(LoadRecoilPattern);

            AButton SaveActivePattern = new(this, "Save Active Pattern to File",
                "Saves the currently loaded pattern AND its current settings back to a new JSON file.");
            SaveActivePattern.Reader.Click += (s, e) =>
            {
                if (_recoilPatternSteps == null || _recoilPatternSteps.Count == 0)
                {
                    MessageBox.Show("No active pattern loaded to save.", "Recoil Control");
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog()
                {
                    Filter = "JSON files (*.json)|*.json",
                    InitialDirectory = RecoilFolderPath,
                    FileName = (Path.GetFileNameWithoutExtension(_loadedRecoilPatternPath) ?? "NewPattern") + "_Custom.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        var patternData = new 
                        { 
                            steps = _recoilPatternSteps,
                            settings = new {
                                Recoil_Scale = GetSettingDouble("Recoil_Scale", 1.0),
                                Recoil_SpeedMultiplier = GetSettingDouble("Recoil_SpeedMultiplier", 1.0),
                                Recoil_DefaultStepDelay = GetSettingInt("Recoil_DefaultStepDelay", 16),
                                Recoil_AdsOnly = IsRecoilAdsOnlyEnabled(),
                                Recoil_LoopPattern = IsRecoilLoopEnabled(),
                                Recoil_RapidFire = IsRapidFireEnabled(),
                                Recoil_RapidFireDelayMs = GetSettingInt("Recoil_RapidFireDelayMs", 90)
                            }
                        };
                        File.WriteAllText(saveFileDialog.FileName, JsonConvert.SerializeObject(patternData, Formatting.Indented));
                        MessageBox.Show($"Saved active pattern and settings to {Path.GetFileName(saveFileDialog.FileName)}", "Recoil Control");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save: {ex.Message}", "Error");
                    }
                }
            };
            RecoilScroller.Children.Add(SaveActivePattern);

            AButton ReloadRecoilPattern = new(this, "Reload Current Pattern",
                "Re-reads the currently selected recoil pattern from disk.");
            ReloadRecoilPattern.Reader.Click += (s, e) =>
            {
                if (!TryLoadConfiguredRecoilPattern(out string error))
                {
                    MessageBox.Show(error, "Recoil Pattern Error");
                    return;
                }

                _recoilStatusTextBlock.Text = BuildRecoilPatternStatusText();
                RefreshRecoilPatternGraph();
            };
            RecoilScroller.Children.Add(ReloadRecoilPattern);

            RecoilScroller.Children.Add(new ALabel("Pattern Importer"));

            TextBox patternInput = new()
            {
                Height = 100,
                Margin = new Thickness(13, 0, 13, 10),
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = (Brush)brushcolor.ConvertFromString("#2D2D2D"),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                ToolTip = "Paste your pattern here (e.g. [dx, dy] or dx, dy lines)"
            };
            RecoilScroller.Children.Add(patternInput);

            AButton ImportPattern = new(this, "Import and Save Pattern",
                "Parses the text above and saves it as a new JSON recoil file.");
            ImportPattern.Reader.Click += (s, e) =>
            {
                string text = patternInput.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Please paste some pattern data first!", "Importer");
                    return;
                }

                var steps = ParseImportedPattern(text);
                if (steps.Count == 0)
                {
                    MessageBox.Show("Could not find any valid [dx, dy] steps in the text. Ensure it follows a format like: [10, 20] or 10, 20", "Import Error");
                    return;
                }

                string fileName = "ImportedPattern_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                string fullPath = Path.Combine(RecoilFolderPath, fileName);

                try
                {
                    var patternData = new { steps = steps };
                    File.WriteAllText(fullPath, JsonConvert.SerializeObject(patternData, Formatting.Indented));

                    if (TryLoadRecoilPatternFromFile(fullPath, out string error))
                    {
                        MessageBox.Show($"Successfully imported {steps.Count} steps as {fileName}!", "Import Success");
                        patternInput.Clear();
                        _recoilStatusTextBlock.Text = BuildRecoilPatternStatusText();
                        RefreshRecoilPatternGraph();
                    }
                    else
                    {
                        MessageBox.Show($"Saved but failed to load: {error}", "Import Partial Success");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save imported pattern: {ex.Message}", "Error");
                }
            };
            RecoilScroller.Children.Add(ImportPattern);

            ASlider RecoilScale = new(this, "Recoil Strength", "Multiplier",
                "Scales the X/Y movement values from your pattern. Higher numbers mean stronger compensation.",
                1);

            RecoilScale.Slider.Minimum = 1;
            RecoilScale.Slider.Maximum = 10;
            RecoilScale.Slider.Value = Math.Round(GetSettingDouble("Recoil_Scale", 1.0));
            RecoilScale.Slider.TickFrequency = 1;
            RecoilScale.Slider.ValueChanged += (s, e) =>
            {
                aimmySettings["Recoil_Scale"] = Math.Round(RecoilScale.Slider.Value);
            };
            RecoilScroller.Children.Add(RecoilScale);

            ASlider RecoilSpeed = new(this, "Recoil Speed", "Multiplier",
                "Higher values replay the pattern faster. 1.0 = recorded speed.",
                0.01);

            RecoilSpeed.Slider.Minimum = 0.1;
            RecoilSpeed.Slider.Maximum = 4;
            RecoilSpeed.Slider.Value = GetSettingDouble("Recoil_SpeedMultiplier", 1.0);
            RecoilSpeed.Slider.TickFrequency = 0.01;
            RecoilSpeed.Slider.ValueChanged += (s, e) =>
            {
                aimmySettings["Recoil_SpeedMultiplier"] = RecoilSpeed.Slider.Value;
            };
            RecoilScroller.Children.Add(RecoilSpeed);

            ASlider RecoilDefaultDelay = new(this, "Fallback Step Delay", "Milliseconds",
                "Used when a pattern step has no delay value.",
                1);

            RecoilDefaultDelay.Slider.Minimum = 1;
            RecoilDefaultDelay.Slider.Maximum = 250;
            RecoilDefaultDelay.Slider.Value = GetSettingDouble("Recoil_DefaultStepDelay", 16);
            RecoilDefaultDelay.Slider.TickFrequency = 1;
            RecoilDefaultDelay.Slider.ValueChanged += (s, e) =>
            {
                aimmySettings["Recoil_DefaultStepDelay"] = RecoilDefaultDelay.Slider.Value;
            };
            RecoilScroller.Children.Add(RecoilDefaultDelay);

            ASlider RapidFireDelay = new(this, "Rapid Fire Delay", "Milliseconds",
                "How quickly the rapid-fire helper repeats clicks while Mouse1 is held.",
                1);

            RapidFireDelay.Slider.Minimum = 25;
            RapidFireDelay.Slider.Maximum = 250;
            RapidFireDelay.Slider.Value = GetSettingDouble("Recoil_RapidFireDelayMs", 90);
            RapidFireDelay.Slider.TickFrequency = 1;
            RapidFireDelay.Slider.ValueChanged += (s, e) =>
            {
                aimmySettings["Recoil_RapidFireDelayMs"] = RapidFireDelay.Slider.Value;
            };
            RecoilScroller.Children.Add(RapidFireDelay);

            
        }

        private void FileWatcher_Reload(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                LoadModelsIntoListBox();

                // Maybe I broke something removing this, fix it, because it was causing a bug where ListBox stops working when something was added =)
                // nori
                try
                {
                    DpiScale dpi = VisualTreeHelper.GetDpi(this);
                    AIModel.DpiScaleX = dpi.DpiScaleX;
                    AIModel.DpiScaleY = dpi.DpiScaleY;

                    InitializeModel();
                }
                catch (Exception ex)
                {
                    // Log or handle the exception if DPI or model initialization fails
                    MessageBox.Show($"Error initializing model after file change: {ex.Message}");
                }
            });
        }

        private void InitializeFileWatcher()
        {
            fileWatcher = new FileSystemWatcher();
            fileWatcher.Path = "bin/models";
            fileWatcher.Filter = "*.onnx";
            fileWatcher.EnableRaisingEvents = true;
            fileWatcher.Created += FileWatcher_Reload;
            fileWatcher.Deleted += FileWatcher_Reload;
            fileWatcher.Renamed += FileWatcher_Reload;
        }

        private bool ModelLoadDebounce = false;

        private void InitializeModel()
        {
            if (!ModelLoadDebounce)
            {
                ModelLoadDebounce = true;

                string selectedModel = SelectorListBox.SelectedItem?.ToString();
                if (selectedModel == null) 
                {
                    ModelLoadDebounce = false;
                    return;
                }

                AIModel newModel = null;
                try
                {
                    string modelPath = Path.Combine("bin/models", selectedModel);
                    newModel = new AIModel(modelPath)
                    {
                        ConfidenceThreshold = (float)(GetSettingDouble("AI_Min_Conf", 5.0) / 100.0),
                        CollectData = toggleState["CollectData"],
                        FovSize = GetSettingInt("FOV_Size", 320),
                        OutputBoxFormat = ParseBoxFormat(GetSettingString("Aim_BoxFormat", "center"))
                    };

                    _modelAccessGate.Wait();
                    try
                    {
                        var previousModel = _onnxModel;
                        _onnxModel = newModel;
                        newModel = null;
                        previousModel?.Dispose();
                    }
                    finally
                    {
                        _modelAccessGate.Release();
                    }

                    // Ensure the debug overlay's static action points to the NEW model instance
                    ConfigureSaveFrameAction();
                    ResetAimMovementState();

                    SelectedModelNotifier.Content = "Loaded Model: " + selectedModel;
                    lastLoadedModel = selectedModel;
                    SaveSessionState();

                    // Log monitor info for coordinate mapping diagnostics
                    var dpi = VisualTreeHelper.GetDpi(this);
                    Log($"Monitor Diagnostic: PhysRes={System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width}x{System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height} DPI={dpi.DpiScaleX:F2}");
                    Visualization.DebugOverlay.AddLog($"AI: Monitor={System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width}x{System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height} (DPI={dpi.DpiScaleX:F2})");
                }
                catch (Exception)
                {
                    newModel?.Dispose();
                    SelectedModelNotifier.Content = "Failed to load: " + selectedModel;
                    // The error message is already shown by AIModel's constructor via MessageBox
                }
                finally
                {
                    ModelLoadDebounce = false;
                }
            }
        }

        private void LoadModelsIntoListBox()
        {
            string[] onnxFiles = Directory.GetFiles("bin/models", "*.onnx");
            SelectorListBox.Items.Clear();

            foreach (string filePath in onnxFiles)
            {
                SelectorListBox.Items.Add(Path.GetFileName(filePath));
            }

            if (SelectorListBox.Items.Count > 0)
            {
                string firstModel = SelectorListBox.Items[0].ToString();

                if (!SelectorListBox.Items.Contains(lastLoadedModel) || lastLoadedModel == "N/A")
                {
                    SelectorListBox.SelectedIndex = 0;
                    lastLoadedModel = firstModel;
                }
                else
                {
                    SelectorListBox.SelectedItem = lastLoadedModel;
                }

                SelectedModelNotifier.Content = "Loaded Model: " + lastLoadedModel;
            }
            ModelLoadDebounce = false;
        }

        private void SelectorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            InitializeModel();
        }

        private async Task LoadConfigAsync(string path)
        {
            if (File.Exists(path))
            {
                string json = await File.ReadAllTextAsync(path);

                var config = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);
                if (config != null)
                {
                    foreach (var (key, value) in config)
                    {
                        if (aimmySettings.TryGetValue(key, out var currentValue))
                        {
                            aimmySettings[key] = value;

                            if (key == "Show_MiniHud" && value is bool showHudValue)
                            {
                                Bools.ShowMiniHud = showHudValue;
                                if (_hudOverlay != null)
                                {
                                    if (showHudValue) _hudOverlay.Show();
                                    else _hudOverlay.Hide();
                                }
                            }
                            else if (key == "Hardware_UseArduino" && value is bool useArduino)
                            {
                                Bools.UseHardwareMouse = useArduino;
                                if (useArduino)
                                    HardwareMouse.Initialize(Bools.ArduinoComPort);
                                else
                                    HardwareMouse.Dispose();
                            }
                            else if (key == "Hardware_ComPort" && value != null)
                            {
                                Bools.ArduinoComPort = value.ToString();
                                if (Bools.UseHardwareMouse)
                                    HardwareMouse.Initialize(Bools.ArduinoComPort);
                            }
                        }
                        else if (key == "ToggleState")
                        {
                            try
                            {
                                Dictionary<string, bool> toggleSnapshot = value is Newtonsoft.Json.Linq.JObject toggleObject
                                    ? toggleObject.ToObject<Dictionary<string, bool>>()
                                    : JsonConvert.DeserializeObject<Dictionary<string, bool>>(value?.ToString() ?? string.Empty);

                                ApplyToggleSnapshot(toggleSnapshot);
                            }
                            catch (Exception ex)
                            {
                                Log($"ToggleState load failed: {ex.Message}");
                            }
                        }
                        else if (key == "TopMost" && value is bool topMostValue)
                        {
                            toggleState["TopMost"] = topMostValue;
                            this.Topmost = topMostValue;
                        }
                    }
                }

                SyncAimSettingsWithPresetIfSelected();
                ApplyMenuAccentColor(GetSettingString("GUI_AccentColor", DefaultMenuAccentColor));
                SyncRecoilSettingsFromConfig();
                _patternCycleBindingManager?.SetBinding(GetSettingString("Recoil_CycleKey", "F11"));
                _recoilRecordingBindingManager?.SetBinding(GetSettingString("Recoil_RecordingKey", "F12"));
                bool recoilLoaded = TryLoadConfiguredRecoilPattern(out string recoilError);
                if (!recoilLoaded
                    && aimmySettings.TryGetValue("Recoil_PatternPath", out var configuredRecoilPath)
                    && !string.IsNullOrWhiteSpace(configuredRecoilPath?.ToString()))
                {
                    Log($"Recoil pattern load failed: {recoilError}");
                }
            }
            if (aimmySettings["Suggested_Model"] != string.Empty)
            {
                MessageBox.Show("The creator of this model suggests you use this model:" +
                    "\n" +
                    aimmySettings["Suggested_Model"], "Suggested Model - DustyAim");
            }

            // We'll attempt to update the AI Settings but it may not be loaded yet.
            try
            {
                int fovSize = GetSettingInt("FOV_Size", 320);
                FOVOverlay.FovSize = fovSize;
                AwfulPropertyChanger.PostNewFOVSize();

                if (_onnxModel != null)
                {
                    ApplyModelSettings(_onnxModel);
                }
            }
            catch { }

            ResetAimMovementState();

            string fileName = Path.GetFileName(path);
            if (ConfigSelectorListBox.Items.Contains(fileName))
            {
                ConfigSelectorListBox.SelectedItem = fileName;
                var configName = ConfigSelectorListBox.SelectedItem?.ToString();
                if (configName != null) lastLoadedConfig = configName;
                SetSelectedConfig();
            }
        }

        private async Task LoadOverlayPropertiesAsync(string path)
        {
            if (File.Exists(path))
            {
                string json = await File.ReadAllTextAsync(path);

                var config = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);
                if (config != null)
                {
                    foreach (var (key, value) in config)
                    {
                        if (OverlayProperties.TryGetValue(key, out var currentValue))
                        {
                            OverlayProperties[key] = value;
                        }
                    }
                }
            }

            try
            {
                AwfulPropertyChanger.PostColor((Color)ColorConverter.ConvertFromString(OverlayProperties["FOV_Color"]));
                AwfulPropertyChanger.PostPDWSize((int)OverlayProperties["PDW_Size"]);
                AwfulPropertyChanger.PostPDWCornerRadius((int)OverlayProperties["PDW_CornerRadius"]);
                AwfulPropertyChanger.PostPDWBorderThickness((int)OverlayProperties["PDW_BorderThickness"]);
                AwfulPropertyChanger.PostPDWOpacity((Double)OverlayProperties["PDW_Opacity"]);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            //ReloadMenu();
        }

        private void ReloadMenu()
        {
            AimScroller.Children.Clear();
            TriggerScroller.Children.Clear();
            RecoilScroller.Children.Clear();
            SettingsScroller.Children.Clear();

            LoadAimMenu();
            LoadTriggerMenu();
            LoadRecoilMenu();
            LoadSettingsMenu();
        }

        private void SyncRecoilSettingsFromConfig()
        {
            SyncToggleWithSetting("RecoilControl", "Recoil_ControlEnabled");
            SyncToggleWithSetting("RecoilLoopPattern", "Recoil_LoopPattern");
            SyncToggleWithSetting("RecoilAdsOnly", "Recoil_AdsOnly");
            SyncToggleWithSetting("RecoilRapidFire", "Recoil_RapidFire");
        }

        private void SyncToggleWithSetting(string toggleName, string settingName)
        {
            if (!toggleRegistry.TryGetValue(toggleName, out _))
                return;

            bool fallback = toggleState.TryGetValue(toggleName, out bool current) ? current : false;
            bool targetState = GetSettingBool(settingName, fallback);
            ForceToggle(toggleName, targetState);
        }

        private void ConfigWatcher_Reload(object sender, FileSystemEventArgs e)
        {
            this.Dispatcher.Invoke(LoadConfigsIntoListBox);
        }

        private void InitializeConfigWatcher()
        {
            ConfigfileWatcher = new FileSystemWatcher();
            ConfigfileWatcher.Path = "bin/configs";
            ConfigfileWatcher.Filters.Add("*.json");
            ConfigfileWatcher.Filters.Add("*.cfg");
            ConfigfileWatcher.EnableRaisingEvents = true;
            ConfigfileWatcher.Created += ConfigWatcher_Reload;
            ConfigfileWatcher.Deleted += ConfigWatcher_Reload;
            ConfigfileWatcher.Renamed += ConfigWatcher_Reload;
        }

        private void LoadConfigsIntoListBox()
        {
            ConfigSelectorListBox.Items.Clear();

            foreach (string filePath in Directory.GetFiles("bin/configs"))
            {
                string fileName = Path.GetFileName(filePath);
                ConfigSelectorListBox.Items.Add(fileName);
            }

            //SetSelectedConfig();
        }

        private void SetSelectedConfig()
        {
            if (ConfigSelectorListBox.Items.Count > 0)
            {
                if (!ConfigSelectorListBox.Items.Contains(lastLoadedConfig) && lastLoadedConfig != "N/A")
                {
                    ConfigSelectorListBox.SelectedIndex = 0;
                    lastLoadedConfig = ConfigSelectorListBox.Items[0].ToString();
                }
                else
                {
                    ConfigSelectorListBox.SelectedItem = lastLoadedConfig;
                }
                SelectedConfigNotifier.Content = "Loaded Config: " + lastLoadedConfig;
            }
        }

        private async void ConfigSelectorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConfigSelectorListBox.SelectedItem != null)
            {
                await LoadConfigAsync($"bin/configs/{ConfigSelectorListBox.SelectedItem.ToString()}");
                ReloadMenu();
                SaveSessionState();
            }
        }

        private void LoadStoreMenu()
        {
            DownloadGateway(ModelStoreScroller, AvailableModels, LackOfModelsText);
            DownloadGateway(ConfigStoreScroller, AvailableConfigs, LackOfConfigsText);
        }

        private void DownloadGateway(StackPanel scroller, IReadOnlyList<DownloadItem> entries, Label lackOfLabel)
        {
            scroller.Children.Clear();
            if (entries.Count == 0)
            {
                lackOfLabel.Visibility = Visibility.Visible;
                return;
            }

            lackOfLabel.Visibility = Visibility.Collapsed;
            foreach (var entry in entries)
            {
                scroller.Children.Add(new ADownloadGateway(entry));
            }
        }

        private void LoadSettingsMenu()
        {
            SettingsScroller.Children.Add(new AInfoSection());

            AToggle CollectDataWhilePlaying = new(this, "Collect Data While Playing",
                "This will enable the AI's ability to save frames to 'captures/dataset' every 0.5s while this toggle is on.");
            CollectDataWhilePlaying.Reader.Name = "CollectData";
            SetupToggle(CollectDataWhilePlaying, state => Bools.CollectDataWhilePlaying = state, Bools.CollectDataWhilePlaying);
            SettingsScroller.Children.Add(CollectDataWhilePlaying);

            ASlider AIMinimumConfidence = new(this, "AI Minimum Confidence", "% Confidence",
                "This setting controls how confident the AI needs to be before making the decision to aim.",
                1);

            AIMinimumConfidence.Slider.Minimum = 1;
            AIMinimumConfidence.Slider.Maximum = 100;
            AIMinimumConfidence.Slider.Value = GetSettingDouble("AI_Min_Conf", 5);
            AIMinimumConfidence.Slider.TickFrequency = 1;
            AIMinimumConfidence.Slider.ValueChanged += (s, x) =>
            {
                if (lastLoadedModel != "N/A" && _onnxModel != null)
                {
                    double ConfVal = ((double)AIMinimumConfidence.Slider.Value);
                    aimmySettings["AI_Min_Conf"] = ConfVal;
                    _onnxModel.ConfidenceThreshold = (float)(ConfVal / 100.0f);
                }
                else
                {
                    // Prevent double messageboxes..
                    if (AIMinimumConfidence.Slider.Value != GetSettingDouble("AI_Min_Conf", 5))
                    {
                        MessageBox.Show("Unable to set confidence, please select a model and try again.", "Slider Error");
                        AIMinimumConfidence.Slider.Value = GetSettingDouble("AI_Min_Conf", 5);
                    }
                }
            };

            SettingsScroller.Children.Add(AIMinimumConfidence);

            bool topMostInitialState = toggleState.ContainsKey("TopMost") ? toggleState["TopMost"] : false;

            AToggle TopMost = new(this, "UI TopMost",
                "This will toggle the UI's TopMost, meaning it can hide behind other windows vs always being on top.");
            TopMost.Reader.Name = "TopMost";
            SetupToggle(TopMost, state => Bools.TopMost = state, topMostInitialState);

            SettingsScroller.Children.Add(TopMost);

            AToggle ShowMiniHud = new(this, "Show Mini HUD",
                "Shows a compact always-on-top status overlay with current aim and recoil state.");
            ShowMiniHud.Reader.Name = "ShowMiniHud";
            SetupToggle(ShowMiniHud, state =>
            {
                Bools.ShowMiniHud = state;
                aimmySettings["Show_MiniHud"] = state;
                if (state)
                    _hudOverlay?.Show();
                else
                    _hudOverlay?.Hide();

                UpdateHudOverlayState();
            }, GetSettingBool("Show_MiniHud", Bools.ShowMiniHud));
            SettingsScroller.Children.Add(ShowMiniHud);

            AToggle UnlockMiniHud = new(this, "Unlock Mini HUD Position",
                "Allows you to drag the Mini HUD around. When checked, clicks will not pass through the HUD.");
            UnlockMiniHud.Reader.Name = "MiniHud_UnlockPosition";
            SetupToggle(UnlockMiniHud, state =>
            {
                aimmySettings["MiniHud_UnlockPosition"] = state;
                if (_hudOverlay != null)
                {
                    _hudOverlay.SetUnlocked(state);
                }
            }, GetSettingBool("MiniHud_UnlockPosition", false));
            SettingsScroller.Children.Add(UnlockMiniHud);

            ASlider MiniHudOpacity = new(this, "Mini HUD Opacity", "Value",
                "Changes how transparent the Mini HUD is.",
                0.05);
            MiniHudOpacity.Slider.Minimum = 0.1;
            MiniHudOpacity.Slider.Maximum = 1.0;
            MiniHudOpacity.Slider.Value = GetSettingDouble("MiniHud_Opacity", 1.0);
            MiniHudOpacity.Slider.TickFrequency = 0.05;
            MiniHudOpacity.Slider.ValueChanged += (s, e) =>
            {
                aimmySettings["MiniHud_Opacity"] = MiniHudOpacity.Slider.Value;
                UpdateHudOverlayState();
            };
            SettingsScroller.Children.Add(MiniHudOpacity);

            ASlider MiniHudScale = new(this, "Mini HUD Scale", "Multiplier",
                "Changes the size of the Mini HUD.",
                0.05);
            MiniHudScale.Slider.Minimum = 0.5;
            MiniHudScale.Slider.Maximum = 2.0;
            MiniHudScale.Slider.Value = GetSettingDouble("MiniHud_Scale", 1.0);
            MiniHudScale.Slider.TickFrequency = 0.05;
            MiniHudScale.Slider.ValueChanged += (s, e) =>
            {
                aimmySettings["MiniHud_Scale"] = MiniHudScale.Slider.Value;
                UpdateHudOverlayState();
            };
            SettingsScroller.Children.Add(MiniHudScale);

            AColorChanger AccentColor = new("GUI Accent Color");
            AccentColor.ColorChangingBorder.Background = GetMenuAccentBrush();
            AccentColor.Reader.Click += (s, e) =>
            {
                System.Windows.Forms.ColorDialog colorDialog = new();
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    Color pickedColor = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                    AccentColor.ColorChangingBorder.Background = new SolidColorBrush(pickedColor);
                    ApplyMenuAccentColor(pickedColor.ToString());
                }
            };
            SettingsScroller.Children.Add(AccentColor);

            AButton SaveConfigSystem = new(this, "Save Config Profile",
    "This will save the current config for the purposes of publishing.");

            SaveConfigSystem.Reader.Click += (s, e) =>
            {
                new SecondaryWindows.ConfigSaver(aimmySettings, lastLoadedModel, BuildToggleSnapshot()).ShowDialog();
            };

            SettingsScroller.Children.Add(SaveConfigSystem);

            #region Hardware Mouse (Arduino)
            SettingsScroller.Children.Add(new ALabel("Hardware Mouse (Arduino)"));

            AToggle UseHardwareMouse = new(this, "Use Arduino Hardware Input",
                "When enabled, mouse movements and clicks are sent via a physical Arduino Leonardo/Micro.");
            UseHardwareMouse.Reader.Name = "UseHardwareMouse";
            SetupToggle(UseHardwareMouse, state => {
                Bools.UseHardwareMouse = state;
                aimmySettings["Hardware_UseArduino"] = state;
                if (state) HardwareMouse.Initialize(Bools.ArduinoComPort);
                else HardwareMouse.Dispose();
            }, GetSettingBool("Hardware_UseArduino", Bools.UseHardwareMouse));
            SettingsScroller.Children.Add(UseHardwareMouse);

            AKeyChanger CycleComPort = new("COM Port", Bools.ArduinoComPort);
            CycleComPort.Reader.Click += (s, e) =>
            {
                string[] ports = new[] { "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9" };
                int currentIndex = Array.IndexOf(ports, Bools.ArduinoComPort);
                int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % ports.Length;
                string nextPort = ports[nextIndex];

                Bools.ArduinoComPort = nextPort;
                aimmySettings["Hardware_ComPort"] = nextPort;
                CycleComPort.KeyNotifier.Content = nextPort;

                if (Bools.UseHardwareMouse) HardwareMouse.Initialize(nextPort);
            };
            SettingsScroller.Children.Add(CycleComPort);

            AButton TestArduino = new(this, "Test Arduino Connection",
                "Attempts a quick serial open/close on the selected COM port.");
            TestArduino.Reader.Click += (s, e) =>
            {
                bool ok = HardwareMouse.TestConnection(out string message);
                MessageBox.Show(message, ok ? "Arduino Connection" : "Arduino Connection Error");
            };
            SettingsScroller.Children.Add(TestArduino);
            #endregion Hardware Mouse (Arduino)
        }

        #region Window Controls

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private static bool SavedData = false;

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Prevent saving overwrite
            if (SavedData) return;

            // Save to Default Config
            try
            {
                var extendedSettings = new Dictionary<string, object>();
                foreach (var kvp in aimmySettings)
                {
                    extendedSettings[kvp.Key] = kvp.Value;
                }

                // Add topmost
                extendedSettings["TopMost"] = this.Topmost ? true : false;

                string json = JsonConvert.SerializeObject(extendedSettings, Formatting.Indented);
                File.WriteAllText("bin/configs/Default.cfg", json);
            }
            catch (Exception x)
            {
                Console.WriteLine("Error saving configuration: " + x.Message);
            }

            // Save Overlay Properties Data
            // Nori
            try
            {
                var OverlaySettings = new Dictionary<string, object>();
                foreach (var kvp in OverlayProperties)
                {
                    OverlaySettings[kvp.Key] = kvp.Value;
                }

                string json = JsonConvert.SerializeObject(OverlaySettings, Formatting.Indented);
                File.WriteAllText("bin/Overlay.cfg", json);
            }
            catch (Exception x)
            {
                Console.WriteLine("Error saving configuration: " + x.Message);
            }

            BuildToggleSnapshot();
            SaveSessionState();
            SavedData = true;

            // Unhook keybind hooker
            bindingManager.StopListening();
            FOVOverlay.Close();
            DetectedPlayerOverlay.Close();

            // Dispose (best practice)
            fileWatcher?.Dispose();
            ConfigfileWatcher?.Dispose();
            cts?.Dispose();
            _rapidFireLoopCts?.Cancel();
            _rapidFireLoopCts?.Dispose();

            // Close
            Application.Current.Shutdown();
        }

        private async void StartRecoilRecordingLoop()
        {
            while (true)
            {
                try
                {
                    if (_isRecordingRecoil)
                    {
                        bool isM1Down = (System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) == System.Windows.Forms.MouseButtons.Left;
                        if (isM1Down)
                        {
                            var currentPos = System.Windows.Forms.Cursor.Position;
                            int dx = (int)(currentPos.X - _lastRecordMousePos.X);
                            int dy = (int)(currentPos.Y - _lastRecordMousePos.Y);

                            if (dx != 0 || dy != 0)
                            {
                                _recordedRecoilSteps.Add(new RecoilPatternStep { Dx = dx, Dy = dy, DelayMs = 10 });
                            }
                            _lastRecordMousePos = currentPos;
                        }
                        else
                        {
                            _lastRecordMousePos = System.Windows.Forms.Cursor.Position;
                        }
                    }
                    ResetLoopFailureState(ref _recoilRecordingLoopErrorCount, ref _recoilRecordingLoopErrorWindowStartUtc);
                }
                catch (Exception ex)
                {
                    bool shouldStop = RegisterLoopFailure("RecoilRecording", ex, ref _recoilRecordingLoopErrorCount, ref _recoilRecordingLoopErrorWindowStartUtc);
                    if (shouldStop)
                        break;

                    await Task.Delay(500);
                }

                await Task.Delay(10);
            }
        }

        private void ToggleRecoilRecording()
        {
            _isRecordingRecoil = !_isRecordingRecoil;
            if (_isRecordingRecoil)
            {
                _recordedRecoilSteps.Clear();
                _lastRecordMousePos = System.Windows.Forms.Cursor.Position;
                System.Media.SystemSounds.Exclamation.Play();
            }
            else
            {
                System.Media.SystemSounds.Hand.Play();
                if (_recordedRecoilSteps.Count > 0)
                {
                    MessageBox.Show($"Recording stopped. Captured {_recordedRecoilSteps.Count} steps. Don't forget to save it in the Recoil menu!", "Recoil Recorder");
                }
            }
        }

        #endregion Window Controls
    }
}
