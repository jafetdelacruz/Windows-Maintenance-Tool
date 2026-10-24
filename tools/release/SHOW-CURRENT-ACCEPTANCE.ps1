param([string] $ManifestPath = (Join-Path $PSScriptRoot 'current-accepted-set.json'))

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$prefix = $repository.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$historical = $manifest.schemaVersion -eq 'accepted-set-1.0' -and $manifest.selectionId -eq 'rc4-no-preamble-5'
$review = $manifest.schemaVersion -eq 'accepted-set-2.0' -and $manifest.selectionId -eq 'cli-rc5dev-review-a3'
if (-not $historical -and -not $review) { throw 'Unknown accepted selection. This selector does not accept new candidates.' }
$requiredRoles = if ($historical) { @('executable', 'verifier', 'process-helper', 'sandbox-profile') }
    else { @('executable','verifier','verification-script','process-helper','hash-wrapper','sandbox-profile','package-manifest','vm-result','host-audit') }
if (@($manifest.artifacts).Count -ne $requiredRoles.Count) { throw 'Incomplete accepted set.' }
foreach ($role in $requiredRoles) {
    $artifacts = @($manifest.artifacts | Where-Object { $_.role -eq $role })
    if ($artifacts.Count -ne 1) { throw "Missing or duplicate role: $role" }
    $artifact = $artifacts[0]
    if ($artifact.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or [System.IO.Path]::IsPathRooted($artifact.path)) {
        throw "Invalid artifact identity: $role"
    }
    $path = [System.IO.Path]::GetFullPath((Join-Path $repository $artifact.path))
    if (-not $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Artifact must stay inside the repository.'
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $artifact.sha256) {
        throw "Hash mismatch: $role. Nothing was launched."
    }
}
if ($manifest.expected.externalActions -ne 0 -or $manifest.expected.deletedFiles -ne 0) { throw 'Contradictory R0 scope.' }
if ($historical) {
    if ($manifest.expected.passed -ne 13 -or $manifest.expected.total -ne 13 -or
        $manifest.expected.inventories -ne 2) { throw 'Contradictory historical acceptance criteria.' }
    Write-Output 'Historical expected result: 13/13, two inventories, no external actions or deletion.'
} else {
    if ($manifest.productionEnabled -ne $false -or $manifest.expected.passed -ne 32 -or $manifest.expected.total -ne 32 -or
        $manifest.expected.functionalReports -ne 4 -or $manifest.expected.rejectedReports -ne 11 -or
        $manifest.expected.inventoriesPerMenu -ne 2) { throw 'Contradictory review acceptance criteria.' }
    $auditArtifact = @($manifest.artifacts | Where-Object role -eq 'host-audit')[0]
    $audit = Get-Content -LiteralPath (Join-Path $repository $auditArtifact.path) -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($audit.Accepted -ne $true -or $audit.Cases -ne 32 -or $audit.FunctionalReports -ne 4 -or $audit.RejectedReports -ne 11) { throw 'Missing independent content acceptance.' }
    foreach ($binding in @(@('executable','CandidateSHA256'),@('verifier','VerifierSHA256'),@('package-manifest','ManifestSHA256'),@('vm-result','ResultSHA256'))) {
        if (@($manifest.artifacts | Where-Object role -eq $binding[0])[0].sha256 -ne $audit.($binding[1])) { throw 'Host audit is bound to a different artifact.' }
    }
    Write-Output 'Accepted review result: 32/32; four functional reports, eleven rejected reports, empty/nonempty R0 menus.'
}
Write-Output "Accepted selection: $($manifest.selectionId). All $($requiredRoles.Count) hashes match."
Write-Output 'Scope: Windows 11 R0 only. Windows 10, interactive UAC/keys/visual acceptance and production remain pending.'
Write-Output 'Nothing was launched. Do not use the obsolete verifier inside the original rc.4 package.'
