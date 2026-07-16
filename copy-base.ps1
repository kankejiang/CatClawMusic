$languages = @(
    "ja", "ko", "fr", "de", "es", "ru", "pt", "it",
    "ar", "hi", "bn", "ta", "te", "mr", "gu", "kn", "ml",
    "id", "ms", "th", "vi", "nl", "pl", "tr", "uk", "sv",
    "nb", "da", "fi", "cs", "hu", "ro", "el", "he", "sk",
    "hr", "sr", "bg", "fil", "ur", "fa", "sw", "ca", "lv",
    "lt", "et", "sl", "is"
)

$baseFile = "c:\Code\CatClawMusic\CatClawMusic.Maui\Resources\AppResources.en.resx"
$resourcesDir = "c:\Code\CatClawMusic\CatClawMusic.Maui\Resources"

foreach ($lang in $languages) {
    $destFile = Join-Path $resourcesDir "AppResources.$lang.resx"
    Copy-Item -Path $baseFile -Destination $destFile -Force
    Write-Host "Copied to AppResources.$lang.resx"
}

Write-Host "Base copy done!"
