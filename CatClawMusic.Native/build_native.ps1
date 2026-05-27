$NDK = "$env:LOCALAPPDATA\Android\Sdk\ndk\27.0.12077973"
if (!(Test-Path "$NDK\ndk-build.cmd")) {
    Write-Host "NDK not found at $NDK" -ForegroundColor Red
    exit 1
}

Push-Location $PSScriptRoot
try {
    Remove-Item -Recurse -Force obj, libs -ErrorAction SilentlyContinue
    & "$NDK\ndk-build.cmd" -j4 NDK_PROJECT_PATH=. APP_BUILD_SCRIPT=jni/Android.mk
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build succeeded, copying to UI project..." -ForegroundColor Green
        Copy-Item -Path "libs\*" -Destination "..\CatClawMusic.UI\libs" -Recurse -Force
        Write-Host "Done!" -ForegroundColor Green
    } else {
        Write-Host "Build failed!" -ForegroundColor Red
    }
} finally {
    Pop-Location
}
