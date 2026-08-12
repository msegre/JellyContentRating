<#
.SYNOPSIS
  Builds the plugin, packages it into a zip, computes its checksum, and
  writes repo/manifest.json so the 'repo' folder can be served as a local
  Jellyfin plugin repository.

.EXAMPLE
  # Serve from your own machine's LAN IP on port 8080
  .\Build-Repo.ps1 -SourceBaseUrl "http://192.168.20.28:8080"

  Then, from the repo\ folder: python -m http.server 8080
  Then, in Jellyfin: Dashboard > Plugins > Repositories > Add
      URL = http://192.168.20.28:8080/manifest.json
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceBaseUrl,

    [string]$Version = "1.0.0.0",
    [string]$TargetAbi = "10.11.0.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root "Jellyfin.Plugin.ContentRating"
$repoDir = Join-Path $root "repo"
$guid = "f3a1b2c4-7e21-4a5b-8e2d-9f0a1b2c3d4e"

New-Item -ItemType Directory -Force -Path $repoDir | Out-Null

Write-Host "Building (Release)..."
dotnet build $projectDir -c Release "/p:Version=$Version"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

$dll = Join-Path $projectDir "bin\Release\net9.0\Jellyfin.Plugin.ContentRating.dll"
if (-not (Test-Path $dll)) {
    throw "Build output not found at $dll -- check the target framework folder name matches (net9.0)."
}

$zipName = "ContentRating-$Version.zip"
$zipPath = Join-Path $repoDir $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath
}

# Jellyfin repo zips contain just the plugin DLL(s) at the root -- no meta.json,
# no subfolder. meta.json is only needed for the "drop straight into plugins/"
# manual-install path; the catalog/repository path generates its own metadata
# from the manifest entry.
Compress-Archive -Path $dll -DestinationPath $zipPath

$md5 = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash.ToLower()
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$sourceUrl = "$($SourceBaseUrl.TrimEnd('/'))/$zipName"

$manifest = @(
    [ordered]@{
        guid        = $guid
        name        = "Content Rating"
        description = "Tag movies as Kid, Teen, or All and filter using Jellyfin's native Allowed Tags parental controls."
        overview    = "Simple age-based content tagging"
        owner       = "you"
        category    = "General"
        versions    = @(
            [ordered]@{
                version   = $Version
                changelog = "Initial release"
                targetAbi = $TargetAbi
                sourceUrl = $sourceUrl
                checksum  = $md5
                timestamp = $timestamp
            }
        )
    }
)

$manifestPath = Join-Path $repoDir "manifest.json"

# ConvertTo-Json silently unwraps a single-element array into a bare {...}
# object instead of [{...}] unless told otherwise (the -AsArray switch that
# fixes this isn't available on Windows PowerShell 5.1). Jellyfin requires a
# top-level JSON array here -- a bare object fails with a JsonException like
# "could not be converted to MediaBrowser.Model.Updates.PackageInfo[]". Guard
# against it explicitly so this works on any PowerShell version.
$json = $manifest | ConvertTo-Json -Depth 6
if ($json.TrimStart()[0] -ne '[') {
    $json = "[$json]"
}

# Also write plain UTF-8 without a BOM explicitly, since Set-Content -Encoding
# utf8 on Windows PowerShell 5.1 adds a BOM (PowerShell 7's utf8 alias does
# not), which is one more thing that shouldn't need to depend on which
# PowerShell version is running the script.
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Done."
Write-Host "  Zip:      $zipPath"
Write-Host "  Manifest: $manifestPath"
Write-Host "  Checksum: $md5"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. cd `"$repoDir`""
Write-Host "  2. python -m http.server 8080   (or any static file server)"
Write-Host "  3. In Jellyfin: Dashboard > Plugins > Repositories > Add"
Write-Host "     URL: $SourceBaseUrl/manifest.json"
Write-Host "  4. Dashboard > Plugins > Catalog > find 'Content Rating' > Install"
Write-Host "     (this replaces your manually-copied install and should also fix"
Write-Host "      the 'error getting plugin details from repository' message)"
