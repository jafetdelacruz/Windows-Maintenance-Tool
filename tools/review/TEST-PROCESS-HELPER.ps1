param(
    [Parameter(Mandatory=$true)][string] $FixtureExe,
    [Parameter(Mandatory=$true)][string] $HelperPath,
    [Parameter(Mandatory=$true)][string] $Case,
    [Parameter(Mandatory=$true)][string] $FixtureDirectory,
    [Parameter(Mandatory=$true)][string] $ExpectedFixtureSha256,
    [switch] $WithBom
)
$ErrorActionPreference = 'Stop'
. $HelperPath
[Console]::InputEncoding = New-Object Text.UTF8Encoding([bool]$WithBom)
$beforeCodePage = [Console]::InputEncoding.CodePage
$beforePreamble = [Console]::InputEncoding.GetPreamble().Length
function Assert-True([bool] $Value, [string] $Message) { if (-not $Value) { throw $Message } }
switch ($Case) {
    'menu' {
        $result = Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('menu') -MenuInput "5`n5`n10" -TimeoutMilliseconds 5000
        Assert-True ($result.ExitCode -eq 0) 'Child failed.'
        $consumed = @([regex]::Matches($result.Output, 'CONSUMED:([A-F0-9]*)') | ForEach-Object { $_.Groups[1].Value })
        Assert-True (($consumed -join ',') -eq '35,35,3130') 'Child did not consume exactly 5/5/10 without BOM.'
        Assert-True ($result.SentChoices -eq 3 -and $result.InputPreambleLength -eq 0) 'Prompt/encoding contract failed.'
    }
    'eof' {
        $result = Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('eof') -TimeoutMilliseconds 5000
        Assert-True ($result.Output.Contains('EOF RECEIVED') -and $result.ExitCode -eq 0) 'EOF was not delivered.'
    }
    'arguments' {
        $result = Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('arguments', 'an argument with spaces', '') -TimeoutMilliseconds 5000
        Assert-True ($result.ExitCode -eq 0 -and $result.Output.Contains('ARG:an argument with spaces')) 'Argument was split or lost.'
        $expectedStderr = 'STDERR: ' + [char]0xE1 + [char]0xE9 + [char]0xED + [char]0xF3 + [char]0xFA
        Assert-True ($result.Output.Contains($expectedStderr)) 'UTF8 stderr was not captured.'
        Assert-True ([regex]::Matches($result.Output, 'ARG:').Count -eq 2) 'Empty argument was lost.'
    }
    'nonzero' {
        $result = Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('nonzero') -TimeoutMilliseconds 5000
        Assert-True ($result.ExitCode -eq 23 -and $result.Output.Contains('EXPECTED FAILURE')) 'Nonzero exit was hidden.'
    }
    'timeout' {
        $pidPath = Join-Path $FixtureDirectory 'owned-child.pid'
        $threw = $false
        try { Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('timeout', $pidPath) -TimeoutMilliseconds 800 | Out-Null }
        catch { $threw = $_.Exception.Message.Contains('exceeded its time limit') }
        Assert-True $threw 'Expected bounded timeout.'
        Assert-True (Test-Path -LiteralPath $pidPath) 'Fixture child did not start; timeout not exercised.'
        $fixturePid = [int](Get-Content -LiteralPath $pidPath -Raw)
        Assert-True ($null -eq (Get-Process -Id $fixturePid -ErrorAction SilentlyContinue)) 'Owned fixture survived helper timeout.'
    }
    'invalid-input' {
        $threw = $false
        try { Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('eof') -MenuInput 'not a menu option' | Out-Null }
        catch { $threw = $_.Exception.Message.Contains('only digits and newlines') }
        Assert-True $threw 'Invalid input was accepted.'
    }
    'unsupported-argument' {
        $threw = $false
        try { Invoke-CandidateProcess -Executable $FixtureExe -ProcessArguments @('arguments', 'a"b') | Out-Null }
        catch { $threw = $_.Exception.Message.Contains('Unsupported argument') }
        Assert-True $threw 'Unsupported argument was accepted.'
    }
    'hash-mismatch' {
        . (Join-Path (Split-Path -Parent $HelperPath) 'Invoke-HashPinnedCandidateProcess.ps1')
        $pidPath = Join-Path $FixtureDirectory 'must-not-start.pid'
        $threw = $false
        try { Invoke-HashPinnedCandidateProcess -Executable $FixtureExe -ExpectedSha256 ('0' * 64) -ProcessArguments @('timeout', $pidPath) -TimeoutMilliseconds 800 | Out-Null }
        catch { $threw = $_.Exception.Message.Contains('SHA256 mismatch') }
        Assert-True $threw 'A mismatched executable was not rejected.'
        Assert-True (-not (Test-Path -LiteralPath $pidPath)) 'The mismatched child was launched.'
    }
    'hash-match' {
        . (Join-Path (Split-Path -Parent $HelperPath) 'Invoke-HashPinnedCandidateProcess.ps1')
        $result = Invoke-HashPinnedCandidateProcess -Executable $FixtureExe -ExpectedSha256 $ExpectedFixtureSha256 -ProcessArguments @('eof') -TimeoutMilliseconds 5000
        Assert-True ($result.ExitCode -eq 0 -and $result.Output.Contains('EOF RECEIVED')) 'Matching child did not run.'
    }
    default { throw 'Unknown regression case.' }
}
Assert-True ([Console]::InputEncoding.CodePage -eq $beforeCodePage) 'Parent code page was not restored.'
Assert-True ([Console]::InputEncoding.GetPreamble().Length -eq $beforePreamble) 'Parent preamble was not restored.'
Write-Output "PASSED:$Case;PARENT_PREAMBLE:$beforePreamble"
Write-Output "HOST_MAJOR:$($PSVersionTable.PSVersion.Major)"
