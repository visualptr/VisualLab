param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'release'))
$ErrorActionPreference = 'Stop'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
& $dotnetPath build (Join-Path $PSScriptRoot 'VisualLab') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$dll = Join-Path $OutputDirectory 'VisualLab.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VisualLab\bin\Release\net48\VisualLab.dll') -Destination $dll
$hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS'), "$hash  VisualLab.dll`n")
Compress-Archive -LiteralPath $dll -DestinationPath (Join-Path $OutputDirectory 'VisualLab.zip') -Force
Write-Output "Packaged VisualLab.dll, VisualLab.zip and SHA256SUMS in $OutputDirectory"
Write-Output 'For LocalAdmin, attach only VisualLab.dll to the public GitHub release.'
