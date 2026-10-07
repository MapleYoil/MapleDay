param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$CertificatePath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Security
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
$entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $archive.Entries) {
    $entryName = $entry.FullName.Replace('\', '/')
    if ($entries.ContainsKey($entryName)) { $archive.Dispose(); throw "Duplicate MSIX file path: $entryName" }
    $entries.Add($entryName, $entry)
}
$sha = [Security.Cryptography.SHA256]::Create()
function Read-EntryBytes([string]$name) {
    $entry = $entries[$name]
    if (-not $entry) { throw "MSIX entry missing: $name" }
    $stream = $entry.Open(); $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}
try {
    $signatureBytes = Read-EntryBytes 'AppxSignature.p7x'
    if ([Text.Encoding]::ASCII.GetString($signatureBytes, 0, 4) -ne 'PKCX') { throw 'Invalid MSIX signature header.' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$signatureBytes[4..($signatureBytes.Length - 1)])
    # Cryptographic verification only; installing trust is a separate user action.
    $cms.CheckSignature($true)
    $expectedCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.Path]::GetFullPath($CertificatePath))
    if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $expectedCertificate.Thumbprint) { throw 'MSIX signer does not match the supplied public certificate.' }
    $blockBytes = Read-EntryBytes 'AppxBlockMap.xml'
    $blockDigest = $sha.ComputeHash($blockBytes)
    $signedContent = $cms.ContentInfo.Content
    $blockDigestInSignature = $false
    for ($index = 0; $index -le $signedContent.Length - 36; $index++) {
        if ([Text.Encoding]::ASCII.GetString($signedContent, $index, 4) -eq 'AXBM') {
            $signedDigest = [byte[]]$signedContent[($index + 4)..($index + 35)]
            $blockDigestInSignature = [Convert]::ToBase64String($signedDigest) -eq [Convert]::ToBase64String($blockDigest)
            break
        }
    }
    if (-not $blockDigestInSignature) { throw 'The block map hash is not bound to the MSIX signature.' }
    [xml]$blockMap = [Text.Encoding]::UTF8.GetString($blockBytes).TrimStart([char]0xFEFF)
    if ($blockMap.BlockMap.HashMethod -ne 'http://www.w3.org/2001/04/xmlenc#sha256') { throw 'Unexpected MSIX block hash algorithm.' }
    $fileCount = 0; $blockCount = 0
    foreach ($file in $blockMap.BlockMap.File) {
        $entry = $entries[$file.Name.Replace('\', '/')]
        if (-not $entry -or $entry.Length -ne [long]$file.Size) { throw "MSIX file size mismatch: $($file.Name)" }
        $stream = $entry.Open()
        try {
            foreach ($block in $file.Block) {
                $buffer = [byte[]]::new(65536); $count = 0
                while ($count -lt $buffer.Length) {
                    $read = $stream.Read($buffer, $count, $buffer.Length - $count)
                    if ($read -eq 0) { break }
                    $count += $read
                }
                $hash = [Convert]::ToBase64String($sha.ComputeHash($buffer, 0, $count))
                if ($hash -ne $block.Hash) { throw "MSIX block digest mismatch: $($file.Name)" }
                $blockCount++
            }
            if ($stream.ReadByte() -ne -1) { throw "MSIX has unmapped bytes: $($file.Name)" }
        } finally { $stream.Dispose() }
        $fileCount++
    }
    [xml]$manifest = [Text.Encoding]::UTF8.GetString((Read-EntryBytes 'AppxManifest.xml')).TrimStart([char]0xFEFF)
    if ($manifest.Package.Identity.Publisher -ne $expectedCertificate.Subject) { throw 'MSIX publisher does not match the signer.' }
    [xml]$storeContract = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\packaging\AppxManifest.xml'))
    if ($manifest.Package.Identity.Name -cne $storeContract.Package.Identity.Name -or
        $manifest.Package.Identity.Publisher -cne $storeContract.Package.Identity.Publisher -or
        $manifest.Package.Properties.PublisherDisplayName -cne $storeContract.Package.Properties.PublisherDisplayName -or
        $manifest.Package.Properties.DisplayName -cne $storeContract.Package.Properties.DisplayName) {
        throw 'MSIX identity does not match the assigned Partner Center identity.'
    }
    if (([version]$manifest.Package.Identity.Version).Revision -ne 0) { throw 'Microsoft Store reserves the fourth version component; it must be zero.' }
    $namespaces = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaces.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
    $namespaces.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
    $namespaces.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
    $namespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $displayName = [string]$storeContract.Package.Properties.DisplayName
    foreach ($element in $manifest.SelectNodes('//uap:VisualElements | //uap5:StartupTask | //com:ExeServer', $namespaces)) {
        if ($element.DisplayName -cne $displayName) { throw 'MSIX display name does not match the reserved Store app name.' }
    }
    $startup = $manifest.SelectSingleNode('//uap5:StartupTask', $namespaces)
    if ($startup.TaskId -ne 'MapleDayStartup' -or $startup.Enabled -ne 'true') { throw 'MSIX startup task contract is missing.' }
    $toast = $manifest.SelectSingleNode('//desktop:ToastNotificationActivation', $namespaces)
    $server = $manifest.SelectSingleNode('//com:ExeServer', $namespaces)
    if (-not $toast -or $server.Executable -ne 'MapleDay.exe' -or $server.Arguments -ne '----AppNotificationActivated:' -or
        $server.SelectSingleNode('com:Class', $namespaces).Id -ne $toast.ToastActivatorCLSID) { throw 'MSIX notification activation contract is missing.' }
    foreach ($required in @('MapleDay.exe', 'MapleDay.dll', 'MapleDay.pri', 'resources.pri', 'coreclr.dll', 'Microsoft.UI.Xaml.dll', 'Assets/Branding/logo.png')) {
        if (-not $entries.ContainsKey($required)) { throw "MSIX runtime file missing: $required" }
    }
    $deps = [Text.Encoding]::UTF8.GetString((Read-EntryBytes 'MapleDay.deps.json')) | ConvertFrom-Json
    $runtimeTarget = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
    $runtimePack = @($runtimeTarget.PSObject.Properties | Where-Object Name -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*')
    if ($runtimePack.Count -ne 1) { throw 'MSIX dependency manifest is missing the self-contained .NET runtime.' }
    $runtimeAssets = @($runtimePack[0].Value.runtime.PSObject.Properties.Name) + @($runtimePack[0].Value.native.PSObject.Properties.Name)
    foreach ($required in @('coreclr.dll', 'System.Private.CoreLib.dll')) {
        if ($runtimeAssets -notcontains $required) { throw "MSIX runtime dependency manifest is missing: $required" }
    }
    foreach ($asset in $runtimeAssets) {
        if (-not $entries.ContainsKey($asset)) { throw "MSIX runtime dependency file is missing: $asset" }
    }
    if ($archive.Entries | Where-Object { $_.FullName -match '(?i)\.(pfx|key)$' }) { throw 'Private key material must not be packaged.' }
    Write-Output "MSIX verified: publisher $($manifest.Package.Properties.PublisherDisplayName), version $($manifest.Package.Identity.Version), $fileCount files, $blockCount SHA-256 blocks, signature valid."
} finally { $sha.Dispose(); $archive.Dispose() }
