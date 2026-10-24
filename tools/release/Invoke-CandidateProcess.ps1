function Invoke-CandidateProcess {
    param(
        [Parameter(Mandatory=$true)][string]$Executable,
        [string[]]$ProcessArguments = @(),
        [string]$MenuInput = '',
        [ValidateRange(100,60000)][int]$TimeoutMilliseconds = 60000
    )
    # Controlled arguments only. No shell is involved; reject unsupported quoting.
    $quoted = foreach ($argument in $ProcessArguments) {
        if ($argument.Contains('"') -or $argument.EndsWith('\') -or $argument -match '[\x00-\x1F]') {
            throw 'Unsupported argument in verification process.'
        }
        '"' + $argument + '"'
    }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = $quoted -join ' '
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $start.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    $started = $false
    $parentInputEncoding = [Console]::InputEncoding
    try {
        # .NET Framework derives the child's stdin writer from Console.InputEncoding.
        # With UTF-8+BOM it can emit EF-BB-BF during process startup, before our raw writes.
        [Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
        if (-not $process.Start()) { throw 'Verification process did not start.' }
        $started = $true
        $preambleLength = $process.StandardInput.Encoding.GetPreamble().Length
        if ($preambleLength -ne 0) { throw 'Unexpected stdin writer preamble; not accepted.' }
        $stderr = $process.StandardError.ReadToEndAsync()
        if ($MenuInput -match '[^0-9\r\n]') { throw 'Verification input must contain only digits and newlines.' }
        $sent = 0
        $trace = @()
        if ($MenuInput.Length -gt 0) {
            # Keep stdin open. Send each choice only after the corresponding real menu prompt.
            $choices = @($MenuInput -split "`n")
            $text = New-Object Text.StringBuilder
            # One character avoids ReadAsync waiting to fill a block while the app waits for stdin.
            $buffer = New-Object char[] 1
            $tail = ''
            $prompts = 0
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $read = $process.StandardOutput.ReadAsync($buffer, 0, $buffer.Length)
            while ($true) {
                if ($clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) { throw 'Verification process exceeded its time limit; not accepted.' }
                if (-not $read.IsCompleted) { [Threading.Thread]::Sleep(10); continue }
                $length = $read.GetAwaiter().GetResult()
                if ($length -eq 0) { break }
                [void]$text.Append($buffer, 0, $length)
                $tail += [string]$buffer[0]
                if ($tail.Length -gt 16) { $tail = $tail.Substring($tail.Length - 16) }
                if ($tail -match 'Opci\u00F3n: $') { $prompts++ }
                while ($sent -lt $prompts -and $sent -lt $choices.Count) {
                    $part = $choices[$sent] + $(if ($sent -lt $choices.Count - 1) { "`n" } else { '' })
                    $bytes = [Text.Encoding]::ASCII.GetBytes($part)
                    $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
                    $process.StandardInput.BaseStream.Flush()
                    $sent++
                    $trace += 'Prompt ' + $sent + ': choice ' + $choices[$sent - 1]
                    if ($sent -eq $choices.Count) { $process.StandardInput.Close() }
                }
                $read = $process.StandardOutput.ReadAsync($buffer, 0, $buffer.Length)
            }
            $stdoutText = $text.ToString()
            $remaining = [Math]::Max(1, $TimeoutMilliseconds - [int]$clock.ElapsedMilliseconds)
            if (-not $process.WaitForExit($remaining)) { throw 'Verification process exceeded its time limit; not accepted.' }
        } else {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $process.StandardInput.Close()
            if (-not $process.WaitForExit($TimeoutMilliseconds)) { throw 'Verification process exceeded its time limit; not accepted.' }
            $stdoutText = $stdout.GetAwaiter().GetResult()
        }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdoutText + $stderr.GetAwaiter().GetResult()
            InputHex = $(if ($MenuInput.Length -gt 0 -and $sent -eq $choices.Count) { [BitConverter]::ToString([Text.Encoding]::ASCII.GetBytes($MenuInput)) } else { '' })
            SentChoices = $sent
            InputTrace = $trace
            InputPreambleLength = $preambleLength
        }
    } finally {
        if ($started -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
        [Console]::InputEncoding = $parentInputEncoding
    }
}
