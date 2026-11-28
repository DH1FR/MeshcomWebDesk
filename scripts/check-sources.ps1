<#
.SYNOPSIS
  Pre-release check of the external MeshCom dashboards that WebDesk scrapes.

.DESCRIPTION
  For every preset in GatewayService.PresetUrls it fetches rakgw.html and nodes.html and applies
  the SAME regular expressions as the app (GatewayService.CallsignRegex / NodePresenceService.RowRegex).
  It also checks that the meshmap JSON endpoint still has the expected field set (informational only,
  WebDesk does not use it yet).

  Exit code 0 = all fine, 1 = at least one FAIL (markup changed / unreachable / no rows).
  A preset listed in $noNodesExpected may lack nodes.html (WARN, not FAIL); the list is currently empty.

  Keep the URLs and regexes in sync with the C# sources when they change.
#>
$ErrorActionPreference = 'Stop'

$presets = [ordered]@{
    oe = 'https://meshcom.oevsv.at/rakgw.html'
    dl = 'http://meshcom.hamnet.network/rakgw.html'
    it = 'https://meshcom.dig-italia.it/rakgw.html'
    hb = 'https://meshcom.emcomm.ch/rakgw.html'
    sp = 'https://sp.meshcom.pl/rakgw.html'
}
# Presets known to have no nodes.html: downgrade to WARN (currently all presets serve it)
$noNodesExpected = @()

$gwRegex   = [regex]::new('<td\s+bgcolor="#00FF66">([A-Z0-9]+-\d+)\s*\(\d+\)</td>', 'IgnoreCase')
$rowRegex  = [regex]::new('<tr><td>\d+</td><td>(?<t>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})</td><td[^>]*>(?<gw>[^<]*)</td><td[^>]*>(?<call>[^<]*)</td>')

$fail = 0
function Get-Page($url) {
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30 -Headers @{ 'Accept-Encoding' = 'gzip' }
        return [pscustomobject]@{ Ok = $true; Html = $r.Content; Err = $null }
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        return [pscustomobject]@{ Ok = $false; Html = ''; Err = if ($code) { "HTTP $code" } else { $_.Exception.Message } }
    }
}
function Show($level, $msg) {
    $color = @{ OK = 'Green'; WARN = 'Yellow'; FAIL = 'Red' }[$level]
    Write-Host ("[{0,-4}] {1}" -f $level, $msg) -ForegroundColor $color
}

foreach ($k in $presets.Keys) {
    $gwUrl = $presets[$k]
    $p = Get-Page $gwUrl
    if (-not $p.Ok) { Show FAIL "$k rakgw.html  $gwUrl  -> $($p.Err)"; $fail++ }
    else {
        $n = $gwRegex.Matches($p.Html).Count
        if ($n -eq 0) { Show FAIL "$k rakgw.html: 0 gateways matched - markup changed?"; $fail++ }
        else { Show OK "$k rakgw.html: $n gateways" }
    }

    $nodesUrl = $gwUrl -replace 'rakgw\.html', 'nodes.html'
    $p = Get-Page $nodesUrl
    if (-not $p.Ok) {
        $lvl = if ($noNodesExpected -contains $k) { 'WARN' } else { 'FAIL' }
        if ($lvl -eq 'FAIL') { $fail++ }
        Show $lvl "$k nodes.html: $($p.Err)$(if ($lvl -eq 'WARN') { ' (expected, no nodes.html)' })"
        continue
    }
    $rows = $rowRegex.Matches($p.Html)
    if ($rows.Count -eq 0) { Show FAIL "$k nodes.html: 0 rows matched - markup changed?"; $fail++; continue }
    $newest = ($rows | ForEach-Object { [datetime]::Parse(($_.Groups['t'].Value -replace '\s+', ' '), [cultureinfo]::InvariantCulture) } | Measure-Object -Maximum).Maximum
    $ageH = ((Get-Date) - $newest).TotalHours   # server prints local time; rough check only
    if ([math]::Abs($ageH) -gt 6) { Show WARN "$k nodes.html: $($rows.Count) rows, newest entry $newest (age ~$([math]::Round($ageH,1)) h)" }
    else { Show OK "$k nodes.html: $($rows.Count) rows, newest $newest" }
}

# meshmap (informational): expected per-node field set
try {
    $j = Invoke-RestMethod -Uri 'https://meshmap.oevsv.at/api/v2/nodes?limit=5' -TimeoutSec 30
    $expected = 'call','lat','lon','altitudeM','batteryPercent','hardware','firmware','isGateway','name','description',
                'country','lastSeenUnix','nbcnt','source','positionWithheld','gwStatus','gwStatusSinceUnix'
    $have = $j.nodes[0].PSObject.Properties.Name
    $missing = $expected | Where-Object { $_ -notin $have }
    $extra   = $have | Where-Object { $_ -notin $expected }
    if ($missing -or $extra) { Show WARN "meshmap /api/v2/nodes fields changed. missing: [$($missing -join ', ')] new: [$($extra -join ', ')]" }
    else { Show OK 'meshmap /api/v2/nodes: field set unchanged (informational)' }
} catch { Show WARN "meshmap /api/v2/nodes: $($_.Exception.Message) (informational)" }

if ($fail -gt 0) { Write-Host "`n$fail check(s) FAILED - fix GatewayService/NodePresenceService before releasing." -ForegroundColor Red; exit 1 }
Write-Host "`nAll scraping sources OK." -ForegroundColor Green
