<#
.SYNOPSIS
  Pre-release check: what changed in the original MeshCom node firmware since the last time we looked?

.DESCRIPTION
  Works on the clean local clone of https://github.com/icssw-org/MeshCom-Firmware
  (default C:\SRC\RA\MeshComFirmware\MeshCom-Firmware-original, remote "upstream").
  It only does `git fetch` (read-only towards GitHub) and compares the newest release tag with the
  baseline in scripts/firmware-baseline.json:
    - new release tags since the baseline
    - hardware names added to the hardware table (nbr_views.cpp / mheard_functions.cpp)  -> new hardware ids
    - new board folders in variants/
    - diffstat of the files that define the wire format / console commands / hardware table
  The script does NOT judge compatibility. Read the printed diffs (git diff <baseline> <latest> -- <file>)
  against WebDesk (MeshcomUdpService, ConsoleCommandHelperService, Helpers/MeshcomLookup.cs).

  After the review, record the new state:  scripts/check-firmware.ps1 -UpdateBaseline

  Exit code: 0 = nothing new, 2 = news to review, 1 = error.
#>
param(
    [string]$Repo   = 'C:\SRC\RA\MeshComFirmware\MeshCom-Firmware-original',
    [string]$Remote = 'upstream',
    [switch]$UpdateBaseline
)
$ErrorActionPreference = 'Stop'
$baselineFile = Join-Path $PSScriptRoot 'firmware-baseline.json'

# Files that define what WebDesk depends on
$watched = @(
    'src/extudp_functions.cpp', 'src/extudp_functions.h',   # ext-UDP JSON wire format
    'src/udp_functions.cpp',
    'src/command_functions.cpp',                            # --info lines, console commands
    'src/mheard_functions.cpp', 'src/mheard_functions.h',   # [MH] format (older versions: hardware table)
    'src/nbr_views.cpp', 'src/nbr_views.h',                 # hardware table (newer versions)
    'src/aprs_functions.cpp',                               # message / ACK / position handling
    'src/configuration_global.h'
)

function Invoke-Git { $o = & git -C $Repo @args 2>&1; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' '): $o" }; $o }

if (-not (Test-Path (Join-Path $Repo '.git'))) { Write-Host "Firmware clone not found: $Repo" -ForegroundColor Red; exit 1 }
Write-Host "Fetching $Remote (tags) ..."
Invoke-Git fetch $Remote --tags --quiet | Out-Null

# Release tags = v4.* of the ORIGINAL repo only (names taken from the remote itself, so tags that
# exist locally only because of a personal fork are ignored), newest first by tag date
$remoteUrl = (Invoke-Git remote get-url $Remote)
if ($remoteUrl -notmatch 'icssw-org/MeshCom-Firmware') { Write-Host "Remote '$Remote' is $remoteUrl - expected the original icssw-org repo." -ForegroundColor Red; exit 1 }
$remoteTags = @(Invoke-Git ls-remote --tags --refs $Remote 'v4.*') | ForEach-Object { ($_ -split "`t")[1] -replace '^refs/tags/', '' }
$tags = @(Invoke-Git tag --list 'v4.*' --sort=-creatordate | Where-Object { $_ -in $remoteTags })
if ($tags.Count -eq 0) { Write-Host 'No v4.* tags found.' -ForegroundColor Red; exit 1 }
$latest = $tags[0]

function Get-HwNames($ref) {
    # The table moved between firmware versions (mheard_functions.cpp: HardWare[], newer: nbr_views.cpp: nbrHwNames[]),
    # so look for whichever file in this ref holds it. The long names are the LAST initialiser in that file.
    $hit = @(Invoke-Git grep -l -E '(HardWare\[max_hardware\]|nbrHwNames\[[A-Za-z_]+\])\s*=' $ref '--' src) | Select-Object -First 1
    if (-not $hit) { return @() }
    $file = ($hit -replace '^[^:]*:', '')
    $src = (Invoke-Git show "${ref}:$file") -join "`n"
    $m = [regex]::Matches($src, '(?:HardWare\[max_hardware\]|nbrHwNames\[[A-Za-z_]+\])\s*=\s*\{([^}]*)\}')
    if ($m.Count -eq 0) { return @() }
    return [regex]::Matches($m[$m.Count - 1].Groups[1].Value, '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value }
}
function Get-Variants($ref) { @(Invoke-Git ls-tree -d --name-only $ref 'variants/') | ForEach-Object { $_ -replace '^variants/', '' } }

if (-not (Test-Path $baselineFile)) {
    Write-Host "No baseline yet ($baselineFile). Creating it from $latest." -ForegroundColor Yellow
    $UpdateBaseline = $true
    $base = $null
} else {
    $base = Get-Content $baselineFile -Raw | ConvertFrom-Json
}

$news = 0
if ($base) {
    Write-Host ("Baseline: {0}   Latest upstream tag: {1}" -f $base.tag, $latest)
    $newTags = @()
    foreach ($t in $tags) { if ($t -eq $base.tag) { break }; $newTags += $t }
    if ($base.tag -notin $tags) { Write-Host "Baseline tag $($base.tag) not found in the clone - comparing with HEAD of tags only." -ForegroundColor Yellow }

    if ($newTags.Count -gt 0) {
        $news++
        Write-Host "`n[NEW] release tags since baseline: $($newTags -join ', ')" -ForegroundColor Yellow
        Write-Host "      Release notes: https://github.com/icssw-org/MeshCom-Firmware/releases/tag/$latest"
    } else { Write-Host '[OK ] no new release tags' -ForegroundColor Green }

    $hwNow = @(Get-HwNames $latest); $hwOld = @($base.hardware)
    $hwNew = @($hwNow | Where-Object { $_ -notin $hwOld })
    $hwGone = @($hwOld | Where-Object { $_ -notin $hwNow })
    if ($hwNew.Count -or $hwGone.Count) {
        $news++
        Write-Host "[NEW] hardware table changed: added [$($hwNew -join ', ')] removed [$($hwGone -join ', ')]" -ForegroundColor Yellow
        Write-Host "      -> add ids to Helpers/MeshcomLookup.cs HwName/HwIcon (old-id remap: getHardwareLong() / nbrHardwareName())"
    } else { Write-Host "[OK ] hardware table unchanged ($($hwNow.Count) entries)" -ForegroundColor Green }

    $vNow = @(Get-Variants $latest); $vOld = @($base.variants)
    $vNew = @($vNow | Where-Object { $_ -notin $vOld })
    if ($vNew.Count) { $news++; Write-Host "[NEW] new board variants: $($vNew -join ', ')" -ForegroundColor Yellow }
    else { Write-Host "[OK ] no new board variants ($($vNow.Count))" -ForegroundColor Green }

    if ($base.tag -in $tags -and $base.tag -ne $latest) {
        $stat = Invoke-Git diff --stat "$($base.tag)..$latest" '--' @watched
        if ($stat) {
            $news++
            Write-Host "`n[NEW] changes in files WebDesk depends on:" -ForegroundColor Yellow
            $stat | ForEach-Object { Write-Host "      $_" }
            Write-Host "`n      Review:  git -C `"$Repo`" diff $($base.tag)..$latest -- <file>"
        } else { Write-Host '[OK ] watched files unchanged' -ForegroundColor Green }
    }
}

if ($UpdateBaseline) {
    [pscustomobject]@{
        tag      = $latest
        checked  = (Get-Date -Format 'yyyy-MM-dd')
        hardware = @(Get-HwNames $latest)
        variants = @(Get-Variants $latest)
    } | ConvertTo-Json | Set-Content $baselineFile -Encoding utf8
    Write-Host "`nBaseline written: $baselineFile (tag $latest)" -ForegroundColor Cyan
    exit 0
}

if ($news -gt 0) { Write-Host "`n$news item(s) to review. After the review run: scripts/check-firmware.ps1 -UpdateBaseline" -ForegroundColor Yellow; exit 2 }
Write-Host "`nNothing new in the firmware since $($base.tag)." -ForegroundColor Green
