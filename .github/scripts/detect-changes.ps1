[CmdletBinding()]
param(
    [string]$BaseSha,
    [string]$HeadSha = "HEAD",
    [switch]$RunAll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BaseSha)) {
    $BaseSha = (git rev-parse "$HeadSha^" 2>$null).Trim()
}

if ([string]::IsNullOrWhiteSpace($BaseSha)) {
    throw "Unable to determine a base revision. Pass -BaseSha explicitly."
}

$changedFiles = @(git diff --name-only $BaseSha $HeadSha)

function Test-ChangedPath {
    param([string[]]$Patterns)

    foreach ($file in $changedFiles) {
        foreach ($pattern in $Patterns) {
            if ($file -like $pattern) {
                return $true
            }
        }
    }

    return $false
}

$backend = Test-ChangedPath @(
    "backend/product-catalog/**",
    "backend/notifications/**",
    "backend/report-scheduler/**",
    "backend/shared-kernel/**",
    "Directory.*",
    "global.json",
    "NuGet.Config"
)
$frontend = Test-ChangedPath @("frontend/**")
$integration = Test-ChangedPath @("tests/integration/**", "tests/common/**")
$e2e = Test-ChangedPath @("tests/e2e/**", "frontend/**")
$infrastructure = Test-ChangedPath @("Infrastructure/**", "aspire/**", "docker-compose.yml", "docker-compose.*")
$security = Test-ChangedPath @(".github/**", "Directory.*", "global.json", "NuGet.Config", "**/*.csproj", "**/packages.lock.json", "**/package-lock.json", "**/Dockerfile")
$fullValidation = $RunAll -or $infrastructure -or $security

# Shared infrastructure and central configuration affect every executable path.
if ($fullValidation) {
    $backend = $true
    $frontend = $true
    $integration = $true
    $e2e = $true
}

$documentationOnly = $changedFiles.Count -gt 0 -and -not ($backend -or $frontend -or $integration -or $e2e -or $infrastructure -or $security)

$outputs = [ordered]@{
    backend = [bool]$backend
    frontend = [bool]$frontend
    integration = [bool]$integration
    e2e = [bool]$e2e
    infrastructure = [bool]$infrastructure
    security = [bool]$security
    full_validation = [bool]$fullValidation
    documentation_only = [bool]$documentationOnly
}

$summary = @(
    "## Change plan",
    "",
    "Base: ``$BaseSha``  ",
    "Head: ``$HeadSha``  ",
    "",
    "| Pipeline | Selected |",
    "|---|---|"
)
foreach ($entry in $outputs.GetEnumerator()) {
    $summary += "| $($entry.Key) | $($entry.Value) |"
}
$summary += "", "### Changed paths"
if ($changedFiles.Count -eq 0) {
    $summary += "- No changed paths detected."
}
else {
    foreach ($file in $changedFiles) {
        $summary += "- ``$file``"
    }
}

if ($env:GITHUB_STEP_SUMMARY) {
    $summary -join [Environment]::NewLine | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
}

if ($env:GITHUB_OUTPUT) {
    foreach ($entry in $outputs.GetEnumerator()) {
        "$($entry.Key)=$($entry.Value.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }
}

$outputs | ConvertTo-Json -Compress
