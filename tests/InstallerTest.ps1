param([Parameter(Mandatory=$true)][string]$OriginalExe, [Parameter(Mandatory=$true)][string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $repo 'package\Install-Patch.ps1'
$originalHash = '48E7DB906FA30B99A828FEBE502F6BF0BE16BE3852D67CB78A6C0F43BF759BFD'
$patchedHash = '7BF423BFFC74DD260560B7E3F0B8D51A6ED7C089C21D90A3FB5A57A0B6DA35D3'
if ((Get-FileHash -LiteralPath $OriginalExe).Hash -ne $originalHash) { throw 'Unsupported test original' }
if (Test-Path -LiteralPath $TestDirectory) { throw 'Test directory must not exist' }
New-Item -ItemType Directory -Path $TestDirectory | Out-Null
$directory = (Resolve-Path -LiteralPath $TestDirectory).ProviderPath
$exe = Join-Path $directory 'Mouse Drive Beta.exe'
$backup = $exe + '.teevolution-original.bak'
Copy-Item -LiteralPath $OriginalExe -Destination $exe
function Invoke-Installer([string]$Mode, [int]$Expected = 0) {
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $installer -Mode $Mode -InstallDirectory $directory
    if ($LASTEXITCODE -ne $Expected) { throw "$Mode returned $LASTEXITCODE, expected $Expected" }
}
Invoke-Installer Status
Invoke-Installer Apply
if ((Get-FileHash -LiteralPath $exe).Hash -ne $patchedHash -or (Get-FileHash -LiteralPath $backup).Hash -ne $originalHash) { throw 'Apply/backup mismatch' }
Invoke-Installer Apply
Invoke-Installer Status
Invoke-Installer Restore
Invoke-Installer Restore
if ((Get-FileHash -LiteralPath $exe).Hash -ne $originalHash) { throw 'Restore mismatch' }
Invoke-Installer Apply
if ((Get-FileHash -LiteralPath $backup).Hash -ne $originalHash) { throw 'Backup was overwritten' }
Invoke-Installer Restore
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $exe -Force
$unsupported = (Get-FileHash -LiteralPath $exe).Hash
Invoke-Installer Apply 1
if ((Get-FileHash -LiteralPath $exe).Hash -ne $unsupported) { throw 'Unsupported file was modified' }
Copy-Item -LiteralPath $OriginalExe -Destination $exe -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $backup -Force
Invoke-Installer Apply 1
if ((Get-FileHash -LiteralPath $exe).Hash -ne $originalHash) { throw 'Original modified despite invalid backup' }
Write-Host 'PASS apply, original backup, repeat apply, status, restore, repeat restore, reapply, unsupported input, invalid backup.'
