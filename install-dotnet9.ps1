$url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.313/dotnet-sdk-9.0.313-win-x64.exe"
$out = "$env:TEMP\dotnet-sdk-9.exe"
Write-Host "Downloading .NET 9 SDK..."
Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing
$size = (Get-Item $out).Length
Write-Host "Downloaded: $size bytes"
Write-Host "Installing (this needs admin)..."
Start-Process -FilePath $out -ArgumentList "/install","/quiet","/norestart" -Wait -NoNewWindow
Write-Host "Done. Exit code: $LASTEXITCODE"
