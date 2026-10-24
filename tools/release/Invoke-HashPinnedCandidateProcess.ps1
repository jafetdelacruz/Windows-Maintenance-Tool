. (Join-Path $PSScriptRoot 'Invoke-CandidateProcess.ps1')

function Invoke-HashPinnedCandidateProcess {
    param(
        [Parameter(Mandatory=$true)][string] $Executable,
        [Parameter(Mandatory=$true)][string] $ExpectedSha256,
        [string[]] $ProcessArguments = @(),
        [string] $MenuInput = '',
        [ValidateRange(100,60000)][int] $TimeoutMilliseconds = 60000
    )
    if ($ExpectedSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'An explicit expected SHA256 is required before launch.' }
    $path = [System.IO.Path]::GetFullPath($Executable)
    # Keep the EXE open without write/delete sharing through the child lifetime.
    # This is local artifact identity checking, not publisher authentication.
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose() }
        if ($actual -ne $ExpectedSha256) { throw 'Candidate SHA256 mismatch. Nothing was launched.' }
        Invoke-CandidateProcess -Executable $path -ProcessArguments $ProcessArguments -MenuInput $MenuInput -TimeoutMilliseconds $TimeoutMilliseconds
    } finally { $stream.Dispose() }
}
