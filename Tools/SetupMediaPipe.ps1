$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $projectRoot '.Local/MediaPipe'
$python = Join-Path $runtime 'Scripts/python.exe'
$model = Join-Path $runtime 'gesture_recognizer.task'
$expectedHash = '97952348CF6A6A4915C2EA1496B4B37EBABC50CBBF80571435643C455F2B0482'
$modelUrl = 'https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task'

if (-not (Test-Path -LiteralPath $python)) {
    python -m venv $runtime
    if ($LASTEXITCODE -ne 0) { throw 'Python virtual environment creation failed.' }
}
& $python -m pip install 'mediapipe==0.10.35'
if ($LASTEXITCODE -ne 0) { throw 'MediaPipe installation failed.' }
if (-not (Test-Path -LiteralPath $model)) {
    Invoke-WebRequest -Uri $modelUrl -OutFile $model
}
if ((Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash -ne $expectedHash) {
    throw "Gesture model checksum mismatch: $model"
}
Write-Host 'Local MediaPipe hand recognition is ready.'
