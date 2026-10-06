[CmdletBinding()]
param(
    [ValidateSet('target', 'full-suite')][string]$Mode = 'target',
    [ValidateRange(1, 20)][int]$Iterations = 1,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Checkpoint file-I/O tracing requires native Windows.' }
if ($Mode -eq 'full-suite' -and $Iterations -ne 1) { throw 'Run one full suite per capture.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    if (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -ne 0) { throw 'Use a new, empty evidence directory.' }
}
[void][IO.Directory]::CreateDirectory($outputRoot)
$rawRoot = Join-Path $outputRoot 'raw'
[void][IO.Directory]::CreateDirectory($rawRoot)
$evidenceRoot = Join-Path $outputRoot 'checkpoint-evidence'
[void][IO.Directory]::CreateDirectory($evidenceRoot)
$utf8 = [Text.UTF8Encoding]::new($false)
$project = Join-Path $repoRoot 'tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj'
$testBinaryRoot = Join-Path $repoRoot 'tests/ClankerWorld.Simulation.Tests/bin/Release/net10.0'
$provider = 'Microsoft-Windows-Kernel-File'
$fixturePattern = '(?i)(?:^|[\\/])(clankerworld-(?:history-soak|checkpoint-blocked|checkpoint-obstructed)-[0-9a-f]{32})(?:[\\/]|$)'

function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), $utf8)
}

function Get-BinaryPins {
    $pins = [ordered]@{}
    foreach ($assembly in @('ClankerWorld.Simulation', 'ClankerWorld.Viewer', 'ClankerWorld.Simulation.Tests')) {
        foreach ($suffix in @('dll', 'pdb')) {
            $name = "$assembly.$suffix"
            $pins[$name] = (Get-FileHash -LiteralPath (Join-Path $testBinaryRoot $name) -Algorithm SHA256).Hash
        }
    }
    return $pins
}

function Export-CheckpointEvents([string]$TracePath, [string]$Name, [bool]$AllowEmpty) {
    $decoded = Join-Path $rawRoot "$Name.xml"
    $summary = Join-Path $outputRoot "$Name-trace-summary.txt"
    & tracerpt.exe $TracePath -of XML -o $decoded -summary $summary -y > (Join-Path $outputRoot "$Name-decode.log") 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native trace decoding failed for $Name ($LASTEXITCODE)." }
    $related = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $counts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::OrdinalIgnoreCase)
    $refusals = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::OrdinalIgnoreCase)
    $readCount = 0
    $selectedCount = 0
    $reader = [Xml.XmlReader]::Create($decoded)
    $writer = [IO.StreamWriter]::new((Join-Path $outputRoot "$Name-checkpoint-events.jsonl"), $false, $utf8)
    try {
        while (-not $reader.EOF) {
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element -or $reader.LocalName -ne 'Event') {
                [void]$reader.Read()
                continue
            }
            $rawEvent = $reader.ReadOuterXml()
            $readCount++
            [xml]$eventXml = $rawEvent
            $eventId = $eventXml.SelectSingleNode("//*[local-name()='System']/*[local-name()='EventID']").InnerText
            $data = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($field in $eventXml.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']")) {
                $fieldName = $field.GetAttribute('Name')
                if ($fieldName) { $data[$fieldName] = $field.InnerText }
            }
            foreach ($field in $eventXml.SelectNodes("//*[local-name()='UserData']//*[not(*)]")) {
                $data[$field.LocalName] = $field.InnerText
            }
            $keys = @($data.Keys | Where-Object { $_ -match '^(FileObject|FileKey|Irp|IrpPtr)$' -and $data[$_] -notmatch '^(?:0x)?0+$' })
            # An IRP describes one operation, not the lifetime of a file. A new
            # operation must resolve through its own name or file identities.
            $irpKeys = @($keys | Where-Object { $_ -match '^Irp(?:Ptr)?$' })
            if ($eventId -ne '24') {
                foreach ($key in $irpKeys) { [void]$related.Remove("$key=$($data[$key])") }
            }
            $namedPaths = @($data.Keys | Where-Object { $_ -match '^(FileName|OpenPath|FilePath|Path|Name)$' } | ForEach-Object { $data[$_] })
            $path = $null
            foreach ($candidate in $namedPaths) {
                if ($candidate -match $fixturePattern) { $path = $candidate; break }
            }
            # A new name for a reused native identity must clear its old association.
            if ($namedPaths.Count -gt 0) {
                foreach ($key in $keys) { [void]$related.Remove("$key=$($data[$key])") }
            }
            if (-not $path) {
                foreach ($key in $keys) {
                    $known = $null
                    if ($related.TryGetValue("$key=$($data[$key])", [ref]$known)) { $path = $known; break }
                }
            }
            if ($eventId -eq '24') {
                foreach ($key in $irpKeys) { [void]$related.Remove("$key=$($data[$key])") }
            }
            if (-not $path) { continue }
            if ($eventId -ne '24') {
                foreach ($key in $keys) { $related["$key=$($data[$key])"] = $path }
            }
            $fixture = [regex]::Match($path, $fixturePattern).Groups[1].Value
            if (-not $counts.ContainsKey($fixture)) { $counts[$fixture] = 0 }
            $counts[$fixture]++
            $selectedCount++
            if ($eventId -eq '24' -and $data.ContainsKey('Status') -and $data['Status'] -eq '0xC0000022') {
                $processId = $eventXml.SelectSingleNode("//*[local-name()='System']/*[local-name()='Execution']").GetAttribute('ProcessID')
                $refusalKey = "$fixture|$processId"
                if (-not $refusals.ContainsKey($refusalKey)) { $refusals[$refusalKey] = 0 }
                $refusals[$refusalKey]++
            }
            # Preserve the event's original fields, native process/thread IDs and status.
            # Association supplies a path for operation-end events that carry only an IRP.
            $writer.WriteLine(([ordered]@{ Fixture = $fixture; ResolvedPath = $path; Xml = $rawEvent } | ConvertTo-Json -Compress))
            # Kernel-File Close retires the file object; NameDelete retires its key.
            foreach ($key in $keys) {
                if (($eventId -eq '14' -and $key -eq 'FileObject') -or ($eventId -eq '11' -and $key -eq 'FileKey')) {
                    [void]$related.Remove("$key=$($data[$key])")
                }
            }
        }
    }
    finally { $reader.Dispose(); $writer.Dispose() }
    if ($selectedCount -eq 0 -and -not $AllowEmpty) { throw 'The trace contained no decoded synthetic checkpoint activity.' }
    return [ordered]@{
        DecodedEvents = $readCount
        CheckpointEvents = $selectedCount
        Fixtures = $counts
        AccessDeniedCompletionsByFixtureAndProcess = $refusals
        RawTraceSha256 = (Get-FileHash -LiteralPath $TracePath -Algorithm SHA256).Hash
        RawTraceBytes = (Get-Item -LiteralPath $TracePath).Length
        SummaryFile = [IO.Path]::GetFileName($summary)
        Association = 'File identities reset on names/close/delete; IRP retained only until operation end; inferred paths are not root-cause evidence'
    }
}

function Invoke-TracedTests([string]$Name, [string]$Filter, [bool]$Calibration) {
    $session = 'ClankerWorldCheckpoint-' + [Guid]::NewGuid().ToString('N')
    $trace = Join-Path $rawRoot "$Name.etl"
    $traceRunning = $false
    $freezeReason = 'test-process-exited'
    $started = [DateTimeOffset]::UtcNow
    $exitCode = $null
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $process = [Diagnostics.Process]::new()
    try {
        & logman.exe start $session -ets -o $trace -p $provider '0xffffffffffffffff' 5 -f bincirc -max 128 -nb 16 64 -bs 256 > (Join-Path $outputRoot "$Name-trace-start.log") 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Native file-I/O trace startup failed ($LASTEXITCODE)." }
        $traceRunning = $true
        $process.StartInfo.FileName = (Get-Command dotnet).Source
        $process.StartInfo.WorkingDirectory = $repoRoot
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        foreach ($argument in @('test', $project, '--configuration', 'Release', '--no-build', '--no-restore',
            '--logger', "trx;LogFileName=$Name.trx", '--results-directory', (Join-Path $outputRoot 'results'))) {
            $process.StartInfo.ArgumentList.Add($argument)
        }
        if ($Filter) { $process.StartInfo.ArgumentList.Add('--filter'); $process.StartInfo.ArgumentList.Add($Filter) }
        elseif (-not $Calibration) {
            foreach ($argument in @('--collect', 'XPlat Code Coverage', '--settings',
                (Join-Path $repoRoot 'skills/test-audit/references/coverage.runsettings'))) {
                $process.StartInfo.ArgumentList.Add($argument)
            }
        }
        if (-not $process.Start()) { throw 'Could not start the unchanged checkpoint test process.' }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        Write-Host "Tracing $Name without changing its test assertions."
        while (-not $process.WaitForExit(1000)) {
            if (-not $traceRunning) { continue }
            foreach ($failure in Get-ChildItem -LiteralPath $evidenceRoot -Filter 'escaping-exception.json' -File -Recurse) {
                if ($seen.Contains($failure.FullName)) { continue }
                try { $record = [IO.File]::ReadAllText($failure.FullName) | ConvertFrom-Json }
                catch { continue } # A file still being written is read again next time.
                [void]$seen.Add($failure.FullName)
                if (-not $record.ExpectedFailureControl) {
                    & logman.exe stop $session -ets > (Join-Path $outputRoot "$Name-trace-stop.log") 2>&1
                    if ($LASTEXITCODE -ne 0) { throw 'Could not freeze the unexpected-failure trace.' }
                    $traceRunning = $false
                    $freezeReason = 'unexpected-escaping-checkpoint-exception'
                    Write-Host 'An unexplained checkpoint failure froze the trace; the tests continue unchanged.'
                    break
                }
            }
        }
        $exitCode = $process.ExitCode
        [IO.File]::WriteAllText((Join-Path $outputRoot "$Name-stdout.log"), $standardOutput.GetAwaiter().GetResult(), $utf8)
        [IO.File]::WriteAllText((Join-Path $outputRoot "$Name-stderr.log"), $standardError.GetAwaiter().GetResult(), $utf8)
        Write-Host ([IO.File]::ReadAllText((Join-Path $outputRoot "$Name-stdout.log")))
    }
    finally {
        if ($traceRunning) {
            & logman.exe stop $session -ets > (Join-Path $outputRoot "$Name-trace-stop.log") 2>&1
            if ($LASTEXITCODE -ne 0) { Write-Warning "Trace shutdown failed: $LASTEXITCODE" }
        }
        $process.Dispose()
    }
    $traceResult = Export-CheckpointEvents $trace $Name (-not $Calibration -and -not $Filter)
    if ($Calibration) {
        $controls = @(Get-ChildItem -LiteralPath $evidenceRoot -Filter 'first-chance.json' -File -Recurse | ForEach-Object {
            [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json
        } | Where-Object { $_.ExpectedFailureControl })
        if ($controls.Count -ne 3) { throw 'Calibration must retain the three existing native refusal controls.' }
        foreach ($control in $controls) {
            $fixture = [IO.Path]::GetFileName($control.DirectoryPath)
            if (-not $traceResult.Fixtures.ContainsKey($fixture)) { throw "Calibration trace did not resolve $fixture." }
            if (-not $traceResult.AccessDeniedCompletionsByFixtureAndProcess.ContainsKey("$fixture|$($control.ProcessId)")) {
                throw "Calibration trace did not retain a native access-denied completion for $fixture in the control process."
            }
        }
    }
    Write-Json (Join-Path $outputRoot "$Name-result.json") ([ordered]@{
        Name = $Name; Filter = $Filter; StartedUtc = $started; FinishedUtc = [DateTimeOffset]::UtcNow
        TestExitCode = $exitCode; TraceFrozenBecause = $freezeReason; Trace = $traceResult
        CalibrationOnly = $Calibration
    })
    return $exitCode
}

$previousEvidence = $env:CLANKERWORLD_CHECKPOINT_DIAGNOSTICS
$previousProcessors = $env:DOTNET_PROCESSOR_COUNT
$env:CLANKERWORLD_CHECKPOINT_DIAGNOSTICS = $evidenceRoot
$env:DOTNET_PROCESSOR_COUNT = '4'
$finalExit = 1
try {
    Push-Location $repoRoot
    try {
        & dotnet restore $project --locked-mode > (Join-Path $outputRoot 'restore.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
        & dotnet build $project --configuration Release --no-restore > (Join-Path $outputRoot 'build.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
        & dotnet --info > (Join-Path $outputRoot 'dotnet-info.log') 2>&1
        $source = (& git rev-parse HEAD).Trim()
        $pins = Get-BinaryPins
        Write-Json (Join-Path $outputRoot 'capture.json') ([ordered]@{
            SourceRevision = $source; Mode = $Mode; Iterations = $Iterations
            OperatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            EffectiveProcessors = [Environment]::ProcessorCount; TestProcessorLimit = 4
            Provider = $provider; CircularTraceMegabytes = 128; BeforeBinaryPins = $pins
            Publication = 'Only synthetic checkpoint events, diagnostics, logs and reports; raw ETL/XML remain local.'
        })
        $controlFilter = 'FullyQualifiedName~WindowsReaderWithoutDeleteSharingPreservesCheckpointUntilReleased|FullyQualifiedName~WindowsReaderAllowingDeleteSharingPreservesCheckpointUntilReleased|FullyQualifiedName~WindowsReadOnlyCheckpointPreservesBytesUntilAttributeIsRemoved'
        $finalExit = Invoke-TracedTests 'controls' $controlFilter $true
        if ($finalExit -eq 0) {
            $targetFilter = if ($Mode -eq 'target') { 'FullyQualifiedName~EquivalentTicksKeepEveryHotHistoryBoundedAcrossRepeatedCompaction' } else { '' }
            for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
                $finalExit = Invoke-TracedTests ('run-{0:D3}' -f $iteration) $targetFilter $false
                if ($finalExit -ne 0) { break } # Never mask the first failure with a passing rerun.
            }
        }
        $afterPins = Get-BinaryPins
        Write-Json (Join-Path $outputRoot 'after-binary-pins.json') $afterPins
        foreach ($name in $pins.Keys) {
            if ($pins[$name] -ne $afterPins[$name]) { throw "Test binary changed during capture: $name" }
        }
    }
    finally { Pop-Location }
}
finally {
    $env:CLANKERWORLD_CHECKPOINT_DIAGNOSTICS = $previousEvidence
    $env:DOTNET_PROCESSOR_COUNT = $previousProcessors
}
exit $finalExit
