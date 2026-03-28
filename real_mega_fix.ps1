$path = 'c:\Users\nikol\OneDrive\Desktop\aimbot C#\AimAssistC#\MainWindow.xaml.cs'
$content = Get-Content -Path $path -Raw

# 1. Fix _lastRecordMousePos declaration
$content = $content -replace 'private Point _lastRecordMousePos;', 'private System.Drawing.Point _lastRecordMousePos;'

# 2. Extract and Move Recoil Recording UI Logic properly
# Capture the block (including the ALabel)
$recordingBlockRegex = '(?s)\s*RecoilScroller\.Children\.Add\(new ALabel\(\"Recoil Recording \[EXPERIMENTAL\]\"\)\);.*?RecoilScroller\.Children\.Add\(SaveRecording\);'

if ($content -match $recordingBlockRegex) {
    $recordingBlock = $matches[0]
    
    # 3. Apply fixes WITHIN the recording block
    # Fix AKeyChanger constructor
    $recordingBlock = $recordingBlock -replace 'AKeyChanger RecordingKey = new\(this, \"Recording Toggle Key\",\s*\"Key to start and stop recording your recoil movement\.\"\);', 'AKeyChanger RecordingKey = new("Recording Toggle Key", _recoilRecordingBindingManager.CurrentBinding);'
    
    # Fix UpdateKey -> KeyNotifier.Content
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.UpdateKey\(_recoilRecordingBindingManager\.CurrentBinding\);', 'RecordingKey.KeyNotifier.Content = _recoilRecordingBindingManager.CurrentBinding;'
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.UpdateKey\(binding\);', 'RecordingKey.KeyNotifier.Content = binding;'
    
    # Fix KeyButton -> Reader
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.KeyButton\.Click', 'RecordingKey.Reader.Click'
    
    # Fix Content -> KeyNotifier.Content
    $recordingBlock = $recordingBlock -replace 'RecordingKey\.KeyButton\.Content = \"Listening\.\.\.\";', 'RecordingKey.KeyNotifier.Content = "Listening...";'
    
    # Fix ConfigSaver
    $recordingBlock = $recordingBlock -replace 'ConfigSaver saver = new\(\);', 'AimmyWPF.SecondaryWindows.ConfigSaver saver = new();'

    # Remove the old (buggy) block
    $content = $content -replace [regex]::Escape($matches[0]), ''
    
    # 4. Re-insert AFTER recoilStatus declaration to fix "recoilStatus before declared" error
    # Look for recoilStatus declaration end
    $insertPoint = 'RecoilScroller\.Children\.Add\(recoilStatus\);'
    $content = $content -replace $insertPoint, "$&`r`n`r`n            $recordingBlock"
}

# 5. Fix StartRecoilRecordingLoop (dx/dy logic)
$content = $content -replace 'int dx = currentPos\.X - _lastRecordMousePos\.X;', 'int dx = (int)(currentPos.X - _lastRecordMousePos.X);'
$content = $content -replace 'int dy = currentPos\.Y - _lastRecordMousePos\.Y;', 'int dy = (int)(currentPos.Y - _lastRecordMousePos.Y);'

$content | Set-Content -Path $path -NoNewline
