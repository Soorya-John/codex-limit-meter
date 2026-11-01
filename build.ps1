$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /out:"$PSScriptRoot\dist\CodexLimitMeter.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$test = Start-Process -FilePath "$PSScriptRoot\dist\CodexLimitMeter.exe" -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
if ($test.ExitCode -ne 0) { throw "Self-test failed: $($test.ExitCode)" }
Write-Output 'Built dist\CodexLimitMeter.exe; self-tests passed.'
