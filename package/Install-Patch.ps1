param(
    [ValidateSet('Apply','Restore','Status')][string]$Mode = 'Apply',
    [string]$InstallDirectory = (Join-Path ${env:ProgramFiles(x86)} 'Teevolution Terra Pro')
)
$ErrorActionPreference = 'Stop'
$originalHash = '48E7DB906FA30B99A828FEBE502F6BF0BE16BE3852D67CB78A6C0F43BF759BFD'
$patchedHash = '7BF423BFFC74DD260560B7E3F0B8D51A6ED7C089C21D90A3FB5A57A0B6DA35D3'
try {
    $directory = (Resolve-Path -LiteralPath $InstallDirectory).ProviderPath
    $exe = Join-Path $directory 'Mouse Drive Beta.exe'
    $backup = $exe + '.teevolution-original.bak'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Application not found: $exe" }
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if ($Mode -eq 'Status') {
        if ($hash -eq $originalHash) { Write-Host 'Original supported version.' }
        elseif ($hash -eq $patchedHash) { Write-Host 'Tray fix is installed.' }
        else { Write-Host "Unsupported executable: $hash" }
        exit 0
    }
    if ($Mode -eq 'Apply' -and $hash -eq $patchedHash) { Write-Host 'Already patched. Nothing changed.'; exit 0 }
    if ($Mode -eq 'Restore' -and $hash -eq $originalHash) { Write-Host 'Original already installed. Nothing changed.'; exit 0 }
    $expected = if ($Mode -eq 'Apply') { $originalHash } else { $patchedHash }
    if ($hash -ne $expected) { throw "Unsupported executable. No changes made. SHA256: $hash" }
    $running = Get-Process -Name 'Mouse Drive Beta' -ErrorAction SilentlyContinue | Where-Object {
        -not $_.Path -or [string]::Equals($_.Path, $exe, [StringComparison]::OrdinalIgnoreCase)
    }
    if ($running) {
        throw 'Exit Teevolution using its tray menu, then run this script again.'
    }
    # Test before elevation; custom writable installations do not need administrator access.
    $probe = Join-Path $directory ([Guid]::NewGuid().ToString('N') + '.write-test')
    try { $stream = [IO.File]::Create($probe); $stream.Dispose(); [IO.File]::Delete($probe) }
    catch {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw }
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Mode ' + $Mode + ' -InstallDirectory "' + $directory + '"'
        $child = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        exit $child.ExitCode
    }
    if (Test-Path -LiteralPath $backup) {
        if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $originalHash) {
            throw 'Existing backup is not the supported original. No changes made.'
        }
    } elseif ($Mode -eq 'Restore') { throw 'Original backup missing. No changes made.' }
    $stage = Join-Path $directory ([Guid]::NewGuid().ToString('N') + '.patch-temp')
    try {
        if ($Mode -eq 'Apply') {
            $patcher = Join-Path $PSScriptRoot 'TrayPatcher.exe'
            if (-not (Test-Path -LiteralPath $patcher)) { throw 'Download and extract the release ZIP; patcher binary is missing.' }
            & $patcher $exe $stage
            if ($LASTEXITCODE -ne 0) { throw 'Patcher failed. Installed executable unchanged.' }
            if ((Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -ne $patchedHash) { throw 'Unexpected patched hash. Installed executable unchanged.' }
            if (Test-Path -LiteralPath $backup) { [IO.File]::Replace($stage, $exe, [NullString]::Value) }
            else { [IO.File]::Replace($stage, $exe, $backup) }
        } else {
            [IO.File]::Copy($backup, $stage, $false)
            [IO.File]::Replace($stage, $exe, [NullString]::Value)
        }
    } finally { if (Test-Path -LiteralPath $stage) { [IO.File]::Delete($stage) } }
    Write-Host "Success: $Mode. Original backup: $backup"
    Write-Host 'For hidden launch, use the -startup argument. Enable Windows startup in Teevolution settings if desired.'
    exit 0
} catch { Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
