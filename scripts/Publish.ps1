param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$PackageVersion = 'auto',
    [string]$CertificateThumbprint = ''
)

$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseDirectory = Join-Path $projectDirectory 'artifacts\MapleDay'
$appDirectory = Join-Path $releaseDirectory 'App'
$launcherSource = Join-Path $projectDirectory 'src\MapleDay.Launcher'
$launcherBuild = Join-Path $projectDirectory 'artifacts\build\launcher'
$launcherPath = Join-Path $releaseDirectory 'MapleDay.exe'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio C++ x64 build tools are required for the launcher.' }
$msvc = Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC\Tools\MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Lib') -Directory | Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkInclude = Join-Path $sdkRoot "Include\$($sdk.Name)"
$sdkLib = Join-Path $sdkRoot "Lib\$($sdk.Name)"
$compilerBin = Join-Path $msvc.FullName 'bin\Hostx64\x64'
$sdkBin = Join-Path $sdkRoot "bin\$($sdk.Name)\x64"

New-Item -ItemType Directory -Force -Path $appDirectory,$launcherBuild | Out-Null
if ($PackageVersion -eq 'auto') {
    [xml]$versionManifest = [IO.File]::ReadAllText((Join-Path $projectDirectory 'packaging\AppxManifest.xml'))
    $previousVersion = [version]$versionManifest.Package.Identity.Version
    $lastVersionPath = Join-Path $projectDirectory 'artifacts\build\msix\last-version.txt'
    if (Test-Path -LiteralPath $lastVersionPath) {
        $lastVersion = [version]([IO.File]::ReadAllText($lastVersionPath).Trim())
        if ($lastVersion -gt $previousVersion) { $previousVersion = $lastVersion }
    }
    $nextParts = @($previousVersion.Major, $previousVersion.Minor, $previousVersion.Build, 0)
    for ($partIndex = 2; $partIndex -ge 0; $partIndex--) {
        if ($nextParts[$partIndex] -lt 65535) { $nextParts[$partIndex]++; break }
        $nextParts[$partIndex] = 0
    }
    if ($nextParts[0] -eq 0) { throw 'Package version range exhausted.' }
    $PackageVersion = $nextParts -join '.'
}
# EXE assembly, installer and MSIX must compare as the same release version.
$publishVersion = [version]$PackageVersion
if ($publishVersion.Major -lt 1 -or $publishVersion.Revision -ne 0 -or
    @($publishVersion.Major,$publishVersion.Minor,$publishVersion.Build,$publishVersion.Revision | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw 'Package version requires four components, a nonzero major version and a zero fourth component.'
}
python (Join-Path $PSScriptRoot 'PrepareImageCache.py')
if ($LASTEXITCODE -ne 0) { throw 'Image cache preparation failed.' }
dotnet publish (Join-Path $projectDirectory 'src\MapleDay\MapleDay.csproj') `
    -c $Configuration -p:Platform=x64 -r win-x64 --self-contained true -o $appDirectory `
    "-p:Version=$PackageVersion" "-p:AssemblyVersion=$PackageVersion" "-p:FileVersion=$PackageVersion"
if ($LASTEXITCODE -ne 0) { throw "MapleDay publish failed (exit $LASTEXITCODE)." }
& (Join-Path $PSScriptRoot 'Verify-Runtime.ps1') -AppDirectory $appDirectory

$previousInclude = $env:INCLUDE
$previousLib = $env:LIB
$previousPath = $env:PATH
Push-Location $launcherSource
try {
    $env:INCLUDE = @((Join-Path $msvc.FullName 'include'),(Join-Path $sdkInclude 'ucrt'),(Join-Path $sdkInclude 'shared'),(Join-Path $sdkInclude 'um')) -join ';'
    $env:LIB = @((Join-Path $msvc.FullName 'lib\x64'),(Join-Path $sdkLib 'ucrt\x64'),(Join-Path $sdkLib 'um\x64')) -join ';'
    $env:PATH = "$compilerBin;$sdkBin;$previousPath"
    $resourceFile = Join-Path $launcherBuild 'MapleDay.res'
    & (Join-Path $sdkBin 'rc.exe') /nologo "/fo$resourceFile" MapleDay.rc
    if ($LASTEXITCODE -ne 0) { throw 'Launcher resource compilation failed.' }
    & (Join-Path $compilerBin 'cl.exe') /nologo /O2 /W4 /MT /EHsc /std:c++17 /utf-8 /DUNICODE /D_UNICODE `
        "/Fo$(Join-Path $launcherBuild 'MapleDay.obj')" "/Fe$launcherPath" main.cpp $resourceFile `
        /link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED kernel32.lib user32.lib shell32.lib bcrypt.lib
    if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
    $updaterTest = Join-Path $launcherBuild 'update-tests.exe'
    & (Join-Path $compilerBin 'cl.exe') /nologo /O2 /W4 /MT /EHsc /std:c++17 /utf-8 /DUNICODE /D_UNICODE `
        "/Fo$(Join-Path $launcherBuild 'update-tests.obj')" "/Fe$updaterTest" update-tests.cpp `
        /link /SUBSYSTEM:CONSOLE kernel32.lib bcrypt.lib
    if ($LASTEXITCODE -ne 0) { throw 'Native update tests compilation failed.' }
    & $updaterTest (Join-Path $launcherBuild 'update-fixture')
    if ($LASTEXITCODE -ne 0) { throw 'Native update replacement/rollback tests failed.' }
} finally {
    Pop-Location
    $env:INCLUDE = $previousInclude
    $env:LIB = $previousLib
    $env:PATH = $previousPath
}

python (Join-Path $PSScriptRoot 'Build-Update.py') $releaseDirectory (Join-Path $projectDirectory 'artifacts\Updates') $PackageVersion
if ($LASTEXITCODE -ne 0) { throw 'Incremental update archive build failed.' }
& (Join-Path $PSScriptRoot 'Build-MSIX.ps1') -AppDirectory $appDirectory -SdkBin $sdkBin `
    -PackageVersion $PackageVersion -CertificateThumbprint $CertificateThumbprint
& (Join-Path $PSScriptRoot 'Build-Installer.ps1')
Write-Output $launcherPath
