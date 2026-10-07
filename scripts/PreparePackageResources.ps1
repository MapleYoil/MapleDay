param(
    [Parameter(Mandatory = $true)][string]$SourcePri,
    [Parameter(Mandatory = $true)][string]$OutputPri,
    [Parameter(Mandatory = $true)][string]$SdkBin,
    [Parameter(Mandatory = $true)][string]$IndexName
)
$ErrorActionPreference = 'Stop'
$makePri = Join-Path $SdkBin 'makepri.exe'
$scratch = Split-Path -Parent $OutputPri
$listPath = Join-Path $scratch 'package-input.resfiles'
$configPath = Join-Path $scratch 'package-priconfig.xml'
$logPath = Join-Path $scratch 'makepri.log'
[IO.File]::WriteAllText($listPath, [IO.Path]::GetFullPath($SourcePri))
$config = '<resources targetOsVersion="10.0.0" majorVersion="1"><index root="\" startIndexAt="package-input.resfiles"><default><qualifier name="Language" value="en-US"/><qualifier name="Scale" value="200"/></default><indexer-config type="PRI"/><indexer-config type="RESFILES" qualifierDelimiter="."/></index></resources>'
[IO.File]::WriteAllText($configPath, $config)
& $makePri new /pr $scratch /cf $configPath /in $IndexName /of $OutputPri /o *> $logPath
if ($LASTEXITCODE -ne 0) { throw "Package resource indexing failed. See $logPath." }

# Detailed dumps include embedded XBF bytes. Verify every candidate and qualifier survived merging.
$dumps = @()
foreach ($item in @(@($SourcePri, 'portable-resources.xml'), @($OutputPri, 'store-resources.xml'))) {
    $dumpPath = Join-Path $scratch $item[1]
    & $makePri dump /if $item[0] /dt detailed /of $dumpPath /o *> $logPath
    if ($LASTEXITCODE -ne 0) { throw "Package resource inspection failed. See $logPath." }
    $dumps += ,([xml][IO.File]::ReadAllText($dumpPath))
}
if ($dumps[1].PriInfo.ResourceMap.name -cne $IndexName) { throw 'Package resource map does not match the Store identity.' }
$original = $dumps[0].SelectNodes('//NamedResource')
$merged = $dumps[1].SelectNodes('//NamedResource')
if ($original.Count -ne $merged.Count) { throw 'Package resources were lost during merging.' }
$byPath = @{}
foreach ($node in $merged) {
    $path = ([uri]$node.uri).AbsolutePath
    $byPath[$path] = $node
}
foreach ($node in $original) {
    $path = ([uri]$node.uri).AbsolutePath
    $copy = $byPath[$path]
    # Numeric internal indexes can change; names, qualifier values and binary data must not.
    $before = $node.InnerXml -replace ' index="\d+"', ''
    $after = if ($copy) { $copy.InnerXml -replace ' index="\d+"', '' } else { '' }
    if ($before -cne $after) { throw "Package resource candidate changed: $path" }
}
Write-Output "Store resources verified: $IndexName, $($merged.Count) resources (including embedded XAML)."
