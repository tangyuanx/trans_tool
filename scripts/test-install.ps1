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
    $old = Start-Process $app -PassThru
    if (!$old.WaitForInputIdle(10000)) { throw 'Baseline window did not start.' }

    Copy-Item $CurrentSetup $setup
    Copy-Item $app $helper
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash
    $worker = Start-Process $helper -ArgumentList @('--apply', $old.Id, "`"$app`"", $hash) -PassThru
    if (!$old.CloseMainWindow()) { throw 'Could not close the baseline window.' }
    if (!$worker.WaitForExit(90000) -or $worker.ExitCode -ne 0) { throw 'Update helper did not complete.' }

    $expected = (Get-Content -Raw (Join-Path $PSScriptRoot '../VERSION')).Trim() + '.0'
    if ((Get-Item $app).VersionInfo.FileVersion -ne $expected) { throw 'In-place version mismatch.' }

    $restarted = $null
    for ($i = 0; $i -lt 30 -and !$restarted; $i++) {
        $restarted = Get-Process -Name TransTool -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $app -and $_.Id -ne $old.Id -and $_.MainWindowHandle -ne 0 }
        if (!$restarted) { Start-Sleep -Milliseconds 200 }
    }
    if (!$restarted) { throw 'Updated application did not reopen.' }
    Write-Host "Verified installation, in-place upgrade and restart ($expected)."
}
finally {
    Stop-TestWindow
    if (Test-Path $uninstall) { Start-Process $uninstall -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait }
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}
