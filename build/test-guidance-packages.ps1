[CmdletBinding()]
param(
    [string] $PackagesDirectory = (Join-Path (Join-Path $PSScriptRoot '..') 'artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$references = [ordered]@{
    'Trellis.Microservices.Abstractions' = [ordered]@{
        'trellis-api-microservices-abstractions.md' = [ordered]@{
            Usage = 'onDemand'
            Description = 'Open when implementing or changing the Trellis internal JWT claim contract shared by gateways and downstream services.'
        }
        'trellis-api-microservices-cookbook.md' = [ordered]@{
            Usage = 'onDemand'
            Description = 'Open when configuring the end-to-end Trellis internal JWT flow, strict bearer validation, tenant isolation, or signing-key rotation.'
        }
    }
    'Trellis.Yarp' = [ordered]@{
        'trellis-api-yarp.md' = [ordered]@{
            Usage = 'onDemand'
            Description = 'Open when configuring or changing Trellis YARP actor forwarding, JWT minting, discovery, JWKS publication, or signing-key rotation.'
        }
    }
    'Trellis.Microservices.AspNetCore' = [ordered]@{
        'trellis-api-internal-jwt.md' = [ordered]@{
            Usage = 'onDemand'
            Description = 'Open when configuring or changing downstream Trellis internal JWT validation, actor hydration, required attributes, or claim-shape enforcement.'
        }
    }
}

$documentReferences = [ordered]@{
    'Trellis.Microservices.Abstractions' = [ordered]@{
        'trellis-api-yarp.md' = 'Trellis.Yarp'
        'trellis-api-internal-jwt.md' = 'Trellis.Microservices.AspNetCore'
        'upstream/trellis-api-core.md' = 'Trellis.Core'
        'upstream/trellis-api-authorization.md' = 'Trellis.Core'
        'upstream/trellis-api-asp.md' = 'Trellis.Core'
        'upstream/trellis-api-servicedefaults.md' = 'Trellis.Core'
        'upstream/trellis-api-cookbook.md' = 'Trellis.Core'
    }
    'Trellis.Yarp' = [ordered]@{
        'trellis-api-microservices-abstractions.md' = 'Trellis.Microservices.Abstractions'
        'trellis-api-internal-jwt.md' = 'Trellis.Microservices.AspNetCore'
        'trellis-api-microservices-cookbook.md' = 'Trellis.Microservices.Abstractions'
        'upstream/trellis-api-authorization.md' = 'Trellis.Core'
        'upstream/trellis-api-asp.md' = 'Trellis.Core'
    }
    'Trellis.Microservices.AspNetCore' = [ordered]@{
        'trellis-api-microservices-abstractions.md' = 'Trellis.Microservices.Abstractions'
        'trellis-api-yarp.md' = 'Trellis.Yarp'
        'trellis-api-microservices-cookbook.md' = 'Trellis.Microservices.Abstractions'
        'upstream/trellis-api-authorization.md' = 'Trellis.Core'
        'upstream/trellis-api-asp.md' = 'Trellis.Core'
    }
}
$packageVersions = @{}
$documentHashes = @{}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$props = [xml] (Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Packages.props') -Raw)
$toolNode = $props.SelectSingleNode('//PackageVersion[@Include="Trellis.AgentDocs.Packaging"]')
if (-not $toolNode) {
    throw 'Directory.Packages.props must pin Trellis.AgentDocs.Packaging.'
}

$toolVersion = $toolNode.GetAttribute('Version')
$toolDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "agentdocs-validate-$([guid]::NewGuid().ToString('N'))"
try {
    $install = & dotnet tool install Trellis.AgentDocs --version $toolVersion --tool-path $toolDirectory 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install Trellis.AgentDocs $toolVersion for validation:`n$($install | Out-String)"
    }

    foreach ($packageId in $references.Keys) {
        $packages = @(
            Get-ChildItem -LiteralPath $PackagesDirectory -Filter '*.nupkg' |
                Where-Object {
                    $_.Name -match "^$([regex]::Escape($packageId))\.\d" -and
                    $_.Name -notlike '*.symbols.nupkg'
                }
        )
        if ($packages.Count -ne 1) {
            throw "Expected one packed $packageId in $PackagesDirectory, found $($packages.Count)."
        }

        $archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
        try {
            $expectedDocuments = $references[$packageId]
            $manifestEntry = $archive.GetEntry('guidance/reference-manifest.json')
            if (-not $manifestEntry) {
                throw "$packageId must pack guidance/reference-manifest.json."
            }

            $manifestReader = [System.IO.StreamReader]::new($manifestEntry.Open())
            try {
                $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json
            }
            finally {
                $manifestReader.Dispose()
            }

            $documents = @($manifest.documents)
            if ($manifest.schemaVersion -ne 1 -or $documents.Count -ne $expectedDocuments.Count) {
                throw "$packageId manifest must use schemaVersion 1 and declare $($expectedDocuments.Count) document(s)."
            }

            $declaredReferences = $manifest.PSObject.Properties['documentReferences']
            $expectedReferences = $documentReferences[$packageId]
            if (-not $declaredReferences -or @($declaredReferences.Value).Count -ne $expectedReferences.Count) {
                throw "$packageId must declare $($expectedReferences.Count) cross-package document references."
            }
            foreach ($referencePath in $expectedReferences.Keys) {
                $matches = @($declaredReferences.Value | Where-Object path -CEQ $referencePath)
                $targetPackage = $expectedReferences[$referencePath]
                $targetPath = if ($targetPackage -ceq 'Trellis.Core') { "trellis/$([System.IO.Path]::GetFileName($referencePath))" } else { $referencePath }
                if ($matches.Count -ne 1 -or $matches[0].packageId -cne $targetPackage -or
                    $matches[0].documentPath -cne $targetPath -or $archive.GetEntry($referencePath)) {
                    throw "$packageId must reference $targetPackage/$targetPath without packing a duplicate at $referencePath."
                }
            }

            $documentHashes[$packageId] = @{}
            foreach ($documentPath in $expectedDocuments.Keys) {
                $matches = @($documents | Where-Object { $_.path -ceq $documentPath })
                if ($matches.Count -ne 1) {
                    throw "$packageId manifest must declare $documentPath exactly once."
                }

                $document = $matches[0]
                $expectedDocument = $expectedDocuments[$documentPath]
                $descriptionProperty = $document.PSObject.Properties['description']
                $description = if ($descriptionProperty) { [string] $descriptionProperty.Value } else { $null }
                if ($document.usage -cne $expectedDocument['Usage'] -or
                    $description -cne $expectedDocument['Description']) {
                    throw "$packageId manifest metadata for $documentPath is incorrect."
                }

                $documentEntry = $archive.GetEntry($documentPath)
                if (-not $documentEntry) {
                    throw "$packageId must pack $documentPath."
                }

                $documentBytes = [System.IO.MemoryStream]::new()
                $documentStream = $documentEntry.Open()
                try {
                    $documentStream.CopyTo($documentBytes)
                    $hash = [Convert]::ToHexString(
                        [System.Security.Cryptography.SHA256]::HashData($documentBytes.ToArray())
                    ).ToLowerInvariant()
                }
                finally {
                    $documentStream.Dispose()
                    $documentBytes.Dispose()
                }

                if ($document.sha256 -cne $hash) {
                    throw "$packageId manifest hash does not match $documentPath."
                }
                $documentHashes[$packageId][$documentPath] = $hash
            }

            $packedGuides = @(
                $archive.Entries |
                    Where-Object { $_.FullName -match '^trellis-api-[^/]+\.md$' }
            )
            if ($packedGuides.Count -ne $expectedDocuments.Count) {
                throw "$packageId must pack only its declared root-level guidance documents."
            }

            $legacyEntries = @(
                $archive.Entries |
                    Where-Object {
                        $_.FullName.StartsWith('trellis/', [StringComparison]::OrdinalIgnoreCase) -or
                        $_.FullName -match '^(build|buildTransitive)/'
                    }
            )
            if ($legacyEntries.Count -ne 0) {
                throw "$packageId must not ship legacy trellis/ documents or consumer build targets."
            }

            $nuspecEntry = $archive.GetEntry("$packageId.nuspec")
            if (-not $nuspecEntry) {
                throw "$packageId is missing its package metadata."
            }

            $nuspecReader = [System.IO.StreamReader]::new($nuspecEntry.Open())
            try {
                [xml] $nuspec = $nuspecReader.ReadToEnd()
            }
            finally {
                $nuspecReader.Dispose()
            }

            if ($nuspec.SelectSingleNode(
                '//*[local-name()="dependency" and @id="Trellis.AgentDocs.Packaging"]'
            )) {
                throw "$packageId leaks the publisher-only Trellis.AgentDocs.Packaging dependency."
            }
            $packageVersions[$packageId] = $nuspec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="version"]').InnerText

            $readmeNode = $nuspec.SelectSingleNode('//*[local-name()="readme"]')
            if (-not $readmeNode -or [string]::IsNullOrWhiteSpace($readmeNode.InnerText)) {
                throw "$packageId does not declare a NuGet README."
            }

            $readmeEntry = $archive.GetEntry($readmeNode.InnerText)
            if (-not $readmeEntry) {
                throw "$packageId is missing its declared NuGet README $($readmeNode.InnerText)."
            }

            $readmeReader = [System.IO.StreamReader]::new($readmeEntry.Open())
            try {
                $readme = $readmeReader.ReadToEnd()
            }
            finally {
                $readmeReader.Dispose()
            }

            $installCommand = "dotnet tool install Trellis.AgentDocs --version $toolVersion --tool-manifest .config/dotnet-tools.json"
            if (-not $readme.Contains($installCommand) -or
                -not $readme.Contains('dotnet tool run agentdocs init <solution-or-project>') -or
                -not $readme.Contains('approvedPackages') -or
                -not $readme.Contains($packageId) -or
                -not $readme.Contains('dotnet tool run agentdocs sync')) {
                throw "$packageId NuGet README must document AgentDocs installation, approval, and synchronization."
            }

            $validation = & (Join-Path $toolDirectory 'agentdocs') validate $packages[0].FullName --strict 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "agentdocs validate --strict rejected ${packageId}:`n$($validation | Out-String)"
            }

            Write-Host "PASS $($packages[0].Name) ships validated AgentDocs guidance without consumer targets"
        }
        finally {
            $archive.Dispose()
        }
    }

    $consumer = Join-Path $toolDirectory 'consumer'
    $cache = Join-Path $toolDirectory 'packages'
    $utf8Bom = [System.Text.UTF8Encoding]::new($true)
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $consumer '.git'))
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $consumer '.config'))
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $consumer '.github'))
    $instructions = Join-Path $consumer '.github\copilot-instructions.md'
    [System.IO.File]::WriteAllText($instructions, 'Consumer-owned instructions.', $utf8Bom)
    $toolManifest = @{
        version = 1
        isRoot = $true
        tools = @{
            'trellis.agentdocs' = @{ version = $toolVersion; commands = @('agentdocs') }
        }
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $consumer '.config\dotnet-tools.json'), $toolManifest, $utf8Bom)
    $feed = [System.Security.SecurityElement]::Escape((Resolve-Path $PackagesDirectory).Path)
    $config = @"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="Trellis.Microservices.Abstractions" />
      <package pattern="Trellis.Yarp" />
      <package pattern="Trellis.Microservices.AspNetCore" />
    </packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@
    [System.IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), $config, $utf8Bom)
    $projects = [ordered]@{ Gateway = 'Trellis.Yarp'; Service = 'Trellis.Microservices.AspNetCore' }
    foreach ($projectName in $projects.Keys) {
        $directory = Join-Path $consumer $projectName
        [void] [System.IO.Directory]::CreateDirectory($directory)
        $packageId = $projects[$projectName]
        $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="$packageId" Version="$($packageVersions[$packageId])" /></ItemGroup>
</Project>
"@
        [System.IO.File]::WriteAllText((Join-Path $directory "$projectName.csproj"), $project, $utf8Bom)
    }
    $solution = Join-Path $consumer 'Consumer.slnx'
    [System.IO.File]::WriteAllText($solution,
        '<Solution><Project Path="Gateway/Gateway.csproj" /><Project Path="Service/Service.csproj" /></Solution>', $utf8Bom)
    $restore = & dotnet restore $solution --packages $cache --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Consumer restore failed:`n$($restore | Out-String)"
    }
    $build = & dotnet build $solution -c Release --no-restore --nologo --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Consumer build failed:`n$($build | Out-String)"
    }
    if ([System.IO.File]::ReadAllText($instructions) -cne 'Consumer-owned instructions.' -or
        (Test-Path -LiteralPath (Join-Path $consumer '.agentdocs')) -or
        @(Get-ChildItem -LiteralPath (Join-Path $consumer '.github') -File).Count -ne 1) {
        throw 'Normal consumer restore/build must not install or modify agent instructions.'
    }

    $assets = Get-Content -LiteralPath (Join-Path $consumer 'Gateway\obj\project.assets.json') -Raw | ConvertFrom-Json
    $coreLibraries = @($assets.libraries.PSObject.Properties | Where-Object Name -Like 'Trellis.Core/*')
    if ($coreLibraries.Count -ne 1) {
        throw 'Consumer must restore exactly one Trellis.Core version.'
    }
    $packageVersions['Trellis.Core'] = $coreLibraries[0].Name.Split('/')[1]
    $coreDirectory = Join-Path $cache "trellis.core\$($packageVersions['Trellis.Core'])"
    $coreManifest = Get-Content -LiteralPath (Join-Path $coreDirectory 'guidance\reference-manifest.json') -Raw | ConvertFrom-Json
    if ($coreManifest.schemaVersion -ne 1) {
        throw 'Restored Trellis.Core must publish schema-v1 guidance.'
    }
    $documentHashes['Trellis.Core'] = @{}
    foreach ($document in $coreManifest.documents) {
        $path = Join-Path $coreDirectory $document.path
        $hash = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($path))
        ).ToLowerInvariant()
        if ($document.sha256 -cne $hash) {
            throw "Restored Trellis.Core manifest hash does not match $($document.path)."
        }
        $documentHashes['Trellis.Core'][$document.path] = $hash
    }

    function Invoke-AgentDocs {
        param([string[]] $Arguments)
        $output = & (Join-Path $toolDirectory 'agentdocs') @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "agentdocs $($Arguments -join ' ') failed:`n$($output | Out-String)"
        }
    }

    Push-Location $consumer
    try {
        Invoke-AgentDocs -Arguments @('init', 'Consumer.slnx')
        $policyPath = Join-Path $consumer '.agentdocs\policy.json'
        $policy = @{ schemaVersion = 1; approvedPackages = @('Trellis.Microservices.Abstractions') } | ConvertTo-Json
        [System.IO.File]::WriteAllText($policyPath, $policy, $utf8Bom)
        Invoke-AgentDocs -Arguments @('sync')
        foreach ($referencePath in $documentReferences['Trellis.Microservices.Abstractions'].Keys) {
            $notice = Join-Path $consumer ".agentdocs\packages\trellis.microservices.abstractions\$referencePath"
            if (-not (Test-Path -LiteralPath $notice) -or
                -not [System.IO.File]::ReadAllText($notice).Contains('not-approved')) {
                throw "Unapproved reference $referencePath must install an unavailable notice."
            }
        }
        foreach ($packageId in @('Trellis.Yarp', 'Trellis.Microservices.AspNetCore', 'Trellis.Core')) {
            if (Test-Path -LiteralPath (Join-Path $consumer ".agentdocs\packages\$($packageId.ToLowerInvariant())")) {
                throw "References must not install unapproved $packageId guidance."
            }
        }

        $policy = @{
            schemaVersion = 1
            approvedPackages = @($references.Keys) + @('Trellis.Core')
        } | ConvertTo-Json
        [System.IO.File]::WriteAllText($policyPath, $policy, $utf8Bom)
        Invoke-AgentDocs -Arguments @('sync', '--strict-references')
        foreach ($packageId in $references.Keys) {
            foreach ($documentPath in $references[$packageId].Keys) {
                $path = Join-Path $consumer ".agentdocs\packages\$($packageId.ToLowerInvariant())\$documentPath"
                $text = [System.IO.File]::ReadAllText($path)
                if ($text.Contains('https://github.com/xavierjohn/Trellis/blob/main/docs/') -or
                    $text.Contains('https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/')) {
                    throw "$packageId/$documentPath must not link to moving GitHub main API references."
                }
                foreach ($referencePath in $documentReferences[$packageId].Keys) {
                    if ($packageId -eq 'Trellis.Microservices.Abstractions' -and
                        $documentPath -eq 'trellis-api-microservices-abstractions.md' -and
                        $documentReferences[$packageId][$referencePath] -eq 'Trellis.Core') {
                        continue
                    }
                    $targetPackage = $documentReferences[$packageId][$referencePath]
                    $targetPath = if ($targetPackage -ceq 'Trellis.Core') { "trellis/$([System.IO.Path]::GetFileName($referencePath))" } else { $referencePath }
                    if (-not $text.Contains("../$($targetPackage.ToLowerInvariant())/$targetPath")) {
                        throw "$packageId/$documentPath must rewrite its reference to $targetPackage/$targetPath."
                    }
                }
            }
            foreach ($referencePath in $documentReferences[$packageId].Keys) {
                if (Test-Path -LiteralPath (Join-Path $consumer ".agentdocs\packages\$($packageId.ToLowerInvariant())\$referencePath")) {
                    throw "Resolved reference $packageId/$referencePath must not leave a duplicate or notice."
                }
            }
        }
        $state = Get-Content -LiteralPath (Join-Path $consumer '.agentdocs\agent-context.json') -Raw | ConvertFrom-Json
        if ($state.SchemaVersion -ne 1) {
            throw 'Consumer context must use schemaVersion 1.'
        }
        $microservicesFiles = @($state.References | Where-Object {
            $_.Sources[0].Package -in $references.Keys
        })
        if ($microservicesFiles.Count -ne 4) {
            throw 'Consumer must install exactly four package-owned Microservices guidance documents.'
        }
        foreach ($file in $microservicesFiles) {
            if ($file.TransformVersion -cne 'document-references-v1' -or @($file.ReferenceDependencies).Count -eq 0) {
                throw "Consumer must record the reference transform for $($file.Path)."
            }
            foreach ($dependency in $file.ReferenceDependencies) {
                if ($dependency.Outcome -cne 'resolved') {
                    throw "Reference $($file.Path)/$($dependency.ReferencePath) must be resolved."
                }
                if ($dependency.TargetPackage -in $packageVersions.Keys -and
                    ($dependency.TargetVersion -cne $packageVersions[$dependency.TargetPackage] -or
                     $dependency.TargetSha256 -cne $documentHashes[$dependency.TargetPackage][$dependency.TargetDocumentPath])) {
                    throw "Reference $($file.Path)/$($dependency.ReferencePath) must record the packed target version and hash."
                }
            }
        }
        $snapshot = @{}
        Get-ChildItem -LiteralPath (Join-Path $consumer '.agentdocs') -File -Recurse | ForEach-Object {
            $snapshot[$_.FullName] = [Convert]::ToHexString(
                [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($_.FullName)))
        }
        Invoke-AgentDocs -Arguments @('sync', '--strict-references')
        Invoke-AgentDocs -Arguments @('check', '--strict', '--strict-references')
        $files = @(Get-ChildItem -LiteralPath (Join-Path $consumer '.agentdocs') -File -Recurse)
        if ($files.Count -ne $snapshot.Count) {
            throw 'Repeated sync must not add or remove guidance files.'
        }
        foreach ($file in $files) {
            $hash = [Convert]::ToHexString(
                [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($file.FullName)))
            if (-not $snapshot.ContainsKey($file.FullName) -or $hash -cne $snapshot[$file.FullName]) {
                throw "Repeated sync must preserve $($file.FullName)."
            }
        }
        Write-Host 'PASS cross-project and upstream references, approval isolation, no duplicate guides, and repeated sync'
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $toolDirectory) {
        Remove-Item -LiteralPath $toolDirectory -Recurse -Force
    }
}
