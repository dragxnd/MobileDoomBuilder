param(
    [ValidateSet('Windows','Android')][string]$Platform = 'Android',
    [Parameter(Mandatory=$true)][string]$UnityEditorRoot
)
$ErrorActionPreference = 'Stop'
$android = Join-Path $UnityEditorRoot 'Data\PlaybackEngines\AndroidPlayer'
$cmake = Join-Path $android 'SDK\cmake\3.22.1\bin\cmake.exe'
if ($Platform -eq 'Windows') {
    $output = Join-Path $PSScriptRoot 'build-windows'
    & $cmake -S $PSScriptRoot -B $output -G 'Visual Studio 15 2017 Win64' -DCMAKE_BUILD_TYPE=Release
} else {
    $output = Join-Path $PSScriptRoot 'build-android'
    & $cmake -S $PSScriptRoot -B $output -G Ninja "-DCMAKE_MAKE_PROGRAM=$android/SDK/cmake/3.22.1/bin/ninja.exe" "-DCMAKE_TOOLCHAIN_FILE=$android/NDK/build/cmake/android.toolchain.cmake" -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-26 -DANDROID_STL=c++_static -DCMAKE_BUILD_TYPE=Release
}
if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed.' }
& $cmake --build $output --config Release --target mobileudb_nodes --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Native library build failed.' }
