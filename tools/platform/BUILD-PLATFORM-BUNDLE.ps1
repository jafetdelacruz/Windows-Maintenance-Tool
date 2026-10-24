param(
    [Parameter(Mandatory=$true)][string] $Package,
    [Parameter(Mandatory=$true)][string] $Destination,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedManifestSha256
)
$ErrorActionPreference = 'Stop'
$Package = [IO.Path]::GetFullPath($Package)
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Destination exists; never overwrite a bundle.' }
$manifestPath = Join-Path $Package 'package-manifest.json'
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $ExpectedManifestSha256) { throw 'Selected package manifest mismatch.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 'review-r0-package-1.0' -or $manifest.ProductionEnabled -ne $false -or
    $manifest.ResourceVersion -ne '1.0.0-rc.5' -or $manifest.RuleSetVersion -ne '1.0.4') { throw 'Expected rc.5 R0 candidate.' }
$copy = @(
    @{ Source='WinMaint.Cli.exe'; Target='WinMaint.Cli.exe' },
    @{ Source='verifier/WinMaint.EvidenceVerifier.exe'; Target='WinMaint.EvidenceVerifier.exe' },
    @{ Source='Invoke-CandidateProcess.ps1'; Target='Invoke-CandidateProcess.ps1' },
    @{ Source='Invoke-HashPinnedCandidateProcess.ps1'; Target='Invoke-HashPinnedCandidateProcess.ps1' }
)
foreach ($entry in $copy) {
    $expected = @($manifest.Artifacts | Where-Object Name -eq $entry.Source)
    if ($expected.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $Package $entry.Source) -Algorithm SHA256).Hash -ne $expected[0].Sha256) { throw 'Package artifact mismatch.' }
}
New-Item -ItemType Directory -Path $Destination | Out-Null
foreach ($entry in $copy) { Copy-Item -LiteralPath (Join-Path $Package $entry.Source) -Destination (Join-Path $Destination $entry.Target) }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VERIFY-PLATFORM.ps1') -Destination $Destination
$files = @('WinMaint.Cli.exe','WinMaint.EvidenceVerifier.exe','VERIFY-PLATFORM.ps1','Invoke-CandidateProcess.ps1','Invoke-HashPinnedCandidateProcess.ps1')
$bundle = [ordered]@{ SchemaVersion='platform-r0-bundle-1.0'; ToolVersion=$manifest.ToolVersion;
    ResourceVersion=$manifest.ResourceVersion; RuleSetVersion=$manifest.RuleSetVersion; ProductionEnabled=$false;
    SourcePackageManifestSHA256=$ExpectedManifestSha256;
    Files=@($files | ForEach-Object { [pscustomobject]@{ Name=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $Destination $_) -Algorithm SHA256).Hash } }) }
$utf8 = New-Object Text.UTF8Encoding($false)
$bundlePath = Join-Path $Destination 'bundle.json'
[IO.File]::WriteAllText($bundlePath, ($bundle | ConvertTo-Json -Depth 10), $utf8)
$pin = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash
$launcher = @'
@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0VERIFY-PLATFORM.ps1" -ExpectedBundleSha256 __PIN__
set "result=%errorlevel%"
echo.
echo Validation finished. Exit code: %result%
echo Keep this entire folder, including RESULTS, and return it to the development PC.
pause
exit /b %result%
'@
[IO.File]::WriteAllText((Join-Path $Destination 'VALIDATE.cmd'), $launcher.Replace('__PIN__',$pin).Replace("`r`n","`n").Replace("`n","`r`n"), [Text.Encoding]::ASCII)
$instructions = @'
WINMAINT rc.5 - PRUEBA DE SOLO LECTURA

1. En la PC con Windows 10, copia esta carpeta completa al Escritorio.
2. Clic derecho en VALIDATE.cmd > Ejecutar como administrador.
3. Acepta el aviso de Windows solo si corresponde a este archivo que acabas de copiar.
4. Espera a que aparezca RESULT: True o RESULT: False. No cierres la ventana antes.
5. Devuelve la carpeta completa, incluida RESULTS, a la PC de desarrollo.

No instala nada ni necesita .NET instalado. No borra archivos, no repara Windows
ni inicia carga CPU/RAM. Solo consulta datos y escribe sus reportes en RESULTS.
Si aparece False, conserva igualmente todo: no significa que debas reparar la PC.

Esta prueba NO acredita aun UAC del ejecutable, teclas Esc/Ctrl+C ni presentacion
en CMD/Windows Terminal. Esas puertas se revisan por separado.
Los reportes son privados. No subir esta carpeta ni RESULTS a GitHub.
'@
[IO.File]::WriteAllText((Join-Path $Destination 'LEEME.txt'), $instructions, (New-Object Text.UTF8Encoding($true)))
[pscustomobject]@{ Bundle=$Destination; ManifestSHA256=$pin; Prepared=$true; Accepted=$false }
