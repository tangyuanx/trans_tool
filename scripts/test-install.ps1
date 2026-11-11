param(
    [Parameter(Mandatory)] [string] $PreviousSetup,
    [Parameter(Mandatory)] [string] $CurrentSetup
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP ('Trans Tool ' + [guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'Installed'
$stage = Join-Path $root 'Staged'
New-Item -ItemType Directory -Force $stage | Out-Null
$app = Join-Path $install 'TransTool.exe'
$setup = Join-Path $stage 'TransTool-Windows-Setup.exe'
$helper = Join-Path $stage 'TransTool-Update.exe'
$uninstall = Join-Path $install 'unins000.exe'

function Stop-TestWindow {
    Get-Process -Name TransTool -ErrorAction SilentlyContinue |
        Where-Object Path -eq $app | Stop-Process -Force -ErrorAction SilentlyContinue
}

try {
    $first = Start-Process $PreviousSetup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', "/DIR=`"$install`"") -Wait -PassThru
    if ($first.ExitCode -ne 0 -or !(Test-Path $app)) { throw 'Baseline installation failed.' }
    if ((Get-Item $app).VersionInfo.FileVersion -ne '0.0.0.0') { throw 'Baseline version mismatch.' }
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B8D02BA-67EF-4D4C-8AD1-1CAD22649362}_is1'
    if ((Get-ItemProperty -LiteralPath $uninstallKey).InstallLocation.TrimEnd('\') -ne $install) {
        throw 'Installer did not register its installation directory.'
    }
    $old = Start-Process $app -PassThru
    if (!$old.WaitForInputIdle(10000)) { throw 'Baseline window did not start.' }

    Copy-Item $CurrentSetup $setup
    Copy-Item $app $helper
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash
    $worker = Start-Process $helper -ArgumentList @('--apply', $old.Id, "`"$app`"", $hash) -PassThru
    $old.Refresh()
    # Hosted runners can start a GUI process in a desktop that PowerShell cannot enumerate.
    if (!$old.CloseMainWindow()) { Stop-Process -Id $old.Id }
    if (!$worker.WaitForExit(90000) -or $worker.ExitCode -ne 0) { throw 'Update helper did not complete.' }

    $expected = (Get-Content -Raw (Join-Path $PSScriptRoot '../VERSION')).Trim() + '.0'
    if ((Get-Item $app).VersionInfo.FileVersion -ne $expected) { throw 'In-place version mismatch.' }

    $restarted = $null
    for ($i = 0; $i -lt 30 -and !$restarted; $i++) {
        $restarted = Get-Process -Name TransTool -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $app -and $_.Id -ne $old.Id }
        if (!$restarted) { Start-Sleep -Milliseconds 200 }
    }
    if (!$restarted -or !$restarted.WaitForInputIdle(10000)) { throw 'Updated application did not reopen.' }
    Write-Host "Verified installation, in-place upgrade and restart ($expected)."
}
finally {
    Stop-TestWindow
    if (Test-Path $uninstall) { Start-Process $uninstall -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait }
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}
