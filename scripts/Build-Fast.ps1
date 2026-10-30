param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $projectDirectory 'src\MapleDay\MapleDay.csproj'
$assets = Join-Path $projectDirectory 'src\MapleDay\obj\project.assets.json'

if ($Restore -or -not (Test-Path -LiteralPath $assets -PathType Leaf)) {
    dotnet restore $project -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "MapleDay restore failed (exit $LASTEXITCODE)." }
}

dotnet build $project -c $Configuration -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw "MapleDay fast build failed (exit $LASTEXITCODE)." }

Write-Output "Fast $Configuration build completed. Use scripts/Publish.ps1 only when EXE, MSIX and installer artifacts are required."
