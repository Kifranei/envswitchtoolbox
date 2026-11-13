# EnvSwitchToolbox（环境切换工具箱）

一个 Windows 小工具：在多个「地点」（不同内网环境）之间一键切换 **hosts 解析** 和 **内网路由**。

常见场景是笔记本同时连着两张网卡：WiFi 用来上网，有线网卡（或专线）接客户/机房内网。本工具让上网流量继续走 WiFi，只把指定内网网段的流量引到有线网卡上。同一批域名在不同地点要解析到不同 IP 时，也可以一键改好 hosts。

## 版本

| 版本 | 界面 | 运行环境 | 源码 |
|---|---|---|---|
| **2.0.0**（当前） | WinUI 3（Fluent 设计） | Windows 10 1809+ / Windows 11，x64 | `src/` |
| 1.0.0 | WinForms | Windows 7 SP1 / 10 / 11，.NET Framework 4.7.2+ | `legacy/winforms/` |

- 2.0.0 用 WinUI 3（Windows App SDK）重写，功能与 1.0.0 相同；**Win11 上使用云母（Mica）背景，Win10 上使用亚克力（Acrylic）背景**，两者都不支持时退回纯色背景。
- 支持 **浅色 / 深色 / 跟随系统** 三种主题（默认跟随系统），在命令栏的「主题」按钮里切换，标题栏按钮颜色随主题变化，云母 / 亚克力背景在两种主题下都会正确着色。主题保存在 `profiles.json` 的 `theme` 字段中。
- 配置文件格式与 1.0.0 兼容：1.0.0 的 `profiles.json` 可直接被 2.0.0 读取；2.0.0 只多写一个 `theme` 字段，1.0.0 读取时会忽略它。导入 / 导出的 JSON 格式不变。
- 1.0.0 的 WinForms 版保留在 `legacy/winforms/`，供 Windows 7 等不支持 WinUI 3 的旧系统使用。

> 开源版不内置任何地点，首次运行配置为空。请自己「新增」，或导入 [`examples/profiles.example.json`](examples/profiles.example.json) 改成你的环境。

## 功能

- **地点管理**：新增、编辑、复制、删除，支持 JSON 导入和导出。每个地点包含：
  - 名称
  - 内网网段和子网掩码，例如 `192.0.2.0 / 255.255.255.0`
  - hosts 条目（域名 → IP）
  - Ping 测试 IP（可空）
  - 兜底网关（可空，网卡上没配网关时使用）
  - 网卡关键字（匹配网卡描述，默认 `Realtek`）
- **仅切换 hosts**：
  1. 先把 hosts 备份为 `hosts.bak`。
  2. 删除所有地点里出现过的域名所在的行，以及旧的 `# ENVSWITCH` 标记。
  3. 在原位置写入当前地点的条目（UTF-8 无 BOM，CRLF）。
  4. 执行 `ipconfig /flushdns`。
- **仅修复路由**：通过内置 PowerShell 脚本完成以下几步。
  1. 按关键字找到有线网卡，把它的接口跃点数设为 500，让默认流量继续走 WiFi。
  2. 读取网卡网关；读不到就用兜底网关。
  3. 清掉被 Tailscale 子网路由（下一跳 `100.100.100.100`）抢走的同网段路由。
  4. 添加持久路由：`route -p add <网段> mask <掩码> <网关> metric 1 if <网卡>`。如果有线网卡本身就在该网段，则只清理冲突路由。
  5. 验证路由是否生效，再做一次 Ping 测试。
- **一键切换**：依次执行上面两步，即切换 hosts 并修复路由。
- **删除修复路由**：撤销「修复路由」，包括持久路由和注册表 `PersistentRoutes` 里的残留，并恢复网卡的自动跃点数。若 Tailscale 在线，还会把该网段的路由交回给 Tailscale。
- **辅助功能**：
  - 查看或打开 hosts
  - `route print -4`
  - Ping 测试
  - 打开配置目录
  - 在状态栏检测当前 hosts 指向哪个地点

## 使用

1. 以**管理员身份**运行 `EnvSwitchToolbox.exe`。修改 hosts 和路由都需要管理员权限，程序清单要求管理员权限，启动时会弹出 UAC 提示；若以普通权限运行，窗口顶部会提示并提供「以管理员身份重新启动」按钮。
2. 点「新增」填写地点，或点「导入…」选择一个 JSON 配置。
3. 在左侧选中地点，点「一键切换（hosts+路由）」。

配置保存在 `%APPDATA%\EnvSwitchToolbox\profiles.json`，格式见 `examples/profiles.example.json`。

> 注意：
> - 部分安全软件（Defender、360、火绒等）会拦截对 hosts 的修改，需要放行。
> - 若 Tailscale 不断重新抢占网段，可执行 `tailscale set --accept-routes=false`。

## 构建

### 2.0.0（WinUI 3）

需要 Windows 和 .NET SDK 10.0：

```bash
dotnet publish src/EnvSwitchToolbox.csproj -c Release -o out
# 产物：out/EnvSwitchToolbox.exe（x64 自包含单文件，内含 .NET 10 与 Windows App SDK 运行库，无需另外安装）
```

首次启动时运行库会解压到 `%TEMP%\.net\EnvSwitchToolbox\`。

### 1.0.0（WinForms，旧系统用）

.NET Framework 4.7.2 的 WinForms 程序（SDK 风格 csproj），运行时只需要 Windows 自带的 .NET Framework 4.7.2 或更高版本。

```bash
# 需要 .NET SDK 6.0 及以上（Windows / Linux / macOS 都能编译）
dotnet build legacy/winforms/EnvSwitchToolbox.csproj -c Release -o out-legacy
# 产物：out-legacy/EnvSwitchToolbox.exe
```

在 Linux 或 macOS 上编译时，会通过 `Microsoft.NETFramework.ReferenceAssemblies` 包取得 net472 引用程序集，不需要安装 Windows SDK。

## 目录结构

```
src/                     2.0.0 WinUI 3 版
  EnvSwitchToolbox.csproj
  Core/                  hosts、路由、配置等（与界面无关）
  UI/                    窗口、对话框、主题与背景材质
  Scripts/               内嵌的 PowerShell 路由脚本
  app.ico / app.manifest（manifest 要求管理员权限）
legacy/winforms/         1.0.0 WinForms 版
examples/profiles.example.json
```

## 许可证

[MIT](LICENSE) © 2026 Kifranei
