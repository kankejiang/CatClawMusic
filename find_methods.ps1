$dllPath = "C:\Program Files\dotnet\packs\Microsoft.Android.Ref.37\36.99.0-preview.5.308\ref\net11.0\Mono.Android.dll"
Write-Host "Loading: $dllPath"
$asm = [System.Reflection.Assembly]::LoadFile($dllPath)
$authType = $asm.GetType("Java.Net.Authenticator")
if ($authType -eq $null) {
    Write-Host "Type not found"
    exit 1
}
Write-Host "=== $($authType.FullName)"
Write-Host "Base: $($authType.BaseType.FullName)"
$methods = $authType.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::DeclaredOnly)
foreach ($m in $methods) {
    Write-Host "  $($m.Name) -> $($m.ReturnType.Name) (IsVirtual=$($m.IsVirtual), IsAbstract=$($m.IsAbstract))"
}
Write-Host ""
Write-Host "=== Properties ==="
$props = $authType.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::DeclaredOnly)
foreach ($p in $props) {
    Write-Host "  $($p.Name) : $($p.PropertyType.Name)"
}
