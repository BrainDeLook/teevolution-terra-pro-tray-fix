$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'src\TrayPatcher.csproj') -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$bin = Join-Path $PSScriptRoot 'src\bin\Release\net48'
$package = Join-Path $PSScriptRoot 'package'
foreach ($file in @('README.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $package -Force
}
foreach ($file in @('TrayPatcher.exe','TrayPatcher.exe.config','Mono.Cecil.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $file) -Destination $package -Force
}
$zip = Join-Path $PSScriptRoot 'Teevolution-Terra-Pro-Tray-Fix-v1.0.0.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256
