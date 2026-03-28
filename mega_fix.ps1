$path = 'c:\Users\nikol\OneDrive\Desktop\aimbot C#\AimAssistC#\MainWindow.xaml.cs'
$content = Get-Content -Path $path -Raw

# 1. Fix _lastRecordMousePos declaration to use Drawing.Point
$content = $content -replace 'private Point _lastRecordMousePos;', 'private System.Drawing.Point _lastRecordMousePos;'

# 2. Extract and Move Recoil Recording UI Logic
# We identify the entire block we added for recording and move it.
$recordingBlockRegex = '(?s)RecoilScroller\.Children\.Add\(new ALabel\(\"Recoil Recording \[EXPERIMENTAL\]\"\)\);.*?SaveRecording\.Reader\.Click \+= \(s, e\) =>.*?RecoilScroller\.Children\.Add\(SaveRecording\);'

# Find the block
if ($content -match $recordingBlockRegex) {
    $recordingBlock = $matches[0]
    
    # Clean up the recording block (fix AKeyChanger and ConfigSaver)
    $recordingBlock = $recordingBlock -replace 'AKeyChanger RecordingKey = new\(this, \"Recording Toggle Key\", \"Used to start/stop recoil recording\.\"\);', 'AKeyChanger RecordingKey = new("Recording Toggle Key", _recoilRecordingBindingManager?.CurrentBinding ?? "F12");'
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.KeyButton\.Content = _recoilRecordingBindingManager\?.CurrentBinding \?\? \"F12\";', ''
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.UpdateKey\(\);', 'RecordingKey.KeyNotifier.Content = binding;'
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.Reader\.Click \+= \(s, e\) =>\s*{\s*}', 'RecordingKey.Reader.Click += (s, e) => { RecordingKey.KeyNotifier.Content = "Listening.."; }'
    # Adding listener logic
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.Reader\.Click \+= \(s, e\) =>\s*{\s*RecordingKey.KeyNotifier.Content = \"Listening\.\.\";\s*}', 'RecordingKey.Reader.Click += (s, e) =>
            {
                RecordingKey.KeyNotifier.Content = "Listening..";
                _recoilRecordingBindingManager.ListenForBinding((binding) =>
                {
                    RecordingKey.KeyNotifier.Content = binding;
                    SaveSessionState();
                });
            };'
    $recordingBlock = $recordingBlock -replace 'ConfigSaver\.Save\(aimmySettings, \"bin/configs/SessionState\.cfg\"\);', 'SaveSessionState();'

    # Remove it from the bottom
    $content = $content -replace [regex]::Escape($matches[0]), ''
    
    # Insert it after AdsOnlyRecoil scroller addition
    $targetAfter = 'RecoilScroller\.Children\.Add\(AdsOnlyRecoil\);'
    $content = $content -replace $targetAfter, "$&`r`n`r`n            $recordingBlock"
}

# 3. Final cleanup of any stray ConfigSaver
$content = $content -replace 'ConfigSaver\.Save', 'SaveSessionState'

# 4. Fix StartRecoilRecordingLoop (dx/dy logic)
$content = $content -replace 'int dx = currentPos\.X - _lastRecordMousePos\.X;', 'int dx = (int)(currentPos.X - _lastRecordMousePos.X);'
$content = $content -replace 'int dy = currentPos\.Y - _lastRecordMousePos\.Y;', 'int dy = (int)(currentPos.Y - _lastRecordMousePos.Y);'

$content | Set-Content -Path $path -NoNewline
