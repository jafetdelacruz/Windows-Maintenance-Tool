$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$selector = Join-Path $repository 'tools/release/SHOW-CURRENT-ACCEPTANCE.ps1'
$current = Join-Path $repository 'tools/release/current-accepted-set.json'
$history = Join-Path $repository 'tools/release/accepted-set-rc4-no-preamble-5.json'
$fixtureRoot = Join-Path $repository ('artifacts/verification/selection-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$checks = New-Object 'Collections.Generic.List[object]'
foreach ($path in @($current,$history)) {
    $output = @(& $selector -ManifestPath $path)
    if (-not ($output -join "`n").Contains('Nothing was launched.')) { throw 'Valid selection did not pass.' }
    $checks.Add([pscustomobject]@{ Case=[IO.Path]::GetFileName($path); Passed=$true })
}
foreach ($name in @('hash','path','missing-role','duplicate-role','unknown-selection','scope','counts')) {
    $node = Get-Content -LiteralPath $current -Raw -Encoding UTF8 | ConvertFrom-Json
    switch ($name) {
        'hash' { $node.artifacts[0].sha256 = '0' * 64; $expected='Hash mismatch:' }
        'path' { $node.artifacts[0].path = '../outside.exe'; $expected='Artifact must stay inside' }
        'missing-role' { $node.artifacts = @($node.artifacts | Select-Object -Skip 1); $expected='Incomplete accepted set.' }
        'duplicate-role' { $node.artifacts[1].role = 'executable'; $expected='Missing or duplicate role:' }
        'unknown-selection' { $node.selectionId = 'unaccepted-candidate'; $expected='Unknown accepted selection.' }
        'scope' { $node.productionEnabled = $true; $expected='Contradictory review acceptance criteria.' }
        'counts' { $node.expected.functionalReports = 0; $expected='Contradictory review acceptance criteria.' }
    }
    $path = Join-Path $fixtureRoot ($name + '.json')
    [IO.File]::WriteAllText($path, ($node | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
    $rejected = $false
    try { $null = & $selector -ManifestPath $path }
    catch { $rejected = $_.Exception.Message.StartsWith($expected, [StringComparison]::Ordinal) }
    if (-not $rejected) { throw ('Invalid selection not rejected as expected: ' + $name) }
    $checks.Add([pscustomobject]@{ Case=$name; Passed=$true })
}
$result = [pscustomobject]@{ Passed=$true; Count=$checks.Count; HostMajor=$PSVersionTable.PSVersion.Major; Cases=@($checks.ToArray()) }
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'RESULTADO.json'), ($result | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
$result | ConvertTo-Json -Depth 10
