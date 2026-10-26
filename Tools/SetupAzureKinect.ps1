param([string]$ProjectPath = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $ProjectPath '.Local/AzureKinect/Runtime'
$cache = Join-Path $ProjectPath '.Local/AzureKinect/Packages'
New-Item -ItemType Directory -Force $runtime,$cache | Out-Null
# Microsoft's official Unity sample pins this compatible set. Never commit these binaries.
$packages = @(
    @('microsoft.azure.kinect.sensor','1.4.1','6C512A20C4A82B80E02B0F6D4A6CDA51E88D4893CD47AB85C7BCA37CD364C976'),
    @('microsoft.azure.kinect.bodytracking','1.1.2','6151FF7E01A623FA2C15F01D9DFC22457C1353C484939D560A6F57A481AD83BF'),
    @('microsoft.azure.kinect.bodytracking.onnxruntime','1.10.0','59E770DC2728014FF407AD131A0BE488B80A9479FC83996235801768BB822781')
)
foreach ($package in $packages) {
    $id,$version,$sha256 = $package
    $archive = Join-Path $cache "$id.$version.nupkg"
    if (!(Test-Path $archive)) {
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $archive
    }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $sha256) { throw "Checksum mismatch: $id" }
    $expanded = Join-Path $cache $id
    Expand-Archive $archive -DestinationPath $expanded -Force
    Get-ChildItem "$expanded/lib/native/amd64/release" -Filter '*.dll' | Copy-Item -Destination $runtime -Force
}
Copy-Item "$cache/microsoft.azure.kinect.bodytracking/content/dnn_model_2_0_lite_op11.onnx" $runtime -Force
Write-Output "Azure Kinect native runtime ready: $runtime"
Write-Output 'Open Sandbox.unity in Windows x64 Unity. No RGB recording is enabled.'
