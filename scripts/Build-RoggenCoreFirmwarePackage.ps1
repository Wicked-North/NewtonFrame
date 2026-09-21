param(
    [string]$Version = '1.1.0',
    [string]$SdkRoot = "$PSScriptRoot\..\.toolchains\nRF5_SDK_17.1.0_ddde560\nRF5_SDK_17.1.0_ddde560",
    [string]$ArmGccRoot = "$env:USERPROFILE\.platformio\packages\toolchain-gccarmnoneeabi",
    [string]$OutputRoot = "$PSScriptRoot\..\firmware\RoggenCore"
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$cmake = Join-Path $repo '.venv\Scripts\cmake.exe'
if (!(Test-Path $cmake)) { $cmake = (Get-Command cmake).Source }
$ninja = Join-Path $repo '.venv\Scripts\ninja.exe'
$build = Join-Path $repo 'Tag_BLE_Photo_Viewer\build'
& $cmake -S (Join-Path $repo 'Tag_BLE_Photo_Viewer') -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" "-DNRF5_SDK_ROOT=$SdkRoot" "-DARM_GCC_ROOT=$ArmGccRoot"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $cmake --build $build --clean-first
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$srec = Get-ChildItem "$env:USERPROFILE\.platformio" -Recurse -Filter srec_cat.exe -File | Select-Object -First 1
if (!$srec) { throw 'srec_cat.exe was not found under the PlatformIO packages directory.' }
$softDevice = Join-Path $SdkRoot 'components\softdevice\s112\hex\s112_nrf52_7.2.0_softdevice.hex'
$app = Join-Path $build 'epaper_ble_receiver.hex'
$out = Join-Path $OutputRoot $Version
New-Item -ItemType Directory -Force $out | Out-Null
$upgradeHex = Join-Path $out "RoggenCore-$Version-upgrade.hex"
$recoveryHex = Join-Path $out "RoggenCore-$Version-recovery.hex"
Copy-Item $app $upgradeHex -Force
& $srec.FullName $softDevice -Intel $app -Intel -o $recoveryHex -Intel
if ($LASTEXITCODE) { exit $LASTEXITCODE }

function Get-Crc32([byte[]]$Bytes) {
    [uint32]$crc = [uint32]::MaxValue
    [uint32]$polynomial = [Convert]::ToUInt32('EDB88320', 16)
    foreach ($value in $Bytes) {
        $crc = $crc -bxor $value
        for ($bit = 0; $bit -lt 8; $bit++) {
            if ($crc -band 1) { $crc = (($crc -shr 1) -bxor $polynomial) } else { $crc = $crc -shr 1 }
        }
    }
    return [uint32]($crc -bxor [uint32]::MaxValue)
}
function Write-Manifest($hexPath, $recovery) {
    $bytes = [IO.File]::ReadAllBytes($hexPath)
    [object[]]$ranges = if ($recovery) { @([ordered]@{start=0; length=110592}) } else { @([ordered]@{start=102400; length=8192}) }
    $manifest = [ordered]@{
        schemaVersion = 1; name = 'RoggenCore'; version = $Version; targetPart = '00052811'; flashSize = 196608
        hexFile = Split-Path $hexPath -Leaf; hexSha256 = (Get-FileHash $hexPath -Algorithm SHA256).Hash
        hexCrc32 = Get-Crc32 $bytes; recoveryCapable = $recovery
        allowedRanges = $ranges
        uicrWords = if ($recovery) { [ordered]@{'10001088'='58031700'} } else { @{} }
    }
    $name = if ($recovery) { 'recovery-manifest.json' } else { 'upgrade-manifest.json' }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $out $name) -Encoding utf8
}
Write-Manifest $upgradeHex $false
Write-Manifest $recoveryHex $true
Write-Output "RoggenCore $Version packages created in $out"
