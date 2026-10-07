param([string]$InnoCompiler = '')

$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseDirectory = Join-Path $projectDirectory 'artifacts\MapleDay'
$installerDirectory = Join-Path $projectDirectory 'artifacts\Installer'
$buildDirectory = Join-Path $projectDirectory 'artifacts\build\installer'
if (-not $InnoCompiler) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $InnoCompiler = $compilerCommand.Source }
    else {
        $InnoCompiler = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 is required to build the EXE installer.'
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectDirectory 'packaging\AppxManifest.xml') -Raw
$version = [version]$manifest.Package.Identity.Version
if (-not (Test-Path -LiteralPath (Join-Path $releaseDirectory 'MapleDay.exe')) -or
    -not (Test-Path -LiteralPath (Join-Path $releaseDirectory 'App\MapleDay.dll'))) {
    throw 'Publish the application before building its installer.'
}
New-Item -ItemType Directory -Force -Path $installerDirectory,$buildDirectory | Out-Null
$log = Join-Path $buildDirectory 'iscc.log'
& $InnoCompiler /Q "/DPackageVersion=$version" "/DReleaseDirectory=$releaseDirectory" "/O$buildDirectory" `
    (Join-Path $projectDirectory 'packaging\MapleDay.iss') *> $log
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 20; throw 'Installer compilation failed.' }
$name = "MapleDay-Setup-$version-x64.exe"
$pendingInstaller = Join-Path $buildDirectory $name
$installer = Join-Path $installerDirectory $name
if (-not (Test-Path -LiteralPath $pendingInstaller) -or (Get-Item -LiteralPath $pendingInstaller).Length -lt 1MB) {
    throw 'Expected installer output is missing or incomplete.'
}
Move-Item -LiteralPath $pendingInstaller -Destination $installer -Force
Write-Output $installer
