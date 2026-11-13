using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EnvSwitchToolbox;

public class MainForm : Form
{
	private AppConfig _cfg;

	private ListBox _list;

	private Label _details;

	private Label _status;

	private Label _adminLabel;

	private RichTextBox _log;

	private SplitContainer _split;

	private readonly List<Button> _actionButtons = new List<Button>();

	private readonly List<Button> _profileButtons = new List<Button>();

	private bool _busy;

	private bool _splitInit;

	private static Icon _appIcon;

	public static Icon AppIcon
	{
		get
		{
			if (_appIcon == null)
			{
				try
				{
					Assembly executingAssembly = Assembly.GetExecutingAssembly();
					string text = executingAssembly.GetManifestResourceNames().FirstOrDefault((string n) => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
					if (text != null)
					{
						using Stream stream = executingAssembly.GetManifestResourceStream(text);
						_appIcon = new Icon(stream);
					}
				}
				catch
				{
				}
				if (_appIcon == null)
				{
					_appIcon = SystemIcons.Application;
				}
			}
			return _appIcon;
		}
	}

	private SiteProfile Selected => _list.SelectedItem as SiteProfile;

	private int S(int v)
	{
		return (int)Math.Round((double)(v * ((Control)this).DeviceDpi) / 96.0);
	}

	public MainForm()
	{
		((Control)this).SuspendLayout();
		((ContainerControl)this).AutoScaleDimensions = new SizeF(96f, 96f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).Text = "环境切换工具箱" + (Program.IsAdmin() ? "（管理员）" : "（未提权！）");
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Form)this).ClientSize = new Size(1000, 680);
		((Control)this).MinimumSize = new Size(820, 560);
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).Icon = AppIcon;
		_split = new SplitContainer
		{
			Dock = (DockStyle)5,
			Orientation = (Orientation)1,
			SplitterWidth = 6
		};
		((Control)this).Controls.Add((Control)(object)_split);
		((Control)_split.Panel1).Padding = new Padding(8, 6, 0, 8);
		GroupBox val = new GroupBox
		{
			Text = "地点（项目现场）",
			Dock = (DockStyle)5,
			Padding = new Padding(6, 4, 6, 6)
		};
		_list = new ListBox
		{
			Dock = (DockStyle)5,
			IntegralHeight = false,
			FormattingEnabled = true,
			Font = new Font("Microsoft YaHei UI", 10.5f)
		};
		((ListControl)_list).Format += delegate(object s, ListControlConvertEventArgs e)
		{
			if (e.ListItem is SiteProfile siteProfile)
			{
				((ConvertEventArgs)e).Value = siteProfile.Name;
			}
		};
		_list.SelectedIndexChanged += delegate
		{
			UpdateDetails();
		};
		((Control)_list).DoubleClick += delegate
		{
			EditProfile();
		};
		((Control)val).Controls.Add((Control)(object)_list);
		FlowLayoutPanel val3 = new FlowLayoutPanel
		{
			Dock = (DockStyle)2,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			WrapContents = true,
			Padding = new Padding(0, 4, 0, 0)
		};
		((Control)val3).Controls.Add((Control)(object)MakeButton("新增", delegate
		{
			AddProfile();
		}, _profileButtons));
		((Control)val3).Controls.Add((Control)(object)MakeButton("编辑", delegate
		{
			EditProfile();
		}, _profileButtons));
		((Control)val3).Controls.Add((Control)(object)MakeButton("复制", delegate
		{
			CopyProfile();
		}, _profileButtons));
		((Control)val3).Controls.Add((Control)(object)MakeButton("删除", delegate
		{
			DeleteProfile();
		}, _profileButtons));
		((Control)val3).Controls.Add((Control)(object)MakeButton("导入…", delegate
		{
			ImportProfiles();
		}, _profileButtons));
		((Control)val3).Controls.Add((Control)(object)MakeButton("导出…", delegate
		{
			ExportProfiles();
		}, _profileButtons));
		((Control)val).Controls.Add((Control)(object)val3);
		((Control)_split.Panel1).Controls.Add((Control)(object)val);
		SplitterPanel panel = _split.Panel2;
		((Control)panel).Padding = new Padding(4, 6, 8, 8);
		_log = new RichTextBox
		{
			Dock = (DockStyle)5,
			ReadOnly = true,
			BackColor = Color.White,
			DetectUrls = false,
			Font = new Font("Microsoft YaHei UI", 9f),
			WordWrap = true,
			HideSelection = false
		};
		((Control)panel).Controls.Add((Control)(object)_log);
		((Control)panel).Controls.Add((Control)new Label
		{
			Text = "运行日志",
			AutoSize = true,
			Dock = (DockStyle)1,
			Padding = new Padding(0, 8, 0, 3)
		});
		FlowLayoutPanel val4 = new FlowLayoutPanel
		{
			Dock = (DockStyle)1,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			WrapContents = true
		};
		((Control)val4).Controls.Add((Control)(object)MakeButton("查看当前 hosts", delegate
		{
			ViewHosts();
		}, null));
		((Control)val4).Controls.Add((Control)(object)MakeButton("打开 hosts 文件", delegate
		{
			OpenHosts();
		}, null));
		((Control)val4).Controls.Add((Control)(object)MakeButton("查看路由 (route print -4)", delegate
		{
			ViewRoutes();
		}, _actionButtons));
		((Control)val4).Controls.Add((Control)(object)MakeButton("Ping 测试", delegate
		{
			DoPing();
		}, _actionButtons));
		((Control)val4).Controls.Add((Control)(object)MakeButton("打开配置目录", delegate
		{
			OpenConfigDir();
		}, null));
		((Control)val4).Controls.Add((Control)(object)MakeButton("清空日志", delegate
		{
			((TextBoxBase)_log).Clear();
		}, null));
		FlowLayoutPanel val5 = new FlowLayoutPanel
		{
			Dock = (DockStyle)1,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			WrapContents = true,
			Padding = new Padding(0, 4, 0, 0)
		};
		Button val6 = MakeButton("一键切换（hosts+路由）", delegate
		{
			DoSwitchAll();
		}, _actionButtons);
		((Control)val6).Font = new Font("Microsoft YaHei UI", 10.5f, (FontStyle)1);
		((Control)val6).ForeColor = Color.FromArgb(0, 100, 0);
		((Control)val5).Controls.Add((Control)(object)val6);
		((Control)val5).Controls.Add((Control)(object)MakeButton("仅切换 hosts", delegate
		{
			DoHostsOnly();
		}, _actionButtons));
		((Control)val5).Controls.Add((Control)(object)MakeButton("仅修复路由", delegate
		{
			DoRouteOnly();
		}, _actionButtons));
		Button val7 = MakeButton("删除修复路由", delegate
		{
			DoRouteDelete();
		}, _actionButtons);
		((Control)val7).ForeColor = Color.FromArgb(140, 60, 0);
		((Control)val5).Controls.Add((Control)(object)val7);
		FlowLayoutPanel val8 = new FlowLayoutPanel
		{
			Dock = (DockStyle)1,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			WrapContents = true,
			Padding = new Padding(0, 6, 0, 0)
		};
		_status = new Label
		{
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 10f, (FontStyle)1),
			ForeColor = Color.FromArgb(0, 90, 160),
			Margin = new Padding(3, 8, 12, 3)
		};
		_adminLabel = new Label
		{
			AutoSize = true,
			Margin = new Padding(3, 9, 3, 3)
		};
		((Control)val8).Controls.Add((Control)(object)_status);
		((Control)val8).Controls.Add((Control)(object)MakeButton("刷新状态", delegate
		{
			RefreshStatus();
		}, null));
		((Control)val8).Controls.Add((Control)(object)_adminLabel);
		_details = new Label
		{
			AutoSize = true,
			Dock = (DockStyle)1,
			BorderStyle = (BorderStyle)1,
			BackColor = Color.FromArgb(250, 251, 253),
			Padding = new Padding(8, 6, 8, 6),
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		Label val9 = new Label
		{
			Text = "当前选中地点",
			AutoSize = true,
			Dock = (DockStyle)1,
			Font = new Font("Microsoft YaHei UI", 9f, (FontStyle)1),
			Padding = new Padding(0, 0, 0, 3)
		};
		((Control)panel).Controls.Add((Control)(object)val4);
		((Control)panel).Controls.Add((Control)(object)val5);
		((Control)panel).Controls.Add((Control)(object)val8);
		((Control)panel).Controls.Add((Control)(object)_details);
		((Control)panel).Controls.Add((Control)(object)val9);
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
		((Form)this).Load += delegate
		{
			OnLoaded();
		};
		((Form)this).FormClosing += (FormClosingEventHandler)delegate(object s, FormClosingEventArgs e)
		{
			if (_busy && (int)MessageBox.Show("操作仍在进行中，确定要退出吗？", "环境切换工具箱", (MessageBoxButtons)4, (MessageBoxIcon)48) != 6)
			{
				((CancelEventArgs)(object)e).Cancel = true;
			}
		};
	}

	private Button MakeButton(string text, EventHandler onClick, List<Button> group)
	{
		Button val = new Button
		{
			Text = text,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			Padding = new Padding(8, 3, 8, 3),
			Margin = new Padding(3),
			UseVisualStyleBackColor = true
		};
		((Control)val).Click += onClick;
		group?.Add(val);
		return val;
	}

	private void InitSplit()
	{
		if (_splitInit)
		{
			return;
		}
		try
		{
			int num = S(210);
			int num2 = S(480);
			if (((Control)_split).Width > num + num2 + _split.SplitterWidth)
			{
				_split.Panel1MinSize = num;
				_split.Panel2MinSize = num2;
				_split.SplitterDistance = Math.Max(num, Math.Min(((Control)_split).Width - num2 - _split.SplitterWidth, S(270)));
			}
			_split.FixedPanel = (FixedPanel)1;
			_splitInit = true;
		}
		catch
		{
		}
	}

	private void OnLoaded()
	{
		((Control)this).MinimumSize = new Size(S(820), S(560));
		Rectangle workingArea = Screen.FromControl((Control)(object)this).WorkingArea;
		if (((Control)this).Height > workingArea.Height)
		{
			((Control)this).Top = workingArea.Top;
			((Control)this).Height = workingArea.Height;
		}
		if (((Control)this).Width > workingArea.Width)
		{
			((Control)this).Left = workingArea.Left;
			((Control)this).Width = workingArea.Width;
		}
		InitSplit();
		_cfg = ConfigStore.Load(out var warning);
		ReloadList(0);
		Log("配置文件：" + ConfigStore.FilePath, LogLevel.Detail);
		if (warning != null)
		{
			Log(warning, LogLevel.Warn);
		}
		if (Program.IsAdmin())
		{
			((Control)_adminLabel).Text = "✔ 已以管理员身份运行";
			((Control)_adminLabel).ForeColor = Color.Green;
		}
		else
		{
			((Control)_adminLabel).Text = "✘ 未以管理员身份运行，修改操作会失败";
			((Control)_adminLabel).ForeColor = Color.Red;
			Log("当前未以管理员身份运行，修改 hosts/路由会失败。请右键「以管理员身份运行」。", LogLevel.Error);
		}
		RefreshStatus();
	}

	private void Log(string msg, LogLevel level)
	{
		if (((Control)this).IsDisposed)
		{
			return;
		}
		if (((Control)this).InvokeRequired)
		{
			try
			{
				((Control)this).BeginInvoke((Delegate)(Action)delegate
				{
					Log(msg, level);
				});
				return;
			}
			catch
			{
				return;
			}
		}
		Color selectionColor = level switch
		{
			LogLevel.Ok => Color.FromArgb(0, 128, 0), 
			LogLevel.Warn => Color.FromArgb(200, 110, 0), 
			LogLevel.Error => Color.Red, 
			LogLevel.Detail => Color.Gray, 
			_ => Color.Black, 
		};
		string[] array = (msg ?? "").Replace("\r\n", "\n").Split(new char[1] { '\n' });
		foreach (string text in array)
		{
			((TextBoxBase)_log).SelectionStart = ((TextBoxBase)_log).TextLength;
			((TextBoxBase)_log).SelectionLength = 0;
			_log.SelectionColor = Color.DarkGray;
			((TextBoxBase)_log).AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
			((TextBoxBase)_log).SelectionStart = ((TextBoxBase)_log).TextLength;
			_log.SelectionColor = selectionColor;
			_log.SelectionFont = (Font)((level == LogLevel.Error) ? ((object)new Font(((Control)_log).Font, (FontStyle)1)) : ((object)((Control)_log).Font));
			((TextBoxBase)_log).AppendText(text + "\n");
		}
		((TextBoxBase)_log).SelectionStart = ((TextBoxBase)_log).TextLength;
		((TextBoxBase)_log).ScrollToCaret();
	}

	private void ReloadList(int selectIndex)
	{
		_list.BeginUpdate();
		_list.Items.Clear();
		foreach (SiteProfile profile in _cfg.Profiles)
		{
			_list.Items.Add((object)profile);
		}
		_list.EndUpdate();
		if (_list.Items.Count > 0)
		{
			((ListControl)_list).SelectedIndex = Math.Max(0, Math.Min(selectIndex, _list.Items.Count - 1));
		}
		UpdateDetails();
	}

	private void UpdateDetails()
	{
		SiteProfile selected = Selected;
		if (selected == null)
		{
			((Control)_details).Text = "（未选择地点，请在左侧选择或新增）";
			return;
		}
		int num = NetUtil.MaskToPrefix(selected.Mask);
		string text = ((selected.Hosts.Count == 0) ? "      （无）" : string.Join("\n", selected.Hosts.Select((HostEntry h) => "      " + h.Ip + "  " + h.Domain)));
		((Control)_details).Text = string.Format("名称：{0}{1}\n内网网段：{2} / {3}（/{4}）\nhosts 条目：\n{5}\nPing 测试 IP：{6}    兜底网关：{7}    网卡关键字：{8}", selected.Name, "", selected.Network, selected.Mask, (num < 0) ? "?" : num.ToString(), text, string.IsNullOrEmpty(selected.PingIp) ? "（无）" : selected.PingIp, string.IsNullOrEmpty(selected.FallbackGateway) ? "（空，自动读取网卡网关）" : selected.FallbackGateway, selected.AdapterKeyword);
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
			MessageBox.Show("保存配置失败：" + ex.Message, "环境切换工具箱", (MessageBoxButtons)0, (MessageBoxIcon)16);
			return false;
		}
	}

	private IEnumerable<string> OtherNames(SiteProfile except)
	{
		return from x in _cfg.Profiles
			where x != except
			select x.Name;
	}

	private void AddProfile()
	{
		ProfileEditForm profileEditForm = new ProfileEditForm(new SiteProfile
		{
			Mask = "255.255.255.0",
			AdapterKeyword = "Realtek"
		}, OtherNames(null), "新增地点");
		try
		{
			if ((int)((Form)profileEditForm).ShowDialog((IWin32Window)(object)this) == 1)
			{
				_cfg.Profiles.Add(profileEditForm.Result);
				if (SaveConfig())
				{
					Log("已新增地点：" + profileEditForm.Result.Name, LogLevel.Ok);
				}
				ReloadList(_cfg.Profiles.Count - 1);
				RefreshStatus();
			}
		}
		finally
		{
			((IDisposable)profileEditForm)?.Dispose();
		}
	}

	private void CopyProfile()
	{
		SiteProfile selected = Selected;
		if (selected == null)
		{
			return;
		}
		SiteProfile siteProfile = selected.Clone();
		siteProfile.Name = selected.Name + " - 副本";
		ProfileEditForm profileEditForm = new ProfileEditForm(siteProfile, OtherNames(null), "复制地点");
		try
		{
			if ((int)((Form)profileEditForm).ShowDialog((IWin32Window)(object)this) == 1)
			{
				_cfg.Profiles.Add(profileEditForm.Result);
				if (SaveConfig())
				{
					Log("已新增地点：" + profileEditForm.Result.Name, LogLevel.Ok);
				}
				ReloadList(_cfg.Profiles.Count - 1);
			}
		}
		finally
		{
			((IDisposable)profileEditForm)?.Dispose();
		}
	}

	private void EditProfile()
	{
		SiteProfile selected = Selected;
		if (selected == null)
		{
			return;
		}
		int selectedIndex = ((ListControl)_list).SelectedIndex;
		ProfileEditForm profileEditForm = new ProfileEditForm(selected.Clone(), OtherNames(selected), "编辑地点 - " + selected.Name);
		try
		{
			if ((int)((Form)profileEditForm).ShowDialog((IWin32Window)(object)this) == 1)
			{
				_cfg.Profiles[selectedIndex] = profileEditForm.Result;
				if (SaveConfig())
				{
					Log("已保存地点：" + profileEditForm.Result.Name, LogLevel.Ok);
				}
				ReloadList(selectedIndex);
				RefreshStatus();
			}
		}
		finally
		{
			((IDisposable)profileEditForm)?.Dispose();
		}
	}

	private void DeleteProfile()
	{
		SiteProfile selected = Selected;
		if (selected != null && (int)MessageBox.Show("确定删除地点「" + selected.Name + "」吗？", "环境切换工具箱", (MessageBoxButtons)4, (MessageBoxIcon)32) == 6)
		{
			int selectedIndex = ((ListControl)_list).SelectedIndex;
			_cfg.Profiles.Remove(selected);
			if (SaveConfig())
			{
				Log("已删除地点：" + selected.Name, LogLevel.Warn);
			}
			ReloadList(selectedIndex);
			RefreshStatus();
		}
	}

	private void ImportProfiles()
	{
		using OpenFileDialog dlg = new OpenFileDialog
		{
			Title = "导入地点配置",
			Filter = "JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*"
		};
		if ((int)((CommonDialog)dlg).ShowDialog((IWin32Window)(object)this) != 1)
		{
			return;
		}
		AppConfig imported;
		try
		{
			imported = ConfigStore.ReadFile(dlg.FileName);
			if (imported == null)
			{
				throw new Exception("文件中没有 profiles 数组");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show((IWin32Window)(object)this, "导入失败：" + ex.Message, "环境切换工具箱", (MessageBoxButtons)0, (MessageBoxIcon)16);
			return;
		}
		int added = 0;
		int replaced = 0;
		int skipped = 0;
		foreach (SiteProfile p in imported.Profiles)
		{
			string err = NetUtil.Validate(p);
			if (err != null)
			{
				Log("跳过无效地点「" + p.Name + "」：" + err, LogLevel.Warn);
				skipped++;
				continue;
			}
			int idx = _cfg.Profiles.FindIndex((SiteProfile x) => string.Equals(x.Name, p.Name, StringComparison.Ordinal));
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
		using SaveFileDialog dlg = new SaveFileDialog
		{
			Title = "导出地点配置",
			Filter = "JSON 配置 (*.json)|*.json",
			FileName = "profiles.json"
		};
		if ((int)((CommonDialog)dlg).ShowDialog((IWin32Window)(object)this) != 1)
		{
			return;
		}
		try
		{
			ConfigStore.WriteFile(_cfg, dlg.FileName);
			Log("已导出到 " + dlg.FileName, LogLevel.Ok);
		}
		catch (Exception ex)
		{
			Log("导出失败：" + ex.Message, LogLevel.Error);
		}
	}

	private void RefreshStatus()
	{
		if (_cfg == null)
		{
			return;
		}
		List<SiteProfile> profiles = _cfg.Profiles.Select((SiteProfile p) => p.Clone()).ToList();
		((Control)_status).Text = "当前 hosts 指向：检测中...";
		Task.Run(() => HostsManager.DetectCurrent(profiles)).ContinueWith(delegate(Task<string> t)
		{
			if (!((Control)this).IsDisposed)
			{
				((Control)_status).Text = "当前 hosts 指向：" + ((t.Exception != null) ? ("检测失败 " + t.Exception.InnerException.Message) : t.Result);
			}
		}, TaskScheduler.FromCurrentSynchronizationContext());
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		foreach (Button item in _actionButtons.Concat(_profileButtons))
		{
			((Control)item).Enabled = !busy;
		}
		((Control)_list).Enabled = !busy;
		((Control)this).UseWaitCursor = busy;
	}

	private async void RunOp(string title, Action<SiteProfile, List<SiteProfile>, LogFn> work, bool needProfile = true)
	{
		if (_busy)
		{
			return;
		}
		SiteProfile selected = Selected;
		if (needProfile && selected == null)
		{
			MessageBox.Show("请先在左侧选择一个地点。", "环境切换工具箱");
			return;
		}
		SiteProfile snap = selected?.Clone();
		List<SiteProfile> all = _cfg.Profiles.Select((SiteProfile x) => x.Clone()).ToList();
		SetBusy(busy: true);
		Log("========== " + title + ((snap != null) ? ("：" + snap.Name) : "") + " ==========", LogLevel.Info);
		try
		{
			await Task.Run(delegate
			{
				work(snap, all, Log);
			});
		}
		catch (Exception ex)
		{
			Log("失败：" + ex.Message, LogLevel.Error);
		}
		finally
		{
			SetBusy(busy: false);
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
		RunOp("一键切换", delegate(SiteProfile p, List<SiteProfile> all, LogFn log)
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
		RunOp("仅切换 hosts", delegate(SiteProfile p, List<SiteProfile> all, LogFn log)
		{
			HostsStep(p, all, log);
			RouteManager.PingTest(p.PingIp, log);
		});
	}

	private void DoRouteOnly()
	{
		RunOp("仅修复路由", delegate(SiteProfile p, List<SiteProfile> all, LogFn log)
		{
			RouteStep(p, log);
		});
	}

	private void DoRouteDelete()
	{
		SiteProfile selected = Selected;
		if (selected == null)
		{
			MessageBox.Show("请先在左侧选择一个地点。", "环境切换工具箱");
			return;
		}
		string text = $"将删除地点「{selected.Name}」网段 {selected.Network}/{NetUtil.MaskToPrefix(selected.Mask)} 上由本工具「修复路由」写过的路由（含持久路由和注册表 PersistentRoutes），\n并恢复有线网卡 AutomaticMetric；若 Tailscale 在线且该网段路由已不存在，会补回 via 100.100.100.100 的 ActiveStore 路由。\n\n直连路由仅在有线网卡仍位于该网段时保留。\n\n确定继续？";
		if ((int)MessageBox.Show((IWin32Window)(object)this, text, "环境切换工具箱", (MessageBoxButtons)4, (MessageBoxIcon)32, (MessageBoxDefaultButton)256) == 6)
		{
			RunOp("删除修复路由", delegate(SiteProfile p, List<SiteProfile> all, LogFn log)
			{
				log("[路由] 删除修复路由 " + p.Network + " / " + p.Mask, LogLevel.Info);
				RouteManager.Delete(p, log);
			});
		}
	}

	private void DoPing()
	{
		RunOp("Ping 测试", delegate(SiteProfile p, List<SiteProfile> all, LogFn log)
		{
			RouteManager.PingTest(p.PingIp, log);
		});
	}

	private void ViewHosts()
	{
		try
		{
			bool hadBom;
			string text = HostsManager.ReadText(HostsManager.HostsPath, out hadBom);
			((Form)new TextViewForm("当前 hosts - " + HostsManager.HostsPath, text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"))).Show((IWin32Window)(object)this);
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
		SetBusy(busy: true);
		try
		{
			Log("执行 route print -4 ...", LogLevel.Detail);
			((Form)new TextViewForm("路由表 (route print -4)", await Task.Run(() => RouteManager.RoutePrint()))).Show((IWin32Window)(object)this);
		}
		catch (Exception ex)
		{
			Log("route print 失败：" + ex.Message, LogLevel.Error);
		}
		finally
		{
			SetBusy(busy: false);
		}
	}
}
