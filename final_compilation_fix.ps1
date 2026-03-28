$path = 'c:\Users\nikol\OneDrive\Desktop\aimbot C#\AimAssistC#\MainWindow.xaml.cs'
$content = Get-Content -Path $path -Raw

# 1. Ensure using Microsoft.Win32; is present
if ($content -notmatch 'using Microsoft\.Win32;') {
    $content = $content -replace 'using System\.Windows;', 'using System.Windows;`r`nusing Microsoft.Win32;'
}

# 2. Fix _lastRecordMousePos declaration
$content = $content -replace 'private Point _lastRecordMousePos;', 'private System.Drawing.Point _lastRecordMousePos;'

# 3. Fix StartRecoilRecordingLoop (dx/dy logic)
$content = $content -replace 'int dx = currentPos\.X - _lastRecordMousePos\.X;', 'int dx = (int)(currentPos.X - _lastRecordMousePos.X);'
$content = $content -replace 'int dy = currentPos\.Y - _lastRecordMousePos\.Y;', 'int dy = (int)(currentPos.Y - _lastRecordMousePos.Y);'

# 4. Replace the BUGGY recording block with a CLEAN one
$oldBlockRegex = '(?s)\s*RecoilScroller\.Children\.Add\(new ALabel\(\"Recoil Recording \[EXPERIMENTAL\]\"\)\);.*?RecoilScroller\.Children\.Add\(SaveRecording\);'

$newBlock = '
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
                    InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "recoil"),
                    FileName = "NewRecordedPattern.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        var patternData = new { steps = _recordedRecoilSteps };
                        File.WriteAllText(saveFileDialog.FileName, JsonConvert.SerializeObject(patternData, Formatting.Indented));
                        MessageBox.Show($"Saved {_recordedRecoilSteps.Count} steps to {Path.GetFileName(saveFileDialog.FileName)}", "Recoil Recorder");
                        _recordedRecoilSteps.Clear();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save: {ex.Message}", "Error");
                    }
                }
            };
            RecoilScroller.Children.Add(SaveRecording);'

$content = $content -replace $oldBlockRegex, $newBlock

$content | Set-Content -Path $path -NoNewline
