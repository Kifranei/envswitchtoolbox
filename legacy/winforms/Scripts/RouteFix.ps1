# ===== EnvSwitchToolbox route fix =====
# Variables $Net $Mask $PrefixLen $Keyword $FallbackGW are prepended by EnvSwitchToolbox.exe
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$Utf8 = New-Object System.Text.UTF8Encoding $false
try { [Console]::OutputEncoding = $Utf8 } catch {}

function Log([string]$lvl, [string]$msg) {
    [Console]::Out.WriteLine("$lvl $msg")
    [Console]::Out.Flush()
}

# Run a native exe (route.exe etc.) and decode its output with the OEM code page (GBK on Chinese Windows)
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

# ---------- 1. 网卡识别 ----------
Log '[*]' "查找 InterfaceDescription 包含 '$Keyword' 的网卡..."
$all = @(Get-NetAdapter -ErrorAction SilentlyContinue)
$adapter = $all | Where-Object { $_.InterfaceDescription -like "*$Keyword*" } |
           Sort-Object @{ Expression = { $_.Status -ne 'Up' } } | Select-Object -First 1
if (-not $adapter) {
    Log '[Error]' "未找到匹配 '$Keyword' 的网卡！可在地点配置中修改「网卡关键字」。当前网卡："
    foreach ($a in $all) { Log '[-]' ("    {0} | {1} | {2}" -f $a.Name, $a.InterfaceDescription, $a.Status) }
    exit 2
}
$idx = [int]$adapter.InterfaceIndex
Log '[*]' ("使用网卡: {0} / {1} (IfIndex {2}, 状态 {3})" -f $adapter.Name, $adapter.InterfaceDescription, $idx, $adapter.Status)
if ($adapter.Status -ne 'Up') { Log '[!!]' "该网卡当前未连接（$($adapter.Status)），请检查网线/内网连接" }

# 调高有线 Metric，默认流量（上网）继续走 WiFi
try {
    Set-NetIPInterface -InterfaceIndex $idx -AddressFamily IPv4 -InterfaceMetric 500 -ErrorAction Stop
    Log '[+]' "已将该网卡 IPv4 接口跃点数设为 500（上网继续走 WiFi）"
} catch {
    Log '[~]' ("设置接口跃点数失败: " + $_.Exception.Message)
}

# ---------- 2. 网关 ----------
$gw = $null
$ipcfg = Get-NetIPConfiguration -InterfaceIndex $idx -ErrorAction SilentlyContinue
if ($ipcfg -and $ipcfg.IPv4DefaultGateway) { $gw = (@($ipcfg.IPv4DefaultGateway)[0]).NextHop }
if (-not $gw) {
    $dr = @(Get-NetRoute -InterfaceIndex $idx -DestinationPrefix '0.0.0.0/0' -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.NextHop -and $_.NextHop -ne '0.0.0.0' })
    if ($dr.Count -gt 0) { $gw = $dr[0].NextHop }
}
if ($gw) {
    Log '[*]' "网卡上配置的网关: $gw"
} elseif ($FallbackGW) {
    $gw = $FallbackGW
    Log '[~]' "网卡上未配置网关，使用兜底网关: $gw"
}

# 有线网卡本身就在该网段（直连 / on-link 路由，下一跳 0.0.0.0）：保护它，不新增也不删除
$script:connected = @(Get-R 'ActiveStore' | Where-Object { [int]$_.InterfaceIndex -eq $idx -and $_.NextHop -eq '0.0.0.0' })
$onLink = $script:connected.Count -gt 0
if ($onLink) {
    Log '[*]' "有线网卡本身就在 $($script:prefix) 网段内（直连路由），无需添加路由，只清理冲突路由"
}

if (-not $gw -and -not $onLink) {
    Log '[Error]' "网卡上没有 IPv4 默认网关，且该地点未设置兜底网关！请在网卡上配置网关，或在地点配置中填写「兜底网关」。"
    exit 3
}

# ---------- 3. 清除 Tailscale 抢走的同网段路由 ----------
# 只处理本地点前缀（如 192.0.2.0/24）下一跳为 100.100.100.100 的路由；
# Get-NetRoute -DestinationPrefix 是精确匹配，Tailscale 自己的 100.100.100.100/32、100.64.0.0/10 等不会被选中。
# 若网卡上的真实网关恰好就是 100.100.100.100（经由有线网卡），则不当作 Tailscale 路由。
function Test-TsRoute($r) { ($r.NextHop -eq $TsNextHop) -and -not ($gw -eq $TsNextHop -and [int]$r.InterfaceIndex -eq $idx) }
if ($gw -eq $TsNextHop) { Log '[~]' "网关本身就是 $TsNextHop，仅删除不在有线网卡上的同前缀 $TsNextHop 路由" }
$tsRoutes = @((@(Get-R 'ActiveStore') + @(Get-R 'PersistentStore')) | Where-Object { Test-TsRoute $_ })
if ($tsRoutes.Count -gt 0) {
    foreach ($r in $tsRoutes) {
        Log '[~]' ("删除 Tailscale 路由: {0} via {1} (if {2}, metric {3}, {4})" -f $script:prefix, $r.NextHop, $r.InterfaceIndex, $r.RouteMetric, $r.Store)
    }
    $tsRoutes | Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
    if ($gw -ne $TsNextHop) { $null = Invoke-Native 'route.exe' @('delete', $Net, 'mask', $Mask, $TsNextHop) }
    if (@(Get-R 'ActiveStore' | Where-Object { Test-TsRoute $_ }).Count -gt 0) {
        Log '[!!]' "Tailscale 路由仍然存在（Tailscale 可能会立即重新添加）"
    } else {
        Log '[+]' "Tailscale 路由已删除"
    }
} else {
    Log '[-]' "没有指向 $TsNextHop 的 $($script:prefix) 路由"
}

# 网段与 Tailscale 的 CGNAT 地址段 100.64.0.0/10 重叠时给出提示
$netU = [uint32]0; $octets = $Net.Split('.'); foreach ($o in $octets) { $netU = $netU * 256 + [uint32]$o }
if ($netU -ge [uint32]1681915904 -and $netU -le [uint32]1686110207) {
    Log '[~]' "注意：网段 $($script:prefix) 位于 Tailscale 使用的 100.64.0.0/10 内；本工具只修改该 /$PrefixLen 前缀，Tailscale 的 100.100.100.100/32 等路由不受影响。若内网不通，请关闭 Tailscale 子网路由（--accept-routes=false）。"
}

# ---------- 4. 路由检查/重建 ----------
if ($onLink) {
    # 只删除：不在有线网卡上的、或下一跳既不是直连也不是本网卡网关的路由；直连路由绝不删除
    $bad = @((@(Get-R 'ActiveStore') + @(Get-R 'PersistentStore')) | Where-Object {
        [int]$_.InterfaceIndex -ne $idx -or ($_.NextHop -ne '0.0.0.0' -and $_.NextHop -ne $gw)
    })
    if ($bad.Count -gt 0) {
        foreach ($r in $bad) {
            Log '[~]' ("不一致: {0} via {1} (if {2}, {3}) -> 删除（直连路由保留）" -f $script:prefix, $r.NextHop, $r.InterfaceIndex, $r.Store)
        }
        $bad | Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
        # route.exe 只按具体下一跳删除，避免误删直连路由
        foreach ($hop in @($bad | ForEach-Object { "$($_.NextHop)" } | Sort-Object -Unique)) {
            if ($hop -and $hop -ne '0.0.0.0' -and $hop -ne $gw) { $null = Invoke-Native 'route.exe' @('delete', $Net, 'mask', $Mask, $hop) }
        }
    } else {
        Log '[-]' "没有需要清理的路由"
    }
} else {
Log '[*]' "目标路由: $($script:prefix) via $gw (if $idx)"
$active     = @(Get-R 'ActiveStore')
$persistent = @(Get-R 'PersistentStore')
$existing   = @($active + $persistent)
$bad        = @($existing | Where-Object { $_.NextHop -ne $gw -or [int]$_.InterfaceIndex -ne $idx })

if ($existing.Count -gt 0 -and $bad.Count -eq 0 -and $active.Count -eq 1 -and $persistent.Count -eq 1) {
    Log '[-]' "路由已正确（生效 + 持久各 1 条），跳过"
} else {
    if ($existing.Count -gt 0) {
        foreach ($r in $bad) {
            Log '[~]' ("不一致: {0} via {1} (if {2}, {3})，目标 {4} (if {5}) -> 删除" -f $script:prefix, $r.NextHop, $r.InterfaceIndex, $r.Store, $gw, $idx)
        }
        if ($bad.Count -eq 0) { Log '[~]' ("现有路由数量异常（生效 {0} 条 / 持久 {1} 条），清除后重建" -f $active.Count, $persistent.Count) }
        $existing | Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
        $null = Invoke-Native 'route.exe' @('delete', $Net, 'mask', $Mask)
    } else {
        Log '[*]' "未找到该网段路由，准备添加"
    }
    $res = Invoke-Native 'route.exe' @('-p', 'add', $Net, 'mask', $Mask, $gw, 'metric', '1', 'if', "$idx")
    if ($res.Text) { Log '[-]' ("route add 输出: " + ($res.Text -replace "\s+", ' ')) }
    Log '[+]' "已执行: route -p add $Net mask $Mask $gw metric 1 if $idx"
}
}

# ---------- 5. 验证 ----------
Start-Sleep -Seconds 2
$now = @(Get-R 'ActiveStore' | Where-Object { $_.NextHop -eq $gw -and [int]$_.InterfaceIndex -eq $idx })
$per = @(Get-R 'PersistentStore' | Where-Object { $_.NextHop -eq $gw -and [int]$_.InterfaceIndex -eq $idx })
$conn = @(Get-R 'ActiveStore' | Where-Object { $_.NextHop -eq '0.0.0.0' -and [int]$_.InterfaceIndex -eq $idx })
$rc = 0
if ($onLink) {
    if ($conn.Count -gt 0) { Log '[OK]' ("直连路由正常: {0} 在有线网卡上 (if {1})" -f $script:prefix, $idx) }
    else { Log '[!!]' "直连路由不见了！请检查有线网卡 IP 设置，或点击「查看路由」"; $rc = 4 }
} elseif ($now.Count -gt 0) {
    Log '[OK]' ("路由已生效: {0} -> {1} (if {2})，持久路由 {3} 条" -f $script:prefix, $gw, $idx, $per.Count)
} else {
    Log '[!!]' "路由未生效！请点击「查看路由」检查 route print -4"
    $rc = 4
}
if (@(Get-R 'ActiveStore' | Where-Object { Test-TsRoute $_ }).Count -gt 0) {
    Log '[!!]' "Tailscale 路由又回来了！请在 Tailscale 中关闭「Use Tailscale subnets」，或执行: tailscale set --accept-routes=false"
    if ($rc -eq 0) { $rc = 5 }
}
exit $rc
