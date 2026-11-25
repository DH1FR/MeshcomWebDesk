<#
.SYNOPSIS
  GitHub statistics for MeshcomWebDesk on demand, with a local history.

.DESCRIPTION
  Asks GitHub (through the gh CLI, logged in as the repository owner) for release downloads, traffic
  (clones/views), stars/forks and the Docker image, prints a summary and appends the numbers to a local
  history so that trends become visible. GitHub itself only keeps 14 days of traffic and only the running
  total of release downloads - the history files in scripts/stats-history/ (not tracked by git) close that gap.

  The numbers are downloads, clones and page views - NOT the number of running installations.
  .sha256 files are counted separately: the self-update fetches the archive together with its checksum,
  a manual browser download usually only the archive.

.PARAMETER Html
  Also write scripts/stats-history/report.html and open it in the browser.

.PARAMETER NoSave
  Do not append to the history (look only).

.PARAMETER Releases
  How many of the newest releases to list (default 12).

.EXAMPLE
  pwsh scripts/github-stats.ps1 -Html
#>
[CmdletBinding()]
param(
    [string]$Repo = 'DH1FR/MeshcomWebDesk',
    [switch]$Html,
    [switch]$NoSave,
    [int]$Releases = 12
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is required: https://cli.github.com/' }

$histDir = Join-Path $PSScriptRoot 'stats-history'
New-Item -ItemType Directory -Force $histDir | Out-Null
$fSnap = Join-Path $histDir 'snapshots.csv'
$fDown = Join-Path $histDir 'downloads.csv'
$fTraf = Join-Path $histDir 'traffic.csv'
$now   = [DateTime]::UtcNow
$ts    = $now.ToString('yyyy-MM-ddTHH:mm:ssZ')
$inv   = [Globalization.CultureInfo]::InvariantCulture

function Invoke-Gh([string]$path) {
    $out = & gh api $path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh api $path failed: $out" }
    $out | ConvertFrom-Json
}
function Read-Csv([string]$f) { if (Test-Path $f) { @(Import-Csv $f) } else { @() } }

# ---------------------------------------------------------------- collect
$repoInfo = Invoke-Gh "repos/$Repo"
$rels = @()
for ($page = 1; $page -le 5; $page++) {
    $chunk = @(Invoke-Gh "repos/$Repo/releases?per_page=100&page=$page")
    $rels += $chunk
    if ($chunk.Count -lt 100) { break }
}
$rels = @($rels | Where-Object { -not $_.draft } | Sort-Object { [DateTime]$_.published_at } -Descending)

$relRows = foreach ($r in $rels) {
    $arch = 0; $sha = 0
    foreach ($a in $r.assets) { if ($a.name -like '*.sha256') { $sha += $a.download_count } else { $arch += $a.download_count } }
    [pscustomobject]@{ Tag = $r.tag_name; Date = ([DateTime]$r.published_at).ToString('yyyy-MM-dd'); Archives = $arch; Sha256 = $sha }
}
$totalArch = ($relRows | Measure-Object Archives -Sum).Sum
$totalSha  = ($relRows | Measure-Object Sha256   -Sum).Sum

$trafficOk = $true
try {
    $clones = Invoke-Gh "repos/$Repo/traffic/clones"
    $views  = Invoke-Gh "repos/$Repo/traffic/views"
} catch { $trafficOk = $false; Write-Warning "Traffic not available (owner rights needed): $($_.Exception.Message)" }

# ---------------------------------------------------------------- previous snapshot (for the deltas)
$snaps   = Read-Csv $fSnap
$prev    = $snaps | Select-Object -Last 1
$prevDown = @{}
if ($prev) {
    foreach ($d in (Read-Csv $fDown | Where-Object ts -eq $prev.ts)) { $prevDown[$d.tag] = [int]$d.archives }
}

# ---------------------------------------------------------------- save history
if (-not $NoSave) {
    $snap = [pscustomobject]@{
        ts = $ts; stars = $repoInfo.stargazers_count; forks = $repoInfo.forks_count; watchers = $repoInfo.subscribers_count
        archives = $totalArch; sha256 = $totalSha; latest = ($relRows | Select-Object -First 1).Tag
    }
    $snap | Export-Csv $fSnap -Append -NoTypeInformation -Encoding utf8
    $relRows | ForEach-Object { [pscustomobject]@{ ts = $ts; tag = $_.Tag; archives = $_.Archives; sha256 = $_.Sha256 } } |
        Export-Csv $fDown -Append -NoTypeInformation -Encoding utf8

    if ($trafficOk) {
        # one row per day, newer numbers replace older ones (GitHub revises the current day)
        $byDay = @{}
        foreach ($t in (Read-Csv $fTraf)) { $byDay[$t.date] = $t }
        $cd = @{}; foreach ($c in $clones.clones) { $cd[([DateTime]$c.timestamp).ToString('yyyy-MM-dd')] = $c }
        $vd = @{}; foreach ($v in $views.views)   { $vd[([DateTime]$v.timestamp).ToString('yyyy-MM-dd')] = $v }
        foreach ($day in ($cd.Keys + $vd.Keys | Sort-Object -Unique)) {
            $byDay[$day] = [pscustomobject]@{
                date = $day
                clones = [int]($cd[$day].count); clones_unique = [int]($cd[$day].uniques)
                views  = [int]($vd[$day].count); views_unique  = [int]($vd[$day].uniques)
            }
        }
        $byDay.Values | Sort-Object date | Export-Csv $fTraf -NoTypeInformation -Encoding utf8
    }
}
$traffic = Read-Csv $fTraf

# ---------------------------------------------------------------- docker image (versions only; pull counts are not in the API)
$docker = $null
try {
    $owner = $repoInfo.owner.login
    $pkg = Invoke-Gh "users/$owner/packages/container/$($repoInfo.name.ToLower())"
    $docker = [pscustomobject]@{ Versions = $pkg.version_count; Url = $pkg.html_url }
} catch { }

# ---------------------------------------------------------------- output
function Delta($a, $b) { if ($null -eq $b) { '' } else { $d = $a - $b; if ($d -gt 0) { "+$d" } elseif ($d -lt 0) { "$d" } else { '0' } } }

$prevTxt = if ($prev) { "since $($prev.ts)" } else { 'first run - no previous snapshot' }
Write-Host ''
Write-Host "GitHub statistics $Repo  ($ts)" -ForegroundColor Cyan
Write-Host ("  Stars {0}  Forks {1}  Watchers {2}   |  Downloads (archives) total {3}  |  history: {4} snapshots, change {5}" -f `
    $repoInfo.stargazers_count, $repoInfo.forks_count, $repoInfo.subscribers_count, $totalArch, $snaps.Count, $prevTxt)
if ($prev) {
    Write-Host ("  Change: Stars {0}  Forks {1}  Downloads {2}" -f (Delta $repoInfo.stargazers_count ([int]$prev.stars)), (Delta $repoInfo.forks_count ([int]$prev.forks)), (Delta $totalArch ([int]$prev.archives)))
}
Write-Host ''
Write-Host "Newest releases (archives = downloads without .sha256; checksum fetches hint at self-updates):" -ForegroundColor Cyan
$relRows | Select-Object -First $Releases | ForEach-Object {
    [pscustomobject]@{ Version = $_.Tag; Released = $_.Date; Archives = $_.Archives; Sha256 = $_.Sha256
        Change = $(if ($prevDown.ContainsKey($_.Tag)) { Delta $_.Archives $prevDown[$_.Tag] } else { if ($prev) { 'new' } else { '' } }) }
} | Format-Table -AutoSize | Out-String | Write-Host

if ($trafficOk) {
    $c14 = ($clones.count); $cu = $clones.uniques; $v14 = $views.count; $vu = $views.uniques
    Write-Host ("Traffic last 14 days: clones {0} ({1} unique), page views {2} ({3} unique)" -f $c14, $cu, $v14, $vu) -ForegroundColor Cyan
    if ($traffic.Count -gt 0) {
        $first = ($traffic | Select-Object -First 1).date
        $sumC = ($traffic | Measure-Object clones -Sum).Sum; $sumV = ($traffic | Measure-Object views -Sum).Sum
        Write-Host ("Accumulated since {0} (own history): clones {1}, page views {2}" -f $first, $sumC, $sumV)
    }
}
if ($docker) { Write-Host ("Docker image: {0} versions - pull counts only on {1}" -f $docker.Versions, $docker.Url) }
Write-Host ''
Write-Host "Note: downloads/clones/views are not running installations. History: $histDir" -ForegroundColor DarkGray

# ---------------------------------------------------------------- html report
if ($Html) {
    $enc = [Net.WebUtility]
    $maxRel = [Math]::Max(1, ($relRows | Select-Object -First 30 | Measure-Object Archives -Maximum).Maximum)
    $relHtml = ($relRows | Select-Object -First 30 | ForEach-Object {
        $w = [int](100 * $_.Archives / $maxRel)
        "<tr><td>$($enc::HtmlEncode($_.Tag))</td><td>$($_.Date)</td><td class='n'>$($_.Archives)</td><td class='n'>$($_.Sha256)</td><td><div class='bar' style='width:$w%'></div></td></tr>"
    }) -join "`n"

    $tr = @($traffic | Select-Object -Last 60)
    $maxT = [Math]::Max(1, ($tr | ForEach-Object { [Math]::Max([int]$_.clones, [int]$_.views) } | Measure-Object -Maximum).Maximum)
    $trHtml = ($tr | ForEach-Object {
        $hc = [int](80 * [int]$_.clones / $maxT); $hv = [int](80 * [int]$_.views / $maxT)
        "<div class='day' title='$($_.date): clones $($_.clones) ($($_.clones_unique) unique), views $($_.views) ($($_.views_unique) unique)'><i class='c' style='height:${hc}px'></i><i class='v' style='height:${hv}px'></i></div>"
    }) -join ''

    $sn = @(Read-Csv $fSnap)
    $maxA = [Math]::Max(1, ($sn | ForEach-Object { [int]$_.archives } | Measure-Object -Maximum).Maximum)
    $snHtml = ($sn | Select-Object -Last 20 | ForEach-Object {
        $h = [int](80 * [int]$_.archives / $maxA)
        "<div class='day' title='$($_.ts): $($_.archives) downloads, $($_.stars) stars'><i class='c' style='height:${h}px'></i></div>"
    }) -join ''

    $page = @"
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>GitHub statistics</title><style>
:root{--bg:#fff;--fg:#1f2328;--mut:#656d76;--line:#d0d7de;--c:#0969da;--v:#bf8700}
@media(prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--mut:#8d96a0;--line:#30363d;--c:#4493f8;--v:#d29922}}
body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,sans-serif;max-width:900px;margin-inline:auto}
h1{font-size:20px}h2{font-size:15px;margin-top:28px}.mut{color:var(--mut)}
.k{display:flex;flex-wrap:wrap;gap:12px}.k div{border:1px solid var(--line);border-radius:8px;padding:8px 14px}.k b{display:block;font-size:22px}
table{border-collapse:collapse;width:100%}td,th{padding:4px 8px;border-bottom:1px solid var(--line);text-align:left}.n{text-align:right;font-variant-numeric:tabular-nums}
.bar{height:10px;background:var(--c);border-radius:3px;min-width:1px}
.chart{display:flex;align-items:flex-end;gap:2px;height:84px;border-bottom:1px solid var(--line)}.day{display:flex;align-items:flex-end;gap:1px;flex:1}
.day i{display:block;flex:1;min-width:1px}.c{background:var(--c)}.v{background:var(--v)}
</style></head><body>
<h1>GitHub statistics &ndash; $Repo</h1><p class="mut">$ts &middot; downloads, clones and page views are not running installations.</p>
<div class="k"><div><b>$($repoInfo.stargazers_count)</b>Stars</div><div><b>$($repoInfo.forks_count)</b>Forks</div><div><b>$totalArch</b>Downloads (archives)</div>
<div><b>$totalSha</b>Checksum fetches</div><div><b>$(if($trafficOk){$clones.count}else{'-'})</b>Clones / 14 days</div><div><b>$(if($trafficOk){$views.count}else{'-'})</b>Views / 14 days</div></div>
<h2>Traffic per day <span class="mut">(<span style="color:var(--c)">&#9632;</span> clones, <span style="color:var(--v)">&#9632;</span> views; own history, last 60 days)</span></h2>
<div class="chart">$trHtml</div>
<h2>Total downloads per snapshot <span class="mut">(last 20 runs)</span></h2><div class="chart">$snHtml</div>
<h2>Releases</h2><table><tr><th>Version</th><th>Released</th><th class="n">Archives</th><th class="n">.sha256</th><th></th></tr>
$relHtml</table>
$(if($docker){"<p class='mut'>Docker image: $($docker.Versions) versions &ndash; pull counts only on <a href='$($docker.Url)'>the package page</a>.</p>"})
</body></html>
"@
    $out = Join-Path $histDir 'report.html'
    Set-Content -Path $out -Value $page -Encoding utf8
    Write-Host "Report: $out"
    Start-Process $out
}
