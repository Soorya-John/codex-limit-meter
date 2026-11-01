$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$root\build.ps1"
$temp = Join-Path $root 'tests\temp'
New-Item -ItemType Directory -Force $temp | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /out:"$temp\FakeCodex.exe" /reference:System.Web.Extensions.dll "$PSScriptRoot\FakeCodex.cs"
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
$savedOverride = $env:CODEX_LIMIT_METER_CODEX
$savedError = $env:METER_TEST_ERROR
$tray = $null
try {
    $env:CODEX_LIMIT_METER_CODEX = "$temp\FakeCodex.exe"
    $env:METER_TEST_ERROR = '0'
    $out = Join-Path $temp 'result.txt'
    $p = Start-Process "$root\dist\CodexLimitMeter.exe" -ArgumentList "--check `"$out`"" -Wait -PassThru -WindowStyle Hidden
    $summary = Get-Content $out -Raw
    if ($p.ExitCode -ne 0 -or $summary -notmatch '5-hour: 75% left' -or $summary -notmatch 'Weekly: 40% left') { throw 'Protocol success test failed' }
    $env:METER_TEST_ERROR = '1'
    $p = Start-Process "$root\dist\CodexLimitMeter.exe" -ArgumentList "--check `"$out`"" -Wait -PassThru -WindowStyle Hidden
    if ($p.ExitCode -ne 1 -or (Get-Content $out -Raw) -notmatch 'Codex rejected') { throw 'Protocol error test failed' }
    $env:METER_TEST_ERROR = '0'
    $tray = Start-Process "$root\dist\CodexLimitMeter.exe" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $tray.Refresh()
    if ($tray.HasExited -or $tray.MainWindowHandle -ne 0) { throw 'Tray-only launch test failed' }
    $second = Start-Process "$root\dist\CodexLimitMeter.exe" -PassThru -WindowStyle Hidden
    if (-not $second.WaitForExit(5000) -or $second.ExitCode -ne 0) { throw 'Single-instance test failed' }
    Write-Output 'PASS: protocol handshake, quota parsing, notifications, errors, tray-only launch, single instance.'
} finally {
    if ($tray -and -not $tray.HasExited) { Stop-Process -Id $tray.Id }
    $env:CODEX_LIMIT_METER_CODEX = $savedOverride
    $env:METER_TEST_ERROR = $savedError
}
