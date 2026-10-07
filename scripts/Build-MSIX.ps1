param(
    [Parameter(Mandatory = $true)][string]$AppDirectory,
    [Parameter(Mandatory = $true)][string]$SdkBin,
    [string]$PackageVersion = 'auto',
    [string]$CertificateThumbprint = ''
)

$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appDirectory = [IO.Path]::GetFullPath($AppDirectory)
$packageDirectory = Join-Path $projectDirectory 'artifacts\MSIX'
$buildDirectory = Join-Path $projectDirectory 'artifacts\build\msix'
$packagePath = Join-Path $packageDirectory 'MapleDay.msix'
$certificatePath = Join-Path $packageDirectory 'MapleYoil.cer'
$versionPath = Join-Path $buildDirectory 'last-version.txt'
$makeAppx = Join-Path $SdkBin 'makeappx.exe'
$signTool = Join-Path $SdkBin 'signtool.exe'
foreach ($required in @($makeAppx, $signTool, (Join-Path $appDirectory 'MapleDay.exe'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required MSIX build file missing: $required" }
}
New-Item -ItemType Directory -Force -Path $packageDirectory,$buildDirectory | Out-Null

[xml]$manifest = [IO.File]::ReadAllText((Join-Path $projectDirectory 'packaging\AppxManifest.xml'))
if ($PackageVersion -eq 'auto') {
    $previous = [version]$manifest.Package.Identity.Version
    if (Test-Path -LiteralPath $versionPath) {
        $last = [version]([IO.File]::ReadAllText($versionPath).Trim())
        if ($last -gt $previous) { $previous = $last }
    }
    # Microsoft Store reserves the fourth component. Increment the build component.
    $parts = @($previous.Major, $previous.Minor, $previous.Build, 0)
    for ($index = 2; $index -ge 0; $index--) {
        if ($parts[$index] -lt 65535) { $parts[$index]++; break }
        $parts[$index] = 0
    }
    if ($parts[0] -eq 0) { throw 'MSIX version range exhausted.' }
    $PackageVersion = $parts -join '.'
}
$version = [version]$PackageVersion
if ($version.Major -lt 1 -or $version.Revision -ne 0 -or @($version.Major,$version.Minor,$version.Build,$version.Revision | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw 'Store PackageVersion must have four components in the range 0..65535, a nonzero major version, and a zero fourth component.'
}
$manifest.Package.Identity.Version = $version.ToString()

# Reuse a non-exportable local signing key. Only its public certificate is distributed.
$subject = [string]$manifest.Package.Identity.Publisher
if ($CertificateThumbprint) {
    $thumbprint = $CertificateThumbprint.Replace(' ', '')
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint"
} else {
    $certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30)
    } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $certificate) {
        $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject -FriendlyName 'MapleYoil - MapleDay development signing' `
            -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -KeyExportPolicy NonExportable `
            -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(3) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
}
if ($certificate.Subject -ne $subject -or -not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) {
    throw "Signing certificate must be valid, have a private key, and match publisher $subject."
}
$pendingCertificate = Join-Path $buildDirectory 'MapleYoil.cer'
Export-Certificate -Cert $certificate -FilePath $pendingCertificate -Force | Out-Null

$generatedManifest = Join-Path $buildDirectory 'AppxManifest.xml'
$manifest.Save($generatedManifest)
$assets = Join-Path $buildDirectory 'PackageAssets'
python (Join-Path $PSScriptRoot 'PreparePackageAssets.py') $assets
if ($LASTEXITCODE -ne 0) { throw 'MSIX package logo preparation failed.' }

# Map the app body to the package root so existing ms-appx:///Assets paths stay valid.
# No second executable deployment folder or copy of the entire runtime is created.
$mapping = [Collections.Generic.List[string]]::new()
$mapping.Add('[Files]')
$mapping.Add(('"{0}" "AppxManifest.xml"' -f $generatedManifest))
foreach ($file in Get-ChildItem -LiteralPath $appDirectory -Recurse -File | Sort-Object FullName) {
    $relative = $file.FullName.Substring($appDirectory.TrimEnd('\').Length + 1)
    $mapping.Add(('"{0}" "{1}"' -f $file.FullName, $relative))
}
# Reindex resources for the Store identity while preserving embedded XAML and SDK resources.
$packageResources = Join-Path $buildDirectory 'resources.pri'
& (Join-Path $PSScriptRoot 'PreparePackageResources.ps1') -SourcePri (Join-Path $appDirectory 'MapleDay.pri') `
    -OutputPri $packageResources -SdkBin $SdkBin -IndexName ([string]$manifest.Package.Identity.Name)
$mapping.Add(('"{0}" "resources.pri"' -f $packageResources))
foreach ($file in Get-ChildItem -LiteralPath $assets -File) {
    $mapping.Add(('"{0}" "PackageAssets\{1}"' -f $file.FullName, $file.Name))
}
$mappingPath = Join-Path $buildDirectory 'mapping.txt'
[IO.File]::WriteAllLines($mappingPath, $mapping, [Text.UTF8Encoding]::new($false))
# Create under a temporary filename; replace the last successful package only after validation.
$pendingPackage = Join-Path $buildDirectory 'MapleDay.pending.msix'
$packLog = Join-Path $buildDirectory 'makeappx.log'
& $makeAppx pack /f $mappingPath /p $pendingPackage /o *> $packLog
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $packLog -Tail 25; throw 'MSIX semantic validation or packaging failed.' }
& $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $pendingPackage
if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
& (Join-Path $PSScriptRoot 'Verify-MSIX.ps1') -PackagePath $pendingPackage -CertificatePath $pendingCertificate
Move-Item -LiteralPath $pendingPackage -Destination $packagePath -Force
Copy-Item -LiteralPath $pendingCertificate -Destination $certificatePath -Force
[IO.File]::WriteAllText($versionPath, $version.ToString())

$instructions = @'
메요일 · MapleDay MSIX (x64)
제작자: MapleYoil
스토어 퍼블리셔 표시 이름: 메요일

MapleDay.msix는 로컬 개발용 인증서로 서명되어 있습니다.
Microsoft Store 제출에는 MapleDay.msix를 업로드합니다. 인증서 등록 안내는 직접 설치할 때만 해당합니다.
처음 설치하는 PC에서는 MapleYoil.cer를 신뢰할 수 있는 사용자(Trusted People)에 먼저 등록해야 합니다.
인증서 등록은 빌드 스크립트에서 자동으로 수행하지 않습니다.

1. MapleYoil.cer를 열고 인증서 설치를 선택합니다.
2. 저장소 위치: 로컬 컴퓨터 → 모든 인증서를 다음 저장소에 저장 → 신뢰할 수 있는 사용자.
3. 등록을 마친 후 MapleDay.msix를 열어 설치합니다.

또는 관리자 PowerShell에서 인증서만 등록:
Import-Certificate -FilePath "MapleYoil.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
그 뒤 사용자 PowerShell에서 설치:
Add-AppxPackage -Path "MapleDay.msix"

배포물에는 공개 인증서(.cer)만 포함됩니다. 개인키(.pfx)는 내보내거나 포함하지 않습니다.
빌드는 패키지 생성과 서명·내용 검증까지만 수행하며 설치하거나 앱을 실행하지 않습니다.
'@
[IO.File]::WriteAllText((Join-Path $packageDirectory 'INSTALL.txt'), $instructions, [Text.UTF8Encoding]::new($true))
Copy-Item -LiteralPath (Join-Path $projectDirectory 'packaging\STORE-SUBMISSION.md') -Destination $packageDirectory -Force
Write-Output "MSIX $version : $packagePath"
