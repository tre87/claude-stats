# claude-stats launcher — builds in Release if source is newer than the binary, then runs.

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project    = Join-Path $ScriptDir "ClaudeStats.Console\ClaudeStats.Console.csproj"
$Binary     = Join-Path $ScriptDir "ClaudeStats.Console\bin\Release\net10.0\ClaudeStats.Console.exe"
$SourceDir  = Join-Path $ScriptDir "ClaudeStats.Console"

function NeedsBuild {
    if (-not (Test-Path $Binary)) { return $true }

    $binaryTime = (Get-Item $Binary).LastWriteTime
    $newer = Get-ChildItem -Path $SourceDir -Recurse -Include "*.cs","*.csproj" |
             Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" } |
             Where-Object { $_.LastWriteTime -gt $binaryTime }

    return ($newer.Count -gt 0)
}

if (NeedsBuild) {
    Write-Host "Building..."
    dotnet build $Project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed."
        exit 1
    }
}

& $Binary @args
