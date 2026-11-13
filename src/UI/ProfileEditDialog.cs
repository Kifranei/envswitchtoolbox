using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EnvSwitchToolbox;

/// <summary>新增 / 编辑 / 复制地点。</summary>
public sealed class ProfileEditDialog
{
	/// <summary>实际显示的对话框（用组合而不是继承 ContentDialog，单文件发布时自定义 XAML 派生类型会导致崩溃）。</summary>
	public ContentDialog Dialog { get; } = new ContentDialog();

	private sealed class HostRow
	{
		public Grid Root;

		public TextBox Domain, Ip;
	}

	private readonly HashSet<string> _otherNames;

	private readonly TextBox _name, _net, _mask, _ping, _gw, _kw;

	private readonly StackPanel _rowsPanel;

	private readonly List<HostRow> _rows = new List<HostRow>();

	private readonly InfoBar _info;

	private string _netWarnedFor;

	public SiteProfile Result { get; private set; }

	public ProfileEditDialog(SiteProfile p, IEnumerable<string> otherNames, string title)
	{
		_otherNames = new HashSet<string>(otherNames, StringComparer.OrdinalIgnoreCase);
		Dialog.Title = title;
		Dialog.PrimaryButtonText = "保存";
		Dialog.CloseButtonText = "取消";
		Dialog.DefaultButton = ContentDialogButton.Primary;
		Dialog.Resources["ContentDialogMaxWidth"] = 760.0;

		Grid fields = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
		fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
		fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		int row = 0;
		TextBox Add(string label, string value, string hint)
		{
			fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			TextBlock l = X.Label(label);
			TextBox b = new TextBox { Text = value ?? "" };
			Grid.SetRow(l, row);
			Grid.SetRow(b, row);
			Grid.SetColumn(b, 1);
			fields.Children.Add(l);
			fields.Children.Add(b);
			if (hint != null)
			{
				TextBlock h = X.Secondary(hint, wrap: true);
				Grid.SetRow(h, row);
				Grid.SetColumn(h, 2);
				fields.Children.Add(h);
			}
			row++;
			return b;
		}
		_name = Add("名称", p.Name, null);
		_net = Add("内网网段", p.Network, "例：192.0.2.0");
		_mask = Add("子网掩码", p.Mask, "例：255.255.255.0");
		_ping = Add("Ping 测试 IP", p.PingIp, "可空");
		_gw = Add("兜底网关", p.FallbackGateway, "可空；网卡未配置网关时使用");
		_kw = Add("网卡关键字", p.AdapterKeyword, "匹配网卡描述，默认 Realtek");
		_net.TextChanged += (s, e) => _info.IsOpen = false;
		_mask.TextChanged += (s, e) => _info.IsOpen = false;

		StackPanel sp = new StackPanel { Spacing = 10, MinWidth = 600 };
		sp.Children.Add(fields);

		Grid hostsHead = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
		hostsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		hostsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		hostsHead.Children.Add(X.Header("hosts 条目（域名 → IP）"));
		StackPanel hb = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		hb.Children.Add(X.Btn("添加一行", (s, e) =>
		{
			HostRow r = AddRow("", _ping.Text.Trim());
			r.Domain.Focus(FocusState.Programmatic);
		}, "\uE710"));
		hb.Children.Add(X.Btn("IP 全部设为 Ping IP", (s, e) => FillIps(), "\uE8B3"));
		Grid.SetColumn(hb, 1);
		hostsHead.Children.Add(hb);
		sp.Children.Add(hostsHead);

		Grid colHead = RowGrid();
		TextBlock h1 = X.Secondary("域名");
		TextBlock h2 = X.Secondary("IP");
		Grid.SetColumn(h2, 1);
		colHead.Children.Add(h1);
		colHead.Children.Add(h2);
		sp.Children.Add(colHead);
		_rowsPanel = new StackPanel { Spacing = 6 };
		sp.Children.Add(new ScrollViewer { Content = _rowsPanel, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) });
		foreach (HostEntry h in p.Hosts ?? new List<HostEntry>())
		{
			AddRow(h.Domain, h.Ip);
		}
		_info = new InfoBar { IsOpen = false, IsClosable = true };
		sp.Children.Add(_info);
		Dialog.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
		Dialog.PrimaryButtonClick += OnSave;
	}

	private static Grid RowGrid()
	{
		Grid g = new Grid { ColumnSpacing = 6 };
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Star) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Star) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
		return g;
	}

	private HostRow AddRow(string domain, string ip)
	{
		HostRow r = new HostRow { Root = RowGrid(), Domain = new TextBox { Text = domain ?? "", MinWidth = 0 }, Ip = new TextBox { Text = ip ?? "", MinWidth = 0 } };
		Grid.SetColumn(r.Ip, 1);
		Button del = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Padding = new Thickness(8, 6, 8, 6) };
		ToolTipService.SetToolTip(del, "删除此行");
		del.Click += (s, e) =>
		{
			_rows.Remove(r);
			_rowsPanel.Children.Remove(r.Root);
		};
		Grid.SetColumn(del, 2);
		r.Root.Children.Add(r.Domain);
		r.Root.Children.Add(r.Ip);
		r.Root.Children.Add(del);
		_rows.Add(r);
		_rowsPanel.Children.Add(r.Root);
		return r;
	}

	private void ShowInfo(string msg, InfoBarSeverity sev, ButtonBase action = null)
	{
		_info.Message = msg;
		_info.Severity = sev;
		_info.ActionButton = action;
		_info.IsOpen = true;
	}

	private void FillIps()
	{
		string ip = _ping.Text.Trim();
		if (!NetUtil.IsValidV4(ip))
		{
			ShowInfo("请先填写有效的 Ping 测试 IP。", InfoBarSeverity.Warning);
			return;
		}
		foreach (HostRow r in _rows)
		{
			r.Ip.Text = ip;
		}
	}

	private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
	{
		SiteProfile sp = new SiteProfile
		{
			Name = _name.Text,
			Network = _net.Text,
			Mask = _mask.Text,
			PingIp = _ping.Text,
			FallbackGateway = _gw.Text,
			AdapterKeyword = _kw.Text,
			Hosts = new List<HostEntry>()
		};
		foreach (HostRow r in _rows)
		{
			if (!string.IsNullOrWhiteSpace(r.Domain.Text) || !string.IsNullOrWhiteSpace(r.Ip.Text))
			{
				sp.Hosts.Add(new HostEntry(r.Domain.Text, r.Ip.Text));
			}
		}
		sp.Normalize();
		string err = NetUtil.Validate(sp);
		if (err == null && _otherNames.Contains(sp.Name))
		{
			err = "已存在同名地点：" + sp.Name;
		}
		if (err != null)
		{
			ShowInfo(err, InfoBarSeverity.Error);
			args.Cancel = true;
			return;
		}
		string netAddr = NetUtil.NetworkAddress(sp.Network, sp.Mask);
		string key = sp.Network + "/" + sp.Mask;
		if (netAddr != sp.Network && _netWarnedFor != key)
		{
			_netWarnedFor = key;
			Button fix = new Button { Content = "改为 " + netAddr };
			fix.Click += (s, e) =>
			{
				_net.Text = netAddr;
				_info.IsOpen = false;
			};
			ShowInfo($"网段 {sp.Network} 不是网络地址，按掩码应为 {netAddr}。点「改为 {netAddr}」自动修正；再点一次「保存」则按原样保存。", InfoBarSeverity.Warning, fix);
			args.Cancel = true;
			return;
		}
		Result = sp;
	}
}
