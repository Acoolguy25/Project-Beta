$unityVersion = "6000.4.6f1"
$projectVersion = Join-Path (Get-Location) "ProjectSettings\ProjectVersion.txt"

if (Test-Path $projectVersion) {
    $versionLine = Get-Content $projectVersion | Where-Object { $_ -match "^m_EditorVersion:\s+(.+)$" } | Select-Object -First 1
    if ($versionLine -match "^m_EditorVersion:\s+(.+)$") {
        $unityVersion = $Matches[1]
    }
}

$unityCsc = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Data\DotNetSdkRoslyn\csc.dll"
if ($env:UNITY_ROSLYN_CSC) {
    $unityCsc = $env:UNITY_ROSLYN_CSC
}

function Write-TraceLines([string[]]$lines) {
    if ($env:UNITY_ROSLYN_TRACE) {
        Add-Content -Path $env:UNITY_ROSLYN_TRACE -Value $lines
    }
}

function Split-CompilerArgs([string]$text) {
    @(
        [regex]::Matches($text, '("[^"]*"|\S+)') |
            ForEach-Object { $_.Value.Trim('"') }
    )
}

function Format-ResponseArg([string]$arg) {
    if ($arg -match "\s") {
        '"' + $arg.Replace('"', '\"') + '"'
    } else {
        $arg
    }
}

$compilerArgs = @($args)
if ($compilerArgs.Count -eq 1 -and $compilerArgs[0] -match "\s") {
    $compilerArgs = Split-CompilerArgs $compilerArgs[0]
}

$filteredArgs = @()
$tempResponseFiles = @()

Write-TraceLines @("ARGS:", ($compilerArgs | ForEach-Object { "  $_" }))

foreach ($arg in $compilerArgs) {
    if ($arg -match "^[/-]sdkpath:") {
        Write-TraceLines @("SKIP ARG: $arg")
        continue
    }

    if ($arg.StartsWith("@")) {
        $responseFile = $arg.Substring(1).Trim('"')
        Write-TraceLines @("RSP ARG: $responseFile")
        if (Test-Path $responseFile) {
            $responseArgs = Split-CompilerArgs ([System.IO.File]::ReadAllText($responseFile))
            $filteredResponseArgs = @($responseArgs | Where-Object { $_ -notmatch "^[/-]sdkpath:" })

            if ($filteredResponseArgs.Count -ne $responseArgs.Count) {
                $tempResponseFile = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), ".rsp")
                $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
                $filteredLines = @($filteredResponseArgs | ForEach-Object { Format-ResponseArg $_ })
                [System.IO.File]::WriteAllLines($tempResponseFile, $filteredLines, $utf8NoBom)
                if ($env:UNITY_ROSLYN_KEEP_RSP) {
                    Write-Host "Filtered response file: $tempResponseFile"
                }
                Write-TraceLines @("FILTERED RSP: $tempResponseFile")
                $tempResponseFiles += $tempResponseFile
                $filteredArgs += "@$tempResponseFile"
                continue
            }
        }
    }

    $filteredArgs += $arg
}

& dotnet $unityCsc @filteredArgs
$exitCode = $LASTEXITCODE

if (-not $env:UNITY_ROSLYN_KEEP_RSP) {
    foreach ($tempResponseFile in $tempResponseFiles) {
        Remove-Item -Path $tempResponseFile -ErrorAction SilentlyContinue
    }
}

exit $exitCode
