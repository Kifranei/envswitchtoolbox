# ===== EnvSwitchToolbox route delete (undo 「修复路由」) =====
# Variables $Net $Mask $PrefixLen $Keyword are prepended by EnvSwitchToolbox.exe
# Goal: remove the dedicated-line / wired routes for this site prefix so that
# Tailscale (or on-link) can take the prefix again after the cable is unplugged.
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$Utf8 = New-Object System.Text.UTF8Encoding $false
try { [Console]::OutputEncoding = $Utf8 } catch {}

function Log([string]$lvl, [string]$msg) {
    [Console]::Out.WriteLine("$lvl $msg")
    [Console]::Out.Flush()
}

function Invoke-Native([string]$exe, [string[]]$argv) {
    $prev = [Console]::OutputEncoding
    $text = ''
    try {
        try { [Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage) } catch {}
        $text = (& $exe @argv 2>&1 | Out-String)
        $code = $LASTEXITCODE
    } finally {
        try { [Console]::OutputEncoding = $Utf8 } catch {}
    }
    return [pscustomobject]@{ Code = $code; Text = $text.Trim() }
}

function Get-R([string]$store) {
    Get-NetRoute -DestinationPrefix $script:prefix -AddressFamily IPv4 -PolicyStore $store -ErrorAction SilentlyContinue
}

trap {
    Log '[Error]' ("脚本异常: " + $_.Exception.Message)
    exit 9
}

$TsNextHop = '100.100.100.100'
$script:prefix = "$Net/$PrefixLen"
$rc = 0

# ---------- helpers ----------
function Test-AdapterInSubnet([int]$ifIndex) {
    # True if the adapter is Up and has an IPv4 address inside $Net/$PrefixLen
    $a = Get-NetAdapter -InterfaceIndex $ifIndex -ErrorAction SilentlyContinue
    if (-not $a -or $a.Status -ne 'Up') { return $false }
    $addrs = @(Get-NetIPAddress -InterfaceIndex $ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)
    foreach ($ip in $addrs) {
        try {
            $bytes = ([System.Net.IPAddress]::Parse($ip.IPAddress)).GetAddressBytes()
            $netBytes = ([System.Net.IPAddress]::Parse($Net)).GetAddressBytes()
            $maskBytes = ([System.Net.IPAddress]::Parse($Mask)).GetAddressBytes()
            $ok = $true
            for ($i = 0; $i -lt 4; $i++) {
                if (($bytes[$i] -band $maskBytes[$i]) -ne ($netBytes[$i] -band $maskBytes[$i])) { $ok = $false; break }
            }
            if ($ok) { return $true }
        } catch {}
    }
    return $false
}

function Remove-PersistentRegistry {
    # HKLM\...\PersistentRoutes values look like: "dest,mask,gateway,metric"
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\PersistentRoutes'
    if (-not (Test-Path $key)) { return }
    $want = "$Net,$Mask,"
    $removed = 0
    try {
        $props = Get-ItemProperty -Path $key -ErrorAction Stop
        foreach ($n in $props.PSObject.Properties.Name) {
            if ($n -like 'PS*') { continue }
            # Value name itself is the "dest,mask,gateway,metric" string
            if ($n.StartsWith($want, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-ItemProperty -Path $key -Name $n -ErrorAction SilentlyContinue
                Log '[~]' "已删除注册表持久路由: $n"
                $removed++
            }
        }
    } catch {
        Log '[~]' ("读取 PersistentRoutes 失败: " + $_.Exception.Message)
    }
    if ($removed -eq 0) { Log '[-]' '注册表 PersistentRoutes 中没有匹配本网段的条目' }
}

# ---------- 1. 网卡识别 ----------
Log '[*]' "查找 InterfaceDescription 包含 '$Keyword' 的有线网卡..."
$all = @(Get-NetAdapter -ErrorAction SilentlyContinue)
$adapter = $all | Where-Object { $_.InterfaceDescription -like "*$Keyword*" } |
           Sort-Object @{ Expression = { $_.Status -ne 'Up' } } | Select-Object -First 1
$idx = $null
if ($adapter) {
    $idx = [int]$adapter.InterfaceIndex
    Log '[*]' ("有线网卡: {0} / {1} (IfIndex {2}, 状态 {3})" -f $adapter.Name, $adapter.InterfaceDescription, $idx, $adapter.Status)
} else {
    Log '[~]' "未找到匹配 '$Keyword' 的网卡（仍会删除本网段非 Tailscale 路由）。当前网卡："
    foreach ($a in $all) { Log '[-]' ("    {0} | {1} | {2}" -f $a.Name, $a.InterfaceDescription, $a.Status) }
}

# Tailscale / Wintun adapter
$ts = $all | Where-Object {
    $_.InterfaceDescription -like '*Tailscale*' -or $_.InterfaceDescription -like '*Wintun*' -or $_.Name -like '*Tailscale*'
} | Sort-Object @{ Expression = { $_.Status -ne 'Up' } } | Select-Object -First 1
$tsIdx = $null
if ($ts) {
    $tsIdx = [int]$ts.InterfaceIndex
    Log '[*]' ("Tailscale 网卡: {0} / {1} (IfIndex {2}, 状态 {3})" -f $ts.Name, $ts.InterfaceDescription, $tsIdx, $ts.Status)
} else {
    Log '[~]' '未找到 Tailscale 网卡（删除后不会自动补回 Tailscale 路由）'
}

# ---------- 2. 列出并删除非 Tailscale 路由 ----------
$active = @(Get-R 'ActiveStore')
$persistent = @(Get-R 'PersistentStore')
$allRoutes = @($active + $persistent)
Log '[*]' ("当前 $($script:prefix) 路由：生效 {0} 条 / 持久 {1} 条" -f $active.Count, $persistent.Count)
foreach ($r in $allRoutes) {
    Log '[-]' ("    via {0} if {1} metric {2} [{3}]" -f $r.NextHop, $r.InterfaceIndex, $r.RouteMetric, $r.Store)
}

$toRemove = @()
$keptDirect = 0
foreach ($r in $allRoutes) {
    $hop = "$($r.NextHop)"
    $ifx = [int]$r.InterfaceIndex
    if ($hop -eq $TsNextHop) {
        Log '[-]' ("保留 Tailscale 路由: via {0} if {1} [{2}]" -f $hop, $ifx, $r.Store)
        continue
    }
    if ($hop -eq '0.0.0.0') {
        # 直连：仅当该网卡仍在本网段且已连接时保留
        if (Test-AdapterInSubnet $ifx) {
            Log '[*]' ("保留直连路由: via 0.0.0.0 if {0}（该网卡仍在 $($script:prefix) 网段内）[{1}]" -f $ifx, $r.Store)
            $keptDirect++
            continue
        }
        Log '[~]' ("删除失效直连路由: via 0.0.0.0 if {0}（网卡已不在本网段或未连接）[{1}]" -f $ifx, $r.Store)
        $toRemove += $r
        continue
    }
    Log '[~]' ("删除修复路由: via {0} if {1} [{2}]" -f $hop, $ifx, $r.Store)
    $toRemove += $r
}

if ($toRemove.Count -gt 0) {
    $toRemove | Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
    # Also clear via route.exe by next-hop (avoids wiping Tailscale / kept direct)
    foreach ($hop in @($toRemove | ForEach-Object { "$($_.NextHop)" } | Sort-Object -Unique)) {
        if ($hop -eq '0.0.0.0') {
            # route.exe delete with 0.0.0.0 can be ambiguous; try without hop, then rely on Remove-NetRoute
            $null = Invoke-Native 'route.exe' @('delete', $Net, 'mask', $Mask, '0.0.0.0')
        } else {
            $null = Invoke-Native 'route.exe' @('delete', $Net, 'mask', $Mask, $hop)
        }
    }
    Log '[+]' ("已删除 {0} 条非 Tailscale 路由" -f $toRemove.Count)
} else {
    Log '[-]' '没有需要删除的非 Tailscale 路由'
}

# Always scrub matching PersistentRoutes registry entries (covers leftovers Remove-NetRoute missed)
Remove-PersistentRegistry

# ---------- 3. 恢复有线网卡 AutomaticMetric ----------
if ($null -ne $idx) {
    try {
        Set-NetIPInterface -InterfaceIndex $idx -AddressFamily IPv4 -AutomaticMetric Enabled -ErrorAction Stop
        Log '[+]' ("已恢复有线网卡 (if {0}) 的 AutomaticMetric" -f $idx)
    } catch {
        Log '[~]' ("恢复 AutomaticMetric 失败: " + $_.Exception.Message)
        $rc = 6
    }
}

# ---------- 4. 若 Tailscale 在线且本网段路由缺失，补回 ActiveStore 路由 ----------
$tsUp = ($null -ne $tsIdx) -and ($ts.Status -eq 'Up')
$haveTs = @((@(Get-R 'ActiveStore') + @(Get-R 'PersistentStore')) | Where-Object { $_.NextHop -eq $TsNextHop }).Count -gt 0
if ($tsUp -and -not $haveTs) {
    # Skip if a live on-link route already covers the prefix (cable still plugged into that subnet)
    $liveDirect = @(Get-R 'ActiveStore' | Where-Object { $_.NextHop -eq '0.0.0.0' -and (Test-AdapterInSubnet ([int]$_.InterfaceIndex)) })
    if ($liveDirect.Count -gt 0) {
        Log '[*]' '有线网卡仍直连本网段，不补加 Tailscale 路由'
    } else {
        try {
            New-NetRoute -DestinationPrefix $script:prefix -InterfaceIndex $tsIdx -NextHop $TsNextHop `
                -AddressFamily IPv4 -RouteMetric 5 -PolicyStore ActiveStore -ErrorAction Stop | Out-Null
            Log '[+]' ("已补加 Tailscale 路由: {0} via {1} if {2} metric 5 (ActiveStore)" -f $script:prefix, $TsNextHop, $tsIdx)
        } catch {
            Log '[!!]' ("补加 Tailscale 路由失败: " + $_.Exception.Message)
            Log '[~]' '可在 Tailscale 中开启「Use Tailscale subnets」，或执行: tailscale set --accept-routes=true'
            $rc = 7
        }
    }
} elseif (-not $tsUp) {
    Log '[~]' 'Tailscale 未连接，跳过补加路由（连上后若仍无本网段，请开启 accept-routes 或再点一次「删除修复路由」）'
} elseif ($haveTs) {
    Log '[-]' "Tailscale 路由已存在，无需补加"
}

# ---------- 5. 验证：打印本网段路由 ----------
Start-Sleep -Milliseconds 500
Log '[*]' "删除后 $($script:prefix) 路由："
$final = @((@(Get-R 'ActiveStore') + @(Get-R 'PersistentStore')))
if ($final.Count -eq 0) {
    Log '[~]' '（无）'
} else {
    foreach ($r in $final) {
        Log '[+]' ("    via {0} if {1} metric {2} [{3}]" -f $r.NextHop, $r.InterfaceIndex, $r.RouteMetric, $r.Store)
    }
}
# Also dump matching lines from route print -4 for the operator
$rp = Invoke-Native 'route.exe' @('print', '-4')
if ($rp.Text) {
    $lines = $rp.Text -split "`r?`n" | Where-Object { $_ -match [regex]::Escape($Net) -or $_ -match '^\s*Network Destination' -or $_ -match '^\s*网络目标' }
    if ($lines) {
        Log '[*]' 'route print -4 相关行：'
        foreach ($ln in $lines) { if ($ln.Trim()) { Log '[-]' ('    ' + $ln.Trim()) } }
    }
}

if ($rc -eq 0) { Log '[OK]' "已删除 $($script:prefix) 的修复路由；专线拔掉后可由 Tailscale 接管该网段" }
exit $rc
