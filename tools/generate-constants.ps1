<#
.SYNOPSIS
    Writes src/XmlAuditfile/Versioning/AuditfileVersion.Constants.cs from the registry of auditfile-spec.

.DESCRIPTION
    Run after tools/fetch-spec.ps1, whenever spec.lock.json changes; the auditfile-spec update workflow does this too.
    With -Check nothing is written: the script fails when the constants differ from the registry.
    Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    pwsh tools/generate-constants.ps1
#>
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$specRoot = if ($env:SPEC_ROOT) { Join-Path $Root $env:SPEC_ROOT } else { Join-Path $Root 'spec' }
$registryPath = Join-Path $specRoot 'generated/registry.json'
$target = Join-Path $Root 'src/XmlAuditfile/Versioning/AuditfileVersion.Constants.cs'
$targetName = 'src/XmlAuditfile/Versioning/AuditfileVersion.Constants.cs'

if (-not (Test-Path $registryPath)) {
    throw "auditfile-spec not found in '$specRoot'. Run: pwsh tools/fetch-spec.ps1"
}

# The registry has no display names; a type that is not here gets its code.
$typeNames = @{
    xaf = 'Auditfile Financieel'
    xaa = 'Auditfile Afrekensystemen'
    xab = 'Auditfile Boordcomputer Taxi'
    xar = 'Auditfile Ritregistratie'
    xas = 'Auditfile Salaris'
}

# Sort key per version: numeric parts padded, so 3.2.1 comes after 3.2 and 2.3.6.1 after 1.2.
function Get-VersionKey([string]$Version) {
    return (($Version -split '[._-]') | ForEach-Object { if ($_ -match '^\d+$') { $_.PadLeft(10, '0') } else { $_ } }) -join '.'
}

$registry = Get-Content $registryPath -Raw -Encoding UTF8 | ConvertFrom-Json

# Per type, in registry order, the versions in order.
$codes = New-Object System.Collections.Generic.List[string]
foreach ($standard in $registry.standards) {
    if (-not $codes.Contains($standard.type)) { $codes.Add($standard.type) }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('namespace XmlAuditfile;')
$lines.Add('')
$lines.Add('// Version names follow the registry: type name and version with underscores, e.g. V3_2 for 3.2.')
$lines.Add('#pragma warning disable CA1707')
$lines.Add('')
$lines.Add('// Nested static classes per audit file type are deliberate: AuditfileVersion.Financial.V3_2 reads as what it is.')
$lines.Add('#pragma warning disable CA1034')
$lines.Add('')
$lines.Add('// The version constants. A test checks that every registry entry has a constant here and vice versa.')
$lines.Add('// Written by tools/generate-constants.ps1 from the registry; do not edit.')
$lines.Add('public sealed partial class AuditfileVersion')
$lines.Add('{')

$firstGroup = $true
foreach ($code in $codes) {
    $standards = @($registry.standards | Where-Object { $_.type -eq $code } | Sort-Object { Get-VersionKey $_.version })
    $typeName = $standards[0].typeName
    $name = $typeName.Substring(0, 1).ToUpperInvariant() + $typeName.Substring(1)
    $label = if ($typeNames.ContainsKey($code)) { $typeNames[$code] } else { 'Auditfile' }

    if (-not $firstGroup) { $lines.Add('') }
    $firstGroup = $false
    $lines.Add("    /// <summary>$label ($($code.ToUpperInvariant())).</summary>")
    $lines.Add("    public static class $name")
    $lines.Add('    {')

    $firstVersion = $true
    foreach ($standard in $standards) {
        $published = if ($standard.origin -and $standard.origin.published) { " (published $($standard.origin.published))" } else { '' }
        $constant = 'V' + ($standard.version -replace '[^0-9A-Za-z]', '_')

        if (-not $firstVersion) { $lines.Add('') }
        $firstVersion = $false
        $lines.Add("        /// <summary>$($code.ToUpperInvariant()) $($standard.version)$published.</summary>")
        $lines.Add("        public static AuditfileVersion $constant => Parse(`"$($standard.key)`");")
    }

    $lines.Add('    }')
}

$lines.Add('}')
$lines.Add('')
$lines.Add('#pragma warning restore CA1707')
$lines.Add('#pragma warning restore CA1034')

$text = ($lines -join "`n") + "`n"
$current = if (Test-Path $target) { [System.IO.File]::ReadAllText($target) } else { '' }

if ($Check) {
    if ($text -ne $current) {
        throw "The version constants in $targetName differ from the registry. Run: pwsh tools/generate-constants.ps1"
    }

    Write-Host 'The version constants match the registry.'
}
elseif ($text -ne $current) {
    [System.IO.File]::WriteAllText($target, $text, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Updated the version constants in $targetName."
}
else {
    Write-Host 'The version constants are up to date.'
}
