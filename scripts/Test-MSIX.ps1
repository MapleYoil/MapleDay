param(
    [string]$PackagePath = (Join-Path $PSScriptRoot '..\artifacts\MSIX\MapleDay.msix'),
    [string]$CertificatePath = (Join-Path $PSScriptRoot '..\artifacts\MSIX\MapleYoil.cer')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$verify = Join-Path $PSScriptRoot 'Verify-MSIX.ps1'
& $verify -PackagePath $PackagePath -CertificatePath $CertificatePath
$fixtureDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\build\msix'))
$fixturePath = Join-Path $fixtureDirectory 'tamper-test.msix'
# No package is installed and no executable or Windows UI is launched.
foreach ($name in @('AppxManifest.xml', 'AppxBlockMap.xml', 'AppxSignature.p7x')) {
    try {
        Copy-Item -LiteralPath $PackagePath -Destination $fixturePath -Force
        $archive = [IO.Compression.ZipFile]::Open($fixturePath, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.GetEntry($name)
            $stream = $entry.Open(); $memory = [IO.MemoryStream]::new()
            try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
            finally { $stream.Dispose(); $memory.Dispose() }
            $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
            $entry.Delete()
            $replacement = $archive.CreateEntry($name)
            $stream = $replacement.Open()
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        } finally { $archive.Dispose() }
        $rejected = $false
        try { & $verify -PackagePath $fixturePath -CertificatePath $CertificatePath | Out-Null }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Tampered $name was accepted." }
        Write-Output "Tampering rejected: $name"
    } finally {
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath -Force }
    }
}
