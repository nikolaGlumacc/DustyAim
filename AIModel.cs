using AimmyWPF.Class;
using KdTree;
using KdTree.Math;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Visualization;

namespace AimmyAimbot
{
    public class AIModel : IDisposable
    {
        private int _modelInputWidth = 640;
        private int _modelInputHeight = 640;
        
        private readonly RunOptions _modeloptions;
        private InferenceSession _onnxModel;

        public float ConfidenceThreshold = 0.6f;
        public bool CollectData = false;
        public int FovSize = 640;
        /// <summary>Current cursor position relative to the capture box. Used for FOV-priority targeting.</summary>
        public System.Drawing.Point CurrentCursorOffset { get; set; } = new System.Drawing.Point(-1, -1);
        public enum BoxFormat
        {
            Auto = 0,
            Center = 1,
            TopLeft = 2
        }

        public BoxFormat OutputBoxFormat { get; set; } = BoxFormat.Auto;

        private DateTime lastSavedTime = DateTime.MinValue;
        private string _inputName;
        private string _outputName;
        private int _numClasses;
        private int _numAnchors;
        private bool _isV8Style = true; // [1, 4+C, N] vs [1, N, 5+C]
        private bool _isEndToEnd = false; // [1, N, 6] (x1, y1, x2, y2, score, class)

        private Bitmap _screenCaptureBitmap = null;
        private readonly object _captureLock = new();
        public static double DpiScaleX = 1.0;
        public static double DpiScaleY = 1.0;
        public Rectangle CaptureScreenBounds { get; set; } = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        private int _logCounter = 0;
        private DateTime _lastScreenGrabErrorLog = DateTime.MinValue;
        private string _lastScreenGrabError = string.Empty;

        private readonly struct LetterboxInfo
        {
            public LetterboxInfo(float scale, float padX, float padY)
            {
                Scale = scale;
                PadX = padX;
                PadY = padY;
            }

            public float Scale { get; }
            public float PadX { get; }
            public float PadY { get; }
        }

        public int ModelInputWidth => _modelInputWidth;
        public int ModelInputHeight => _modelInputHeight;

        /// <summary>Saves the current screen capture to disk for visual debug inspection.</summary>
        public void SaveLastFrame()
        {
            Rectangle box = GetPhysicalCaptureBox();
            Log($"[Save] Requesting frame capture at {box.X},{box.Y} {box.Width}x{box.Height}");

            if (box.Width <= 0 || box.Height <= 0)
            {
                Log($"[Save] Error: Invalid capture box {box.Width}x{box.Height}.");
                Visualization.DebugOverlay.AddLog("Save Error: Invalid capture box.");
                return;
            }

            try
            {
                using Bitmap saveCopy = CaptureToNewBitmap(box);
                Visualization.DebugOverlay.AddLog("AI: Initializing save...");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = Path.Combine(baseDir, "captures");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                string path = Path.Combine(dir, $"debug_frame_{DateTime.Now:HHmmss}.png");

                saveCopy.Save(path, System.Drawing.Imaging.ImageFormat.Png);

                string fullPath = Path.GetFullPath(path);
                Log($"Frame saved to: {fullPath}");
                Visualization.DebugOverlay.AddLog($"Frame saved → {fullPath}");
            }
            catch (Exception ex) 
            { 
                Log($"SaveLastFrame error: {ex.Message}"); 
                DebugOverlay.AddLog($"Save Error: {ex.Message}");
            }
        }

        public AIModel(string modelPath)
        {
            Log($"Initializing AIModel: {modelPath}");
            _modeloptions = new RunOptions();

            var sessionOptions = new SessionOptions
            {
                EnableCpuMemArena = true,
                EnableMemoryPattern = true,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_PARALLEL
            };

            string fullPath = Path.GetFullPath(modelPath);
            if (!File.Exists(fullPath))
            {
                Log("Error: Model file not found.");
                throw new FileNotFoundException("Model file not found.", fullPath);
            }

            if (new FileInfo(fullPath).Length == 0)
            {
                Log("Error: Model file is 0 bytes.");
                MessageBox.Show($"The model file is empty (0 bytes): \n{modelPath}\n\nThis usually means the download failed. Please try deleting the file and downloading it again.", "Download Failed");
                throw new Exception("Model file is empty.");
            }

            try
            {
                Log("Attempting to load via DirectML...");
                LoadViaDirectML(sessionOptions, fullPath);
                Log("Successfully loaded via DirectML.");
            }
            catch (Exception ex)
            {
                var dmlError = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
                Log($"DirectML Load Failed: {dmlError}");

                if (ex.Message.Contains("ModelProto does not have a graph"))
                {
                    MessageBox.Show($"The model file metadata is missing or corrupted: \n{modelPath}\n\nThis usually means the file was not downloaded correctly. Please try redownloading the model.", "Model Corrupted");
                    throw;
                }
                
                MessageBox.Show($"There was an error starting the OnnxModel via DirectML: {dmlError}\n\nProgram will attempt to use CPU only, performance may be poor.", "Model Error");
                
                sessionOptions.Dispose();
                sessionOptions = new SessionOptions(); 
                Log("Attempting to load via CPU...");
                LoadViaCPU(sessionOptions, fullPath);
                Log("Successfully loaded via CPU.");
            }

            InitializeMetadata();
        }

        private void Log(string message)
        {
            try {
                File.AppendAllText("dusty_debug.log", $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            } catch { }
        }

        private void LogScreenGrabException(Exception ex)
        {
            if (ex == null) return;

            string message = ex.Message;
            DateTime now = DateTime.UtcNow;
            if (message != _lastScreenGrabError || (now - _lastScreenGrabErrorLog).TotalMilliseconds >= 750)
            {
                Log($"ScreenGrab Exception: {message}");
                _lastScreenGrabError = message;
                _lastScreenGrabErrorLog = now;
            }
        }

        private void EnsureCaptureBitmapSize(Rectangle detectionBox)
        {
            lock (_captureLock)
            {
                if (_screenCaptureBitmap == null || _screenCaptureBitmap.Width != detectionBox.Width || _screenCaptureBitmap.Height != detectionBox.Height)
                {
                    _screenCaptureBitmap?.Dispose();
                    _screenCaptureBitmap = new Bitmap(detectionBox.Width, detectionBox.Height);
                    Log($"ScreenGrab: Re-allocated bitmap to {detectionBox.Width}x{detectionBox.Height}");
                }
            }
        }

        private void ResetCaptureBitmapNoThrow()
        {
            lock (_captureLock)
            {
                try { _screenCaptureBitmap?.Dispose(); }
                catch { }
                _screenCaptureBitmap = null;
            }
        }

        private static Bitmap CaptureToNewBitmap(Rectangle detectionBox)
        {
            Bitmap bitmap = new Bitmap(detectionBox.Width, detectionBox.Height);
            try
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(detectionBox.Left, detectionBox.Top, 0, 0, detectionBox.Size);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private void InitializeMetadata()
        {
            if (_onnxModel == null) return;

            try
            {
                // Get Input Info
                if (_onnxModel.InputMetadata.Count == 0) throw new Exception("Model has no inputs.");
                var inputMeta = _onnxModel.InputMetadata.First();
                _inputName = inputMeta.Key;
                var inputShape = inputMeta.Value.Dimensions;
                
                // Expecting [1, 3, H, W] or [1, H, W, 3]
                if (inputShape.Length == 4) {
                    if (inputShape[1] == 3) { // [1, 3, H, W]
                        _modelInputHeight = inputShape[2];
                        _modelInputWidth = inputShape[3];
                    } else if (inputShape[3] == 3) { // [1, H, W, 3]
                        _modelInputHeight = inputShape[1];
                        _modelInputWidth = inputShape[2];
                    }
                }
                
                // Fallback for dynamic/negative shapes (e.g. YOLOv11 showing as -1x-1)
                if (_modelInputWidth <= 0) _modelInputWidth = 640;
                if (_modelInputHeight <= 0) _modelInputHeight = 640;

                Log($"Model Input detected: {_inputName} ({_modelInputWidth}x{_modelInputHeight})");
                Visualization.DebugOverlay.AddLog($"AI: Model loaded! Input={_modelInputWidth}x{_modelInputHeight}");

                // Get Output Info
                if (_onnxModel.OutputMetadata.Count == 0) throw new Exception("Model has no outputs.");
                var outputMeta = _onnxModel.OutputMetadata.First();
                _outputName = outputMeta.Key;
                var shape = outputMeta.Value.Dimensions;
                Log($"Model Output detected: {_outputName} (Shape: {string.Join("x", shape)})");
                Log("INFO: Output shape like 1x5x8400 is normal (8400 detections, 5 values each).");

                // Handle possible 4D output [1, 1, N, C] by treating it as 3D [1, N, C]
                if (shape.Length == 4 && shape[0] == 1 && shape[1] == 1) {
                    shape = new int[] { 1, shape[2], shape[3] };
                }

                if (shape.Length == 3)
                {
                    if (shape[1] < shape[2])
                    {
                        if (shape[1] == 4 || shape[1] == 5) // YOLOv8/v11 with very few classes? unlikely. 
                        {
                             // Usually [1, 4+C, N] or [1, N, 6]
                        }
                        
                        _isV8Style = true;
                        _numClasses = shape[1] - 4;
                        _numAnchors = shape[2];
                        _isEndToEnd = false;
                    }
                    else
                    {
                        _isV8Style = false;
                        _numAnchors = shape[1];
                        _isEndToEnd = false;
                        
                        // Check for End-to-End (YOLOv10/v12) [1, 300, 6]
                        if (shape[2] == 6) {
                            _isEndToEnd = true;
                            _numClasses = 1; // It's usually a single class or class index is at index 5
                            Log("Detected End-to-End model format (v10/v12 style).");
                        } else {
                            _numClasses = shape[2] - 5;
                        }
                    }
                }
                else
                {
                    Log($"Warning: Unexpected model output shape index 0 has {shape.Length} dimensions.");
                    MessageBox.Show($"Unexpected model output shape: {string.Join("x", shape)}. Detection might fail.", "Model Warning");
                    _numAnchors = 0;
                    _numClasses = 0;
                }
            }
            catch (Exception ex)
            {
                Log($"Metadata Initialization Error: {ex.Message}");
                MessageBox.Show($"Error initializing model metadata: {ex.Message}", "Init Error");
                throw;
            }
        }

        private void LoadViaDirectML(SessionOptions sessionOptions, string modelPath)
        {
            sessionOptions.AppendExecutionProvider_DML(0);
            _onnxModel = new InferenceSession(modelPath, sessionOptions);
        }

        private void LoadViaCPU(SessionOptions sessionOptions, string modelPath)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CPU();
                _onnxModel = new InferenceSession(modelPath, sessionOptions);
            }
            catch (Exception e)
            {
                Log($"CPU Load Failed: {e.Message}");
                MessageBox.Show($"Error starting the model via CPU: {e.Message}");
                System.Windows.Application.Current.Shutdown();
            }
        }

        public class Prediction
        {
            public RectangleF Rectangle { get; set; }
            public float Confidence { get; set; }
        }

        public static float AIConfidence { get; set; }

        public Bitmap ScreenGrab(Rectangle detectionBox)
        {
            if (detectionBox.Width <= 0 || detectionBox.Height <= 0)
            {
                Log($"ScreenGrab Error: Invalid capture box {detectionBox.Width}x{detectionBox.Height}");
                return null;
            }

            Exception lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    EnsureCaptureBitmapSize(detectionBox);

                    lock (_captureLock)
                    {
                        if (_screenCaptureBitmap == null) return null;
                        
                        // One-time log to verify capture is working
                        if (_logCounter == 1) Log($"ScreenGrab: First capture successful ({_screenCaptureBitmap.Width}x{_screenCaptureBitmap.Height})");

                        using (var g = Graphics.FromImage(_screenCaptureBitmap))
                        {
                            g.CopyFromScreen(detectionBox.Left, detectionBox.Top, 0, 0, detectionBox.Size);
                        }

                        return _screenCaptureBitmap;
                    }
                }

                catch (Exception ex)
                {
                    lastError = ex;
                    ResetCaptureBitmapNoThrow();

                    if (attempt == 0)
                        continue;
                }
            }

            LogScreenGrabException(lastError);
            return null;
        }

        public static float[] BitmapToFloatArray(Bitmap image, int targetW, int targetH)
        {
            Bitmap resized;
            if (image.Width != targetW || image.Height != targetH)
                resized = new Bitmap(image, new Size(targetW, targetH));
            else
                resized = image;

            float[] result = new float[3 * targetH * targetW];
            Rectangle rect = new Rectangle(0, 0, targetW, targetH);
            BitmapData bmpData = null;
            try
            {
                bmpData = resized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

                IntPtr ptr = bmpData.Scan0;
                int bytes = Math.Abs(bmpData.Stride) * targetH;
                byte[] rgbValues = new byte[bytes];

                Marshal.Copy(ptr, rgbValues, 0, bytes);
                for (int i = 0; i < rgbValues.Length / 3; i++)
                {
                    int index = i * 3;
                    result[i] = rgbValues[index + 2] / 255.0f; // R
                    result[targetW * targetH + i] = rgbValues[index + 1] / 255.0f; // G
                    result[2 * targetW * targetH + i] = rgbValues[index] / 255.0f; // B
                }
            }
            finally
            {
                if (bmpData != null)
                    resized.UnlockBits(bmpData);

                if (resized != image)
                    resized.Dispose();
            }

            return result;
        }

        private static float[] BitmapToLetterboxedFloatArray(Bitmap image, int targetW, int targetH, out LetterboxInfo letterboxInfo)
        {
            float scaleX = (float)targetW / Math.Max(1, image.Width);
            float scaleY = (float)targetH / Math.Max(1, image.Height);
            float scale = Math.Min(scaleX, scaleY);

            int resizedWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
            int resizedHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
            int padX = (targetW - resizedWidth) / 2;
            int padY = (targetH - resizedHeight) / 2;

            letterboxInfo = new LetterboxInfo(scale, padX, padY);

            using Bitmap letterboxed = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(letterboxed))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(
                    image,
                    new Rectangle(padX, padY, resizedWidth, resizedHeight),
                    new Rectangle(0, 0, image.Width, image.Height),
                    GraphicsUnit.Pixel);
            }

            return BitmapToFloatArray(letterboxed, targetW, targetH);
        }

        public Rectangle GetPhysicalCaptureBox()
        {
            Rectangle screenBounds = CaptureScreenBounds;
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            int screenW = screenBounds.Width;
            int screenH = screenBounds.Height;

            // FovSize is logical (DIPs), so we convert to physical pixels
            int cropW = Math.Min(screenW, (int)(FovSize * DpiScaleX));
            int cropH = Math.Min(screenH, (int)(FovSize * DpiScaleY));

            int cropX = screenBounds.Left + ((screenW - cropW) / 2);
            int cropY = screenBounds.Top + ((screenH - cropH) / 2);

            if (_logCounter % 200 == 0)
            {
                Log($"Capture Bounds Update: X={cropX}, Y={cropY}, Size={cropW}x{cropH} (ScreenBounds: {screenBounds.Left},{screenBounds.Top} {screenW}x{screenH}, DPI={DpiScaleX:F2})");
            }

            return new Rectangle(cropX, cropY, cropW, cropH);
        }

        public async Task<Prediction> GetClosestPredictionToCenterAsync()
        {
            if (_onnxModel == null || string.IsNullOrWhiteSpace(_inputName)) return null;

            Rectangle physicalDetectionBox = GetPhysicalCaptureBox();
            Rectangle physicalScreenBounds = CaptureScreenBounds;
            if (physicalScreenBounds.Width <= 0 || physicalScreenBounds.Height <= 0)
                physicalScreenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int physicalScreenW = physicalScreenBounds.Width;
            int physicalScreenH = physicalScreenBounds.Height;

            _logCounter++;
            if (_logCounter % 100 == 0)
                Log($"Capture info: PhysScreen={physicalScreenW}x{physicalScreenH}, PhysCrop={physicalDetectionBox.X},{physicalDetectionBox.Y} {physicalDetectionBox.Width}x{physicalDetectionBox.Height} (DPI={DpiScaleX:F2})");

            DebugOverlay.TotalFrames = _logCounter;
            DebugOverlay.PhysW = physicalScreenW;
            DebugOverlay.PhysH = physicalScreenH;
            DebugOverlay.CaptureX = physicalDetectionBox.X;
            DebugOverlay.CaptureY = physicalDetectionBox.Y;
            DebugOverlay.CapW = physicalDetectionBox.Width;
            DebugOverlay.CapH = physicalDetectionBox.Height;
            DebugOverlay.DpiX = DpiScaleX;
            DebugOverlay.DpiY = DpiScaleY;
            DebugOverlay.CurrentThreshold = ConfidenceThreshold;
            DebugOverlay.NumAnchors = _numAnchors;
            DebugOverlay.ModelStyleStr = _isEndToEnd ? "End-to-End" : (_isV8Style ? "YOLOv8 [1,4+C,N]" : "YOLOv5 [1,N,5+C]");

            Bitmap frame = ScreenGrab(physicalDetectionBox);
            if (frame == null) return null;

            if (CollectData)
            {
                DateTime currentTime = DateTime.Now;
                if ((currentTime - lastSavedTime).TotalSeconds >= 0.5)
                {
                    lastSavedTime = currentTime;
                    string uuid = Guid.NewGuid().ToString();
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string dir = Path.Combine(baseDir, "captures", "dataset");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    Bitmap clone = null;
                    lock (_captureLock)
                    {
                        if (_screenCaptureBitmap != null)
                            clone = new Bitmap(_screenCaptureBitmap);
                    }

                    if (clone != null)
                    {
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                using (clone)
                                {
                                    string fullPath = Path.Combine(dir, $"{uuid}.jpg");
                                    clone.Save(fullPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                                    Log($"Auto-saved dataset frame: {fullPath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Dataset save error: {ex.Message}");
                                Visualization.DebugOverlay.AddLog($"Auto-save Error: {ex.Message}");
                            }
                        });
                    }
                }
            }

            LetterboxInfo letterboxInfo;
            float[] inputArray = BitmapToLetterboxedFloatArray(frame, _modelInputWidth, _modelInputHeight, out letterboxInfo);
            if (inputArray == null) return null;

            Tensor<float> inputTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, _modelInputHeight, _modelInputWidth });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };

            try
            {
                using (var results = _onnxModel.Run(inputs, new[] { _outputName }, _modeloptions))
                {
                    if (results.Count == 0) return null;
                    var outputTensor = results[0].AsTensor<float>();
                    int[] outputShape = outputTensor.Dimensions.ToArray();
                    if (outputShape.Length != 3)
                    {
                        Log($"Unsupported output tensor rank: {outputShape.Length}. Expected rank 3.");
                        return null;
                    }

                    bool runtimeIsEndToEnd = _isEndToEnd;
                    bool runtimeIsV8Style = _isV8Style;
                    int runtimeNumAnchors = Math.Max(0, _numAnchors);
                    int runtimeNumClasses = Math.Max(1, _numClasses);

                    int dim1 = outputShape[1];
                    int dim2 = outputShape[2];

                    if (dim2 == 6)
                    {
                        runtimeIsEndToEnd = true;
                        runtimeIsV8Style = false;
                        runtimeNumAnchors = Math.Max(0, dim1);
                        runtimeNumClasses = 1;
                    }
                    else if (dim1 > 0 && dim2 > 0)
                    {
                        if (dim1 < dim2)
                        {
                            runtimeIsEndToEnd = false;
                            runtimeIsV8Style = true;
                            runtimeNumAnchors = dim2;
                            runtimeNumClasses = Math.Max(1, dim1 - 4);
                        }
                        else
                        {
                            runtimeIsEndToEnd = false;
                            runtimeIsV8Style = false;
                            runtimeNumAnchors = dim1;
                            runtimeNumClasses = Math.Max(1, dim2 - 5);
                        }
                    }

                    DebugOverlay.NumAnchors = runtimeNumAnchors;
                    DebugOverlay.ModelStyleStr = runtimeIsEndToEnd ? "End-to-End" : (runtimeIsV8Style ? "YOLOv8 [1,4+C,N]" : "YOLOv5 [1,N,5+C]");

                    if (runtimeNumAnchors <= 0) return null;

                    float fovMinX = (_modelInputWidth - FovSize) / 2.0f;
                    float fovMaxX = (_modelInputWidth + FovSize) / 2.0f;
                    float fovMinY = (_modelInputHeight - FovSize) / 2.0f;
                    float fovMaxY = (_modelInputHeight + FovSize) / 2.0f;

                    float localMaxX = Math.Max(1f, physicalDetectionBox.Width);
                    float localMaxY = Math.Max(1f, physicalDetectionBox.Height);

                    var tree = new KdTree<float, Prediction>(2, new FloatMath());
                    object treeLock = new object();
                    float maxConfSeen = 0f;
                    object maxConfLock = new object();

                    Parallel.For(0, runtimeNumAnchors, i =>
                    {
                        float x_center;
                        float y_center;
                        float width;
                        float height;
                        float confidence;

                        if (runtimeIsEndToEnd)
                        {
                            float x1 = outputTensor[0, i, 0];
                            float y1 = outputTensor[0, i, 1];
                            float x2 = outputTensor[0, i, 2];
                            float y2 = outputTensor[0, i, 3];
                            confidence = outputTensor[0, i, 4];

                            width = x2 - x1;
                            height = y2 - y1;
                            x_center = x1 + width / 2;
                            y_center = y1 + height / 2;
                        }
                        else if (runtimeIsV8Style)
                        {
                            x_center = outputTensor[0, 0, i];
                            y_center = outputTensor[0, 1, i];
                            width = outputTensor[0, 2, i];
                            height = outputTensor[0, 3, i];

                            confidence = 0f;
                            for (int c = 0; c < runtimeNumClasses; c++)
                            {
                                float score = outputTensor[0, 4 + c, i];
                                if (score > confidence) confidence = score;
                            }
                        }
                        else
                        {
                            float obj_conf = outputTensor[0, i, 4];
                            x_center = outputTensor[0, i, 0];
                            y_center = outputTensor[0, i, 1];
                            width = outputTensor[0, i, 2];
                            height = outputTensor[0, i, 3];

                            float max_class_score = 0f;
                            for (int c = 0; c < runtimeNumClasses; c++)
                            {
                                float score = outputTensor[0, i, 5 + c];
                                if (score > max_class_score) max_class_score = score;
                            }
                            confidence = max_class_score * obj_conf;
                        }

                        lock (maxConfLock)
                        {
                            if (confidence > maxConfSeen) maxConfSeen = confidence;
                        }

                        if (x_center <= 1.0f && y_center <= 1.0f && width <= 1.0f && height <= 1.0f)
                        {
                            x_center *= _modelInputWidth;
                            y_center *= _modelInputHeight;
                            width *= _modelInputWidth;
                            height *= _modelInputHeight;
                        }

                        if (confidence < ConfidenceThreshold) return;

                        if (_logCounter % 100 == 0)
                            Log($"Target Found: {x_center:F1},{y_center:F1} Conf:{confidence:F2}");

                        float xMinCenter = x_center - width / 2;
                        float yMinCenter = y_center - height / 2;
                        float xMaxCenter = x_center + width / 2;
                        float yMaxCenter = y_center + height / 2;

                        float xMinTopLeft = x_center;
                        float yMinTopLeft = y_center;
                        float xMaxTopLeft = x_center + width;
                        float yMaxTopLeft = y_center + height;

                        bool centerInBounds = xMinCenter >= 0 && yMinCenter >= 0 && xMaxCenter <= _modelInputWidth && yMaxCenter <= _modelInputHeight;
                        bool topLeftInBounds = xMinTopLeft >= 0 && yMinTopLeft >= 0 && xMaxTopLeft <= _modelInputWidth && yMaxTopLeft <= _modelInputHeight;

                        bool useTopLeft;
                        if (OutputBoxFormat == BoxFormat.Center)
                        {
                            useTopLeft = false;
                        }
                        else if (OutputBoxFormat == BoxFormat.TopLeft)
                        {
                            useTopLeft = true;
                        }
                        else
                        {
                            if (centerInBounds && !topLeftInBounds)
                                useTopLeft = false;
                            else if (!centerInBounds && topLeftInBounds)
                                useTopLeft = true;
                            else if (!centerInBounds && !topLeftInBounds)
                                useTopLeft = false;
                            else
                            {
                                // When both interpretations are plausible, prefer center-format
                                // to avoid half-box shifts where top-left lands at target center.
                                useTopLeft = false;
                            }
                        }

                        float xMinModel = useTopLeft ? xMinTopLeft : xMinCenter;
                        float yMinModel = useTopLeft ? yMinTopLeft : yMinCenter;
                        float xMaxModel = useTopLeft ? xMaxTopLeft : xMaxCenter;
                        float yMaxModel = useTopLeft ? yMaxTopLeft : yMaxCenter;

                        float candidateCenterX = xMinModel + ((xMaxModel - xMinModel) * 0.5f);
                        float candidateCenterY = yMinModel + ((yMaxModel - yMinModel) * 0.5f);
                        bool centerInsideFov =
                            candidateCenterX >= fovMinX &&
                            candidateCenterX <= fovMaxX &&
                            candidateCenterY >= fovMinY &&
                            candidateCenterY <= fovMaxY;
                        if (!centerInsideFov)
                            return;

                        float xMinCapture = (xMinModel - letterboxInfo.PadX) / letterboxInfo.Scale;
                        float yMinCapture = (yMinModel - letterboxInfo.PadY) / letterboxInfo.Scale;
                        float xMaxCapture = (xMaxModel - letterboxInfo.PadX) / letterboxInfo.Scale;
                        float yMaxCapture = (yMaxModel - letterboxInfo.PadY) / letterboxInfo.Scale;

                        xMinCapture = Math.Clamp(xMinCapture, 0f, localMaxX);
                        yMinCapture = Math.Clamp(yMinCapture, 0f, localMaxY);
                        xMaxCapture = Math.Clamp(xMaxCapture, 0f, localMaxX);
                        yMaxCapture = Math.Clamp(yMaxCapture, 0f, localMaxY);

                        float captureWidth = xMaxCapture - xMinCapture;
                        float captureHeight = yMaxCapture - yMinCapture;
                        if (captureWidth <= 1f || captureHeight <= 1f) return;

                        float captureCenterX = xMinCapture + captureWidth / 2f;
                        float captureCenterY = yMinCapture + captureHeight / 2f;

                        var prediction = new Prediction
                        {
                            Rectangle = new RectangleF(xMinCapture, yMinCapture, captureWidth, captureHeight),
                            Confidence = confidence
                        };

                        lock (treeLock)
                        {
                            tree.Add(new[] { captureCenterX, captureCenterY }, prediction);
                        }
                    });

                    if (_logCounter % 30 == 0)
                        Log($"Frame stats: MaxConf={maxConfSeen:F3} Threshold={ConfidenceThreshold:F2} Anchors={runtimeNumAnchors} V8Style={runtimeIsV8Style}");

                    DebugOverlay.LastMaxConf = maxConfSeen;
                    DebugOverlay.DetectionsThisSec = tree.Count;

                    // FOV-priority: aim at the target closest to crosshair (cursor) not screen center
                    float queryX = physicalDetectionBox.Width / 2.0f;
                    float queryY = physicalDetectionBox.Height / 2.0f;
                    if (CurrentCursorOffset.X >= 0)
                    {
                        queryX = Math.Clamp(CurrentCursorOffset.X, 0f, physicalDetectionBox.Width);
                        queryY = Math.Clamp(CurrentCursorOffset.Y, 0f, physicalDetectionBox.Height);
                    }
                    var nodes = tree.GetNearestNeighbours(new[] { queryX, queryY }, 1);
                    if (nodes.Length > 0)
                    {
                        AIConfidence = nodes[0].Value.Confidence;
                        return nodes[0].Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Inference Error: {ex.Message}");
                return null;
            }

            return null;
        }

        public void Dispose()
        {
            _onnxModel?.Dispose();
            lock (_captureLock)
            {
                _screenCaptureBitmap?.Dispose();
                _screenCaptureBitmap = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
