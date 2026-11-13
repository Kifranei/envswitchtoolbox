using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EnvSwitchToolbox;

public sealed class MainWindow : Window
{
	public static readonly List<Window> TextWindows = new List<Window>();

	private AppConfig _cfg;

	private readonly Grid _root;

	private readonly DispatcherQueue _dq;

	private readonly ListView _list;

	private readonly TextBlock _details;

	private readonly TextBlock _status;

	private readonly LogView _log;

	private readonly InfoBar _adminBar;

	private readonly List<Control> _actionButtons = new List<Control>();

	private readonly List<Control> _profileButtons = new List<Control>();

	private readonly List<RadioMenuFlyoutItem> _themeItems = new List<RadioMenuFlyoutItem>();

	private bool _busy;

	private bool _closeConfirmed;

	private SiteProfile Selected => _list.SelectedIndex >= 0 && _cfg != null && _list.SelectedIndex < _cfg.Profiles.Count ? _cfg.Profiles[_list.SelectedIndex] : null;

	public MainWindow()
	{
		_dq = DispatcherQueue.GetForCurrentThread();
		bool admin = Program.IsAdmin();
		string title = Program.AppTitle + (admin ? "（管理员）" : "（未提权！）");
		Title = title;
		_cfg = ConfigStore.Load(out string warning);
		_cfg.Theme = WindowChrome.NormalizeTheme(_cfg.Theme);

		_root = new Grid();
		_root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		_root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		_root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		_root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		Grid titleBar = WindowChrome.BuildTitleBar(title, out _);
		_root.Children.Add(titleBar);

		// ---------- 顶部工具栏 ----------
		CommandBar bar = new CommandBar
		{
			DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
			Margin = new Thickness(4, 0, 4, 0)
		};
		bar.PrimaryCommands.Add(Cmd("查看当前 hosts", "\uE8A5", ViewHosts, null));
		bar.PrimaryCommands.Add(Cmd("打开 hosts 文件", "\uE70F", OpenHosts, null));
		bar.PrimaryCommands.Add(Cmd("查看路由 (route print -4)", "\uE968", ViewRoutes, _actionButtons));
		bar.PrimaryCommands.Add(Cmd("Ping 测试", "\uE9D9", DoPing, _actionButtons));
		bar.PrimaryCommands.Add(new AppBarSeparator());
		bar.PrimaryCommands.Add(Cmd("打开配置目录", "\uE838", OpenConfigDir, null));
		bar.PrimaryCommands.Add(Cmd("清空日志", "\uE894", () => _log.Clear(), null));
		AppBarButton theme = new AppBarButton { Label = "主题", Icon = new FontIcon { Glyph = "\uE790" } };
		ToolTipService.SetToolTip(theme, "主题：跟随系统 / 浅色 / 深色");
		MenuFlyout tf = new MenuFlyout();
		foreach ((string key, string label) in new[] { ("system", "跟随系统"), ("light", "浅色"), ("dark", "深色") })
		{
			RadioMenuFlyoutItem it = new RadioMenuFlyoutItem { Text = label, GroupName = "theme", Tag = key };
			it.Click += (s, e) => SetTheme(key);
			tf.Items.Add(it);
			_themeItems.Add(it);
		}
		theme.Flyout = tf;
		bar.PrimaryCommands.Add(theme);
		Grid.SetRow(bar, 1);
		_root.Children.Add(bar);

		// ---------- 未提权提示 ----------
		_adminBar = new InfoBar
		{
			Severity = InfoBarSeverity.Error,
			Title = "未以管理员身份运行",
			Message = "修改 hosts / 路由需要管理员权限，当前为受限模式，修改操作会失败。",
			IsClosable = false,
			IsOpen = !admin,
			Margin = new Thickness(16, 0, 16, 8)
		};
		Button elevate = new Button { Content = "以管理员身份重新启动" };
		elevate.Click += async (s, e) =>
		{
			Program.ReleaseMutex();
			if (Program.RestartElevated(out string err))
			{
				_closeConfirmed = true;
				Close();
			}
			else
			{
				await Dialogs.Message("提权失败：" + err);
			}
		};
		_adminBar.ActionButton = elevate;
		Grid.SetRow(_adminBar, 2);
		_root.Children.Add(_adminBar);

		// ---------- 主体 ----------
		Grid body = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 4, 16, 16) };
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 220 });
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		Grid.SetRow(body, 3);
		_root.Children.Add(body);

		// 左：地点列表
		Grid left = new Grid { RowSpacing = 8 };
		left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		left.Children.Add(X.Header("地点（项目现场）"));
		_list = new ListView { SelectionMode = ListViewSelectionMode.Single, IsDoubleTapEnabled = true, Margin = new Thickness(-4, 0, -4, 0) };
		_list.SelectionChanged += (s, e) => UpdateDetails();
		_list.DoubleTapped += (s, e) => EditProfile();
		Grid.SetRow(_list, 1);
		left.Children.Add(_list);
		Grid pbtns = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
		for (int i = 0; i < 3; i++)
		{
			pbtns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		}
		pbtns.RowDefinitions.Add(new RowDefinition());
		pbtns.RowDefinitions.Add(new RowDefinition());
		(string text, string glyph, Action a)[] pb =
		{
			("新增", "\uE710", AddProfile), ("编辑", "\uE70F", EditProfile), ("复制", "\uE8C8", CopyProfile),
			("删除", "\uE74D", DeleteProfile), ("导入…", "\uE8B5", ImportProfiles), ("导出…", "\uEDE1", ExportProfiles)
		};
		for (int i = 0; i < pb.Length; i++)
		{
			(string text, string glyph, Action a) = pb[i];
			Button b = X.Btn(text, (s, e) => a(), glyph);
			b.HorizontalAlignment = HorizontalAlignment.Stretch;
			Grid.SetRow(b, i / 3);
			Grid.SetColumn(b, i % 3);
			pbtns.Children.Add(b);
			_profileButtons.Add(b);
		}
		Grid.SetRow(pbtns, 2);
		left.Children.Add(pbtns);
		Border leftCard = X.Card();
		leftCard.Child = left;
		body.Children.Add(leftCard);

		// 右：详情、状态、操作、日志
		Grid right = new Grid { RowSpacing = 12 };
		right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 160 });
		Grid.SetColumn(right, 1);
		body.Children.Add(right);

		StackPanel detailSp = new StackPanel { Spacing = 6 };
		detailSp.Children.Add(X.Header("当前选中地点"));
		_details = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, LineHeight = 22 };
		detailSp.Children.Add(_details);
		Border detailCard = X.Card();
		detailCard.Child = new ScrollViewer { Content = detailSp, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
		right.Children.Add(detailCard);

		StackPanel ops = new StackPanel { Spacing = 10 };
		Grid statusRow = new Grid { ColumnSpacing = 10 };
		statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		_status = new TextBlock { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
		statusRow.Children.Add(_status);
		Button refresh = X.Btn("刷新状态", (s, e) => RefreshStatus(), "\uE72C");
		Grid.SetColumn(refresh, 1);
		statusRow.Children.Add(refresh);
		TextBlock adminText = new TextBlock
		{
			Text = admin ? "✔ 已以管理员身份运行" : "✘ 未以管理员身份运行，修改操作会失败",
			Foreground = new SolidColorBrush(admin ? Windows.UI.Color.FromArgb(255, 0x10, 0x9A, 0x3C) : Windows.UI.Color.FromArgb(255, 0xD1, 0x34, 0x38)),
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(adminText, 2);
		statusRow.Children.Add(adminText);
		ops.Children.Add(statusRow);
		StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		Button all = X.Btn("一键切换（hosts+路由）", (s, e) => DoSwitchAll(), "\uE8AB", accent: true);
		actions.Children.Add(all);
		_actionButtons.Add(all);
		foreach ((string text, string glyph, Action a) in new (string, string, Action)[] { ("仅切换 hosts", "\uE774", DoHostsOnly), ("仅修复路由", "\uE90F", DoRouteOnly), ("删除修复路由", "\uE74D", DoRouteDelete) })
		{
			Button b = X.Btn(text, (s, e) => a(), glyph);
			actions.Children.Add(b);
			_actionButtons.Add(b);
		}
		ops.Children.Add(new ScrollViewer { Content = actions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled });
		Grid.SetRow(ops, 1);
		right.Children.Add(ops);

		Grid logGrid = new Grid();
		logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		TextBlock logHead = X.Header("运行日志");
		logHead.Margin = new Thickness(14, 10, 14, 0);
		logGrid.Children.Add(logHead);
		_log = new LogView("Microsoft YaHei UI", 13, "HH:mm:ss");
		Grid.SetRow(_log.Element, 1);
		logGrid.Children.Add(_log.Element);
		Border logCard = X.Card(0, 0);
		logCard.Child = logGrid;
		Grid.SetRow(logCard, 2);
		right.Children.Add(logCard);

		Content = _root;
		Dialogs.Root = _root;
		Dialogs.DefaultTitle = Program.AppTitle;
		WindowChrome.Setup(this, _root, titleBar, _cfg.Theme, 1000, 720, 820, 560);
		SyncThemeMenu();

		AppWindow.Closing += OnClosing;
		Closed += (s, e) =>
		{
			foreach (Window w in TextWindows.ToList())
			{
				try
				{
					w.Close();
				}
				catch
				{
				}
			}
			Application.Current.Exit();
		};

		ReloadList(0);
		Log("配置文件：" + ConfigStore.FilePath, LogLevel.Detail);
		if (warning != null)
		{
			Log(warning, LogLevel.Warn);
		}
		if (!admin)
		{
			Log("当前未以管理员身份运行，修改 hosts/路由会失败。请点上方「以管理员身份重新启动」。", LogLevel.Error);
		}
		if (_cfg.Profiles.Count == 0)
		{
			Log("还没有任何地点：点左下「新增」填写，或「导入…」一个 JSON 配置（示例见仓库 examples/profiles.example.json）。", LogLevel.Info);
		}
		RefreshStatus();
	}

	private static AppBarButton Cmd(string label, string glyph, Action a, List<Control> group)
	{
		AppBarButton b = new AppBarButton { Label = label, Icon = new FontIcon { Glyph = glyph } };
		b.Click += (s, e) => a();
		group?.Add(b);
		return b;
	}

	// ---------- 主题 ----------

	private void SetTheme(string theme)
	{
		_cfg.Theme = WindowChrome.NormalizeTheme(theme);
		WindowChrome.ApplyTheme(_root, _cfg.Theme);
		foreach (Window w in TextWindows)
		{
			if (w is TextViewWindow tv)
			{
				WindowChrome.ApplyTheme(tv.Root, _cfg.Theme);
			}
		}
		SyncThemeMenu();
		SaveConfig();
	}

	private void SyncThemeMenu()
	{
		foreach (RadioMenuFlyoutItem it in _themeItems)
		{
			it.IsChecked = (string)it.Tag == _cfg.Theme;
		}
	}

	// ---------- 日志 ----------

	private void Log(string msg, LogLevel level)
	{
		if (!_dq.HasThreadAccess)
		{
			_dq.TryEnqueue(() => Log(msg, level));
			return;
		}
		_log.Append(msg, level.ToString());
	}

	public void LogError(string msg)
	{
		Log(msg, LogLevel.Error);
	}

	// ---------- 地点列表 ----------

	private void ReloadList(int selectIndex)
	{
		_list.Items.Clear();
		foreach (SiteProfile p in _cfg.Profiles)
		{
			_list.Items.Add(new TextBlock { Text = p.Name, FontSize = 15, Margin = new Thickness(0, 4, 0, 4) });
		}
		if (_list.Items.Count > 0)
		{
			_list.SelectedIndex = Math.Max(0, Math.Min(selectIndex, _list.Items.Count - 1));
		}
		UpdateDetails();
	}

	private void UpdateDetails()
	{
		SiteProfile p = Selected;
		if (p == null)
		{
			_details.Text = "（未选择地点，请在左侧选择或新增）";
			return;
		}
		int prefix = NetUtil.MaskToPrefix(p.Mask);
		string hosts = p.Hosts.Count == 0 ? "      （无）" : string.Join("\n", p.Hosts.Select(h => "      " + h.Ip + "  " + h.Domain));
		_details.Text = string.Format("名称：{0}\n内网网段：{1} / {2}（/{3}）\nhosts 条目：\n{4}\nPing 测试 IP：{5}    兜底网关：{6}    网卡关键字：{7}",
			p.Name, p.Network, p.Mask, prefix < 0 ? "?" : prefix.ToString(), hosts,
			string.IsNullOrEmpty(p.PingIp) ? "（无）" : p.PingIp,
			string.IsNullOrEmpty(p.FallbackGateway) ? "（空，自动读取网卡网关）" : p.FallbackGateway,
			p.AdapterKeyword);
	}

	private bool SaveConfig()
	{
		try
		{
			ConfigStore.Save(_cfg);
			return true;
		}
		catch (Exception ex)
		{
			Log("保存配置失败：" + ex.Message, LogLevel.Error);
			_ = Dialogs.Message("保存配置失败：" + ex.Message);
			return false;
		}
	}

	private IEnumerable<string> OtherNames(SiteProfile except)
	{
		return _cfg.Profiles.Where(x => x != except).Select(x => x.Name);
	}

	private async Task<SiteProfile> EditDialog(SiteProfile p, IEnumerable<string> others, string title)
	{
		ProfileEditDialog d = new ProfileEditDialog(p, others, title);
		if (await Dialogs.ShowAsync(d.Dialog) != ContentDialogResult.Primary)
		{
			return null;
		}
		return d.Result;
	}

	private async void AddProfile()
	{
		if (_busy)
		{
			return;
		}
		SiteProfile r = await EditDialog(new SiteProfile { Mask = "255.255.255.0", AdapterKeyword = "Realtek" }, OtherNames(null), "新增地点");
		if (r == null)
		{
			return;
		}
		_cfg.Profiles.Add(r);
		if (SaveConfig())
		{
			Log("已新增地点：" + r.Name, LogLevel.Ok);
		}
		ReloadList(_cfg.Profiles.Count - 1);
		RefreshStatus();
	}

	private async void CopyProfile()
	{
		SiteProfile sel = Selected;
		if (sel == null || _busy)
		{
			return;
		}
		SiteProfile c = sel.Clone();
		c.Name = sel.Name + " - 副本";
		SiteProfile r = await EditDialog(c, OtherNames(null), "复制地点");
		if (r == null)
		{
			return;
		}
		_cfg.Profiles.Add(r);
		if (SaveConfig())
		{
			Log("已新增地点：" + r.Name, LogLevel.Ok);
		}
		ReloadList(_cfg.Profiles.Count - 1);
	}

	private async void EditProfile()
	{
		SiteProfile sel = Selected;
		if (sel == null || _busy)
		{
			return;
		}
		int idx = _list.SelectedIndex;
		SiteProfile r = await EditDialog(sel.Clone(), OtherNames(sel), "编辑地点 - " + sel.Name);
		if (r == null)
		{
			return;
		}
		_cfg.Profiles[idx] = r;
		if (SaveConfig())
		{
			Log("已保存地点：" + r.Name, LogLevel.Ok);
		}
		ReloadList(idx);
		RefreshStatus();
	}

	private async void DeleteProfile()
	{
		SiteProfile sel = Selected;
		if (sel == null || _busy)
		{
			return;
		}
		if (!await Dialogs.Confirm("确定删除地点「" + sel.Name + "」吗？", null, "删除", "取消", defaultNo: true))
		{
			return;
		}
		int idx = _list.SelectedIndex;
		_cfg.Profiles.Remove(sel);
		if (SaveConfig())
		{
			Log("已删除地点：" + sel.Name, LogLevel.Warn);
		}
		ReloadList(idx);
		RefreshStatus();
	}

	private async void ImportProfiles()
	{
		if (_busy)
		{
			return;
		}
		string file = FileDialogs.Open(WindowChrome.Hwnd(this), "导入地点配置", "JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*");
		if (file == null)
		{
			return;
		}
		AppConfig imported;
		try
		{
			imported = ConfigStore.ReadFile(file);
			if (imported == null)
			{
				throw new Exception("文件中没有 profiles 数组");
			}
		}
		catch (Exception ex)
		{
			await Dialogs.Message("导入失败：" + ex.Message);
			return;
		}
		int added = 0, replaced = 0, skipped = 0;
		foreach (SiteProfile p in imported.Profiles)
		{
			string err = NetUtil.Validate(p);
			if (err != null)
			{
				Log("跳过无效地点「" + p.Name + "」：" + err, LogLevel.Warn);
				skipped++;
				continue;
			}
			int idx = _cfg.Profiles.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.Ordinal));
			if (idx >= 0)
			{
				_cfg.Profiles[idx] = p;
				replaced++;
			}
			else
			{
				_cfg.Profiles.Add(p);
				added++;
			}
		}
		if (SaveConfig())
		{
			Log($"已导入：新增 {added} 个，覆盖同名 {replaced} 个，跳过 {skipped} 个", LogLevel.Ok);
		}
		ReloadList(0);
		RefreshStatus();
	}

	private void ExportProfiles()
	{
		string file = FileDialogs.Save(WindowChrome.Hwnd(this), "导出地点配置", "JSON 配置 (*.json)|*.json", "profiles.json", "json");
		if (file == null)
		{
			return;
		}
		try
		{
			ConfigStore.WriteFile(_cfg, file);
			Log("已导出到 " + file, LogLevel.Ok);
		}
		catch (Exception ex)
		{
			Log("导出失败：" + ex.Message, LogLevel.Error);
		}
	}

	// ---------- 状态 / 操作 ----------

	private void RefreshStatus()
	{
		if (_cfg == null)
		{
			return;
		}
		List<SiteProfile> profiles = _cfg.Profiles.Select(p => p.Clone()).ToList();
		_status.Text = "当前 hosts 指向：检测中...";
		Task.Run(() => HostsManager.DetectCurrent(profiles)).ContinueWith(t =>
		{
			string text = "当前 hosts 指向：" + (t.Exception != null ? ("检测失败 " + t.Exception.InnerException?.Message) : t.Result);
			_dq.TryEnqueue(() => _status.Text = text);
		});
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		foreach (Control c in _actionButtons.Concat(_profileButtons))
		{
			c.IsEnabled = !busy;
		}
		_list.IsEnabled = !busy;
	}

	private async void RunOp(string title, Action<SiteProfile, List<SiteProfile>, LogFn> work, bool needProfile = true)
	{
		if (_busy)
		{
			return;
		}
		SiteProfile sel = Selected;
		if (needProfile && sel == null)
		{
			await Dialogs.Message("请先在左侧选择一个地点。");
			return;
		}
		SiteProfile snap = sel?.Clone();
		List<SiteProfile> all = _cfg.Profiles.Select(x => x.Clone()).ToList();
		SetBusy(true);
		Log("========== " + title + (snap != null ? ("：" + snap.Name) : "") + " ==========", LogLevel.Info);
		try
		{
			await Task.Run(() => work(snap, all, Log));
		}
		catch (Exception ex)
		{
			Log("失败：" + ex.Message, LogLevel.Error);
		}
		finally
		{
			SetBusy(false);
			RefreshStatus();
		}
	}

	private static void HostsStep(SiteProfile p, List<SiteProfile> all, LogFn log)
	{
		log("[hosts] 切换到「" + p.Name + "」", LogLevel.Info);
		if (p.Hosts.Count == 0)
		{
			log("该地点没有 hosts 条目：将只清除其它地点的相关条目", LogLevel.Warn);
		}
		HostsManager.Switch(p, all, log);
		log("[hosts] 完成", LogLevel.Ok);
	}

	private static void RouteStep(SiteProfile p, LogFn log)
	{
		log("[路由] 修复 " + p.Network + " / " + p.Mask, LogLevel.Info);
		RouteManager.Fix(p, log);
	}

	private void DoSwitchAll()
	{
		RunOp("一键切换", (p, all, log) =>
		{
			try
			{
				HostsStep(p, all, log);
			}
			catch (Exception ex)
			{
				log("[hosts] 失败：" + ex.Message, LogLevel.Error);
				log("继续执行路由修复...", LogLevel.Warn);
			}
			RouteStep(p, log);
			log("========== 完成：上网走 WiFi / 内网走有线 ==========", LogLevel.Ok);
		});
	}

	private void DoHostsOnly()
	{
		RunOp("仅切换 hosts", (p, all, log) =>
		{
			HostsStep(p, all, log);
			RouteManager.PingTest(p.PingIp, log);
		});
	}

	private void DoRouteOnly()
	{
		RunOp("仅修复路由", (p, all, log) => RouteStep(p, log));
	}

	private async void DoRouteDelete()
	{
		SiteProfile sel = Selected;
		if (sel == null)
		{
			await Dialogs.Message("请先在左侧选择一个地点。");
			return;
		}
		string text = $"将删除地点「{sel.Name}」网段 {sel.Network}/{NetUtil.MaskToPrefix(sel.Mask)} 上由本工具「修复路由」写过的路由（含持久路由和注册表 PersistentRoutes），\n并恢复有线网卡 AutomaticMetric；若 Tailscale 在线且该网段路由已不存在，会补回 via 100.100.100.100 的 ActiveStore 路由。\n\n直连路由仅在有线网卡仍位于该网段时保留。\n\n确定继续？";
		if (!await Dialogs.Confirm(text, null, "继续", "取消", defaultNo: true))
		{
			return;
		}
		RunOp("删除修复路由", (p, all, log) =>
		{
			log("[路由] 删除修复路由 " + p.Network + " / " + p.Mask, LogLevel.Info);
			RouteManager.Delete(p, log);
		});
	}

	private void DoPing()
	{
		RunOp("Ping 测试", (p, all, log) => RouteManager.PingTest(p.PingIp, log));
	}

	private void ViewHosts()
	{
		try
		{
			string text = HostsManager.ReadText(HostsManager.HostsPath, out _);
			new TextViewWindow("当前 hosts - " + HostsManager.HostsPath, text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"), _cfg.Theme).Activate();
		}
		catch (Exception ex)
		{
			Log("读取 hosts 失败：" + ex.Message, LogLevel.Error);
		}
	}

	private void OpenHosts()
	{
		try
		{
			Process.Start(ProcRunner.SysExe("notepad.exe"), "\"" + HostsManager.HostsPath + "\"");
			Log("已用记事本打开 hosts（以管理员身份，可直接保存）", LogLevel.Detail);
		}
		catch (Exception ex)
		{
			Log("打开 hosts 失败：" + ex.Message, LogLevel.Error);
		}
	}

	private void OpenConfigDir()
	{
		try
		{
			Directory.CreateDirectory(ConfigStore.Dir);
			Process.Start("explorer.exe", "\"" + ConfigStore.Dir + "\"");
		}
		catch (Exception ex)
		{
			Log("打开配置目录失败：" + ex.Message, LogLevel.Error);
		}
	}

	private async void ViewRoutes()
	{
		if (_busy)
		{
			return;
		}
		SetBusy(true);
		try
		{
			Log("执行 route print -4 ...", LogLevel.Detail);
			string text = await Task.Run(() => RouteManager.RoutePrint());
			new TextViewWindow("路由表 (route print -4)", text, _cfg.Theme).Activate();
		}
		catch (Exception ex)
		{
			Log("route print 失败：" + ex.Message, LogLevel.Error);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
	{
		if (!_busy || _closeConfirmed)
		{
			return;
		}
		e.Cancel = true;
		if (await Dialogs.Confirm("操作仍在进行中，确定要退出吗？", null, "退出", "取消", defaultNo: true))
		{
			_closeConfirmed = true;
			Close();
		}
	}
}
