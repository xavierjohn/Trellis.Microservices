#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies that packed NuGet content lands at clean, well-formed paths.

.DESCRIPTION
    PackagePath separators are platform-dependent in a way that is easy to miss. On Windows a
    trailing backslash in PackagePath="trellis\" is recognized as a directory marker and the doc
    lands at trellis/<name>.md. On Linux the backslash is not a separator: it normalizes to
    "trellis/" and NuGet then appends its own, so the doc lands at the malformed
    "trellis//<name>.md".

    That malformed path still satisfies a trellis/*.md glob, so the copy logic still delivers the
    docs and every functional test stays green. The defect is invisible unless something asserts
    the exact entry path - which is why it reached published packages unnoticed.

    Two checks, because neither alone is sufficient:

      1. Declarations - no PackagePath contains a backslash. Platform-independent, so it holds
         the line on a developer's Windows machine where check 2 cannot fail.
      2. Packed entries - every entry is a clean relative path. This is the real behaviour, but
         it can only go red on Linux.

.PARAMETER ArtifactsPath
    Directory containing .nupkg files to inspect. Skipped when absent or empty.
#>
[CmdletBinding()]
param(
    [string] $ArtifactsPath = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$failures = @()

function Assert-Condition {
    param([string] $Because, [bool] $Condition, [string[]] $Detail = @())

    if ($Condition) {
        Write-Host "  PASS  $Because"
        return
    }

    Write-Host "  FAIL  $Because" -ForegroundColor Red
    foreach ($line in $Detail) { Write-Host "          $line" -ForegroundColor Red }
    $script:failures += $Because
}

Write-Host 'Check 1 - PackagePath declarations use forward slashes'

# Parse the XML rather than scanning text. A line-based regex cannot see the single-quoted
# attribute form (PackagePath='trellis\') or the <PackagePath>trellis\</PackagePath> metadata
# element, and it would flag the comments in these files that quote the malformed value on
# purpose. XDocument handles all three correctly and gives line numbers via IXmlLineInfo.
#
# Property indirection - PackagePath="$(SomeVar)" where SomeVar holds a backslash - is not
# statically visible here; check 2 against the packed bytes is what covers that case.
$offenders = @(
    Get-ChildItem -Path $repoRoot -Recurse -File -Include '*.csproj', '*.props', '*.targets' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' } |
        ForEach-Object {
            $file = $_
            $document = [System.Xml.Linq.XDocument]::Load($file.FullName, [System.Xml.Linq.LoadOptions]::SetLineInfo)
            $relative = $file.FullName.Substring($repoRoot.Length + 1)

            $nodes = @()
            $nodes += @($document.Descendants() | Where-Object { $_.Name.LocalName -eq 'PackagePath' })
            $nodes += @($document.Descendants().Attributes() | Where-Object { $_.Name.LocalName -eq 'PackagePath' })

            foreach ($node in $nodes) {
                if ($node.Value -like '*\*') {
                    "${relative}:$(([System.Xml.IXmlLineInfo]$node).LineNumber) -> PackagePath=$($node.Value)"
                }
            }
        }
)
Assert-Condition -Because 'no PackagePath contains a backslash' `
    -Condition ($offenders.Count -eq 0) -Detail $offenders

Write-Host "`nCheck 2 - packed entries are clean relative paths"

$packages = @()
if (Test-Path $ArtifactsPath) {
    $packages = @(Get-ChildItem -Path $ArtifactsPath -Filter '*.nupkg' -File)
}

if ($packages.Count -eq 0) {
    Write-Host "  SKIP  no .nupkg found under '$ArtifactsPath' (run dotnet pack first)"
}
else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $totalDocs = 0

    foreach ($package in $packages) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        }
        finally {
            $archive.Dispose()
        }

        $malformed = @($entries | Where-Object { $_ -match '//' -or $_ -match '\\' -or $_.StartsWith('/') })
        Assert-Condition -Because "$($package.Name) has no malformed entry paths" `
            -Condition ($malformed.Count -eq 0) -Detail $malformed

        # Not every package ships a reference, so an empty set here is legitimate and is NOT an
        # error. The vacuous case - nothing anywhere ships one - is caught once, after the loop.
        $docs = @($entries | Where-Object { $_ -like '*trellis-api-*.md' })
        $totalDocs += $docs.Count

        $misplaced = @($docs | Where-Object { $_ -notmatch '^trellis/[^/]+\.md$' })
        Assert-Condition -Because "$($package.Name) ships its references at trellis/<name>.md" `
            -Condition ($misplaced.Count -eq 0) -Detail $misplaced
    }

    # Without this, a repo-wide break in doc packaging would leave every per-package placement
    # assertion trivially true and the whole check would report green while shipping nothing.
    Assert-Condition -Because 'at least one package ships an API reference' -Condition ($totalDocs -gt 0)
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "Package layout check FAILED ($($failures.Count) assertion(s))." -ForegroundColor Red
    exit 1
}

Write-Host 'Package layout check passed.' -ForegroundColor Green
exit 0
