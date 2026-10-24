$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

$expectedUser = 'WDAGUtilityAccount'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) 'WinMaintPhase4Lab'
$executablePath = 'C:\WinMaintLab\WinMaint.Cli.exe'
$oldFile = Join-Path $fixtureRoot 'eligible-old.bin'
$recentFile = Join-Path $fixtureRoot 'preserve-recent.bin'
$restoreMarker = 'C:\WinMaint-Phase4-Mutation.marker'

if ($env:USERNAME -ne $expectedUser) {
    throw 'This script can run only inside Windows Sandbox.'
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw 'The published executable was not found in the read-only mapping.'
}

if (Test-Path -LiteralPath $restoreMarker -PathType Leaf) {
    Write-Host 'RESTORE CHECK: a mutation marker already exists in this Sandbox session.' -ForegroundColor Yellow
}
else {
    Write-Host 'RESTORE CHECK: no previous-session mutation marker is present.' -ForegroundColor Green
}

if (Test-Path -LiteralPath $fixtureRoot) {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
[IO.File]::WriteAllBytes($oldFile, [byte[]]::new(4096))
[IO.File]::SetLastWriteTimeUtc($oldFile, [DateTime]::UtcNow.AddDays(-2))
[IO.File]::WriteAllBytes($recentFile, [byte[]]::new(2048))

try {
    Write-Host 'Isolated fixture ready: 1 old file and 1 recent file.'
    & $executablePath --phase4-lab-temp --no-ansi
    $applicationExitCode = $LASTEXITCODE

    $oldExists = Test-Path -LiteralPath $oldFile -PathType Leaf
    $recentExists = Test-Path -LiteralPath $recentFile -PathType Leaf
    if ($applicationExitCode -eq 0 -and -not $oldExists -and $recentExists) {
        [IO.File]::WriteAllText($restoreMarker, 'Created by the Phase 4 disposable lab.')
        Write-Host 'MUTATION GATE PASSED: only the old file was deleted.' -ForegroundColor Green
        Write-Host 'RESTORE MARKER CREATED: close Sandbox completely before the next check.' -ForegroundColor Green
    }
    elseif ($applicationExitCode -eq 2 -and $oldExists -and $recentExists) {
        Write-Host 'CANCELLATION GATE PASSED: no fixture file was changed.' -ForegroundColor Green
    }
    else {
        Write-Host "TEST FAILED: exit code $applicationExitCode; old=$oldExists; recent=$recentExists." -ForegroundColor Red
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }

    Write-Host 'The disposable fixture was removed.'
    Read-Host 'Press Enter to close this console'
}
