using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EnvSwitchToolbox;

public class ProfileEditForm : Form
{
	private readonly SiteProfile _p;

	private readonly HashSet<string> _otherNames;

	private TextBox _name;

	private TextBox _net;

	private TextBox _mask;

	private TextBox _ping;

	private TextBox _gw;

	private TextBox _kw;

	private DataGridView _grid;

	private Panel _fields;

	private readonly List<Label> _labels = new List<Label>();

	private readonly List<TextBox> _boxes = new List<TextBox>();

	private readonly List<Label> _hints = new List<Label>();

	public SiteProfile Result { get; private set; }

	private int S(int v)
	{
		return (int)Math.Round((double)(v * ((Control)this).DeviceDpi) / 96.0);
	}

	public ProfileEditForm(SiteProfile p, IEnumerable<string> otherNames, string title)
	{
		_p = p;
		_otherNames = new HashSet<string>(otherNames, StringComparer.OrdinalIgnoreCase);
		((Control)this).SuspendLayout();
		((ContainerControl)this).AutoScaleDimensions = new SizeF(96f, 96f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)this).Text = title;
		((Form)this).ClientSize = new Size(600, 580);
		((Control)this).MinimumSize = new Size(500, 480);
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).MinimizeBox = false;
		((Form)this).MaximizeBox = true;
		((Form)this).ShowInTaskbar = false;
		((Form)this).Icon = MainForm.AppIcon;
		((Control)this).Padding = new Padding(10);
		_grid = new DataGridView
		{
			Dock = (DockStyle)5,
			AllowUserToAddRows = true,
			AllowUserToDeleteRows = true,
			RowHeadersWidth = 28,
			AutoSizeColumnsMode = (DataGridViewAutoSizeColumnsMode)16,
			SelectionMode = (DataGridViewSelectionMode)1,
			BackgroundColor = SystemColors.Window,
			MultiSelect = true
		};
		_grid.Columns.Add((DataGridViewColumn)new DataGridViewTextBoxColumn
		{
			Name = "Domain",
			HeaderText = "域名",
			FillWeight = 60f
		});
		_grid.Columns.Add((DataGridViewColumn)new DataGridViewTextBoxColumn
		{
			Name = "Ip",
			HeaderText = "IP",
			FillWeight = 40f
		});
		foreach (HostEntry host in p.Hosts)
		{
			_grid.Rows.Add(new object[2] { host.Domain, host.Ip });
		}
		((Control)this).Controls.Add((Control)(object)_grid);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "hosts 条目（域名 → IP）：",
			AutoSize = true,
			Dock = (DockStyle)1,
			Padding = new Padding(0, 8, 0, 4)
		});
		_fields = new Panel
		{
			Dock = (DockStyle)1,
			Height = 200
		};
		_name = AddRow("名称：", p.Name, null);
		_net = AddRow("内网网段：", p.Network, "例：192.0.2.0");
		_mask = AddRow("子网掩码：", p.Mask, "例：255.255.255.0");
		_ping = AddRow("Ping 测试 IP：", p.PingIp, "可空");
		_gw = AddRow("兜底网关：", p.FallbackGateway, "可空；网卡未配置网关时使用");
		_kw = AddRow("网卡关键字：", p.AdapterKeyword, "匹配网卡描述，默认 Realtek");
		((Control)_fields).Layout += (LayoutEventHandler)delegate
		{
			LayoutFields();
		};
		((Control)this).Controls.Add((Control)(object)_fields);
		FlowLayoutPanel val = new FlowLayoutPanel
		{
			Dock = (DockStyle)2,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			WrapContents = true,
			Padding = new Padding(0, 4, 0, 0)
		};
		((Control)val).Controls.Add((Control)(object)Btn("添加一行", delegate
		{
			int num = _grid.Rows.Add(new object[2]
			{
				"",
				_ip()
			});
			_grid.CurrentCell = _grid.Rows[num].Cells[0];
			_grid.BeginEdit(true);
		}));
		((Control)val).Controls.Add((Control)(object)Btn("删除选中行", delegate
		{
			DeleteSelectedRows();
		}));
		((Control)val).Controls.Add((Control)(object)Btn("IP 全部设为 Ping IP", delegate
		{
			FillIps();
		}));
		((Control)this).Controls.Add((Control)(object)val);
		FlowLayoutPanel val2 = new FlowLayoutPanel
		{
			Dock = (DockStyle)2,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			FlowDirection = (FlowDirection)2,
			WrapContents = false,
			Padding = new Padding(0, 6, 0, 0)
		};
		Button val3 = Btn("取消", delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		});
		Button val4 = Btn("保存", delegate
		{
			OnOk();
		});
		((Control)val2).Controls.Add((Control)(object)val3);
		((Control)val2).Controls.Add((Control)(object)val4);
		((Control)this).Controls.Add((Control)(object)val2);
		((Form)this).AcceptButton = null;
		((Form)this).CancelButton = (IButtonControl)(object)val3;
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
		((Form)this).Load += delegate
		{
			((Control)this).MinimumSize = new Size(S(500), S(480));
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
			LayoutFields();
		};
	}

	private TextBox AddRow(string label, string value, string hint)
	{
		Label val = new Label
		{
			Text = label,
			AutoSize = true
		};
		TextBox val2 = new TextBox
		{
			Text = (value ?? "")
		};
		Label val3 = new Label
		{
			Text = (hint ?? ""),
			AutoSize = true,
			ForeColor = Color.Gray,
			Visible = (hint != null)
		};
		((Control)_fields).Controls.Add((Control)(object)val);
		((Control)_fields).Controls.Add((Control)(object)val2);
		((Control)_fields).Controls.Add((Control)(object)val3);
		_labels.Add(val);
		_boxes.Add(val2);
		_hints.Add(val3);
		return val2;
	}

	private void LayoutFields()
	{
		if (_fields == null || _boxes.Count == 0)
		{
			return;
		}
		int width = ((Control)_fields).ClientSize.Width;
		int num = S(6);
		int num2 = _labels.Max((Label x) => ((Control)x).PreferredSize.Width);
		int num3 = (from x in _hints
			where ((Control)x).Visible
			select ((Control)x).PreferredSize.Width).DefaultIfEmpty(0).Max();
		int preferredHeight = ((TextBoxBase)_boxes[0]).PreferredHeight;
		int num4 = Math.Max(preferredHeight, ((Control)_labels[0]).PreferredSize.Height) + S(6);
		int num5 = num2 + num;
		int val = width - num5 - num - num3;
		val = Math.Max(S(140), Math.Min(S(300), val));
		bool flag = num5 + val + num + num3 > width;
		int num6 = 0;
		for (int num7 = 0; num7 < _boxes.Count; num7++)
		{
			Label val2 = _labels[num7];
			TextBox obj = _boxes[num7];
			Label val3 = _hints[num7];
			((Control)val2).Location = new Point(0, num6 + (preferredHeight - ((Control)val2).PreferredSize.Height) / 2 + S(1));
			((Control)obj).SetBounds(num5, num6, Math.Max(val2: (!((Control)val3).Visible) ? Math.Max(val, Math.Min(width - num5, val + num + num3)) : val, val1: S(80)), preferredHeight);
			if (((Control)val3).Visible)
			{
				if (flag)
				{
					((Control)val3).Location = new Point(num5, num6 + preferredHeight + S(1));
					num6 += ((Control)val3).PreferredSize.Height;
				}
				else
				{
					((Control)val3).Location = new Point(num5 + val + num, num6 + (preferredHeight - ((Control)val3).PreferredSize.Height) / 2 + S(1));
				}
			}
			num6 += num4;
		}
		if (((Control)_fields).Height != num6)
		{
			((Control)_fields).Height = num6;
		}
	}

	private string _ip()
	{
		return (((Control)_ping).Text ?? "").Trim();
	}

	private static Button Btn(string text, EventHandler h)
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
		((Control)val).Click += h;
		return val;
	}

	private void DeleteSelectedRows()
	{
		List<DataGridViewRow> list = (from DataGridViewRow r in (IEnumerable)_grid.SelectedRows
			where !r.IsNewRow
			select r).ToList();
		if (list.Count == 0 && _grid.CurrentRow != null && !_grid.CurrentRow.IsNewRow)
		{
			list.Add(_grid.CurrentRow);
		}
		foreach (DataGridViewRow item in list)
		{
			_grid.Rows.Remove(item);
		}
	}

	private void FillIps()
	{
		string text = _ip();
		if (!NetUtil.IsValidV4(text))
		{
			MessageBox.Show((IWin32Window)this, "请先填写有效的 Ping 测试 IP。", ((Control)this).Text);
			return;
		}
		foreach (DataGridViewRow item in (IEnumerable)_grid.Rows)
		{
			DataGridViewRow val = item;
			if (!val.IsNewRow)
			{
				val.Cells[1].Value = text;
			}
		}
	}

	private void OnOk()
	{
		_grid.EndEdit();
		SiteProfile siteProfile = new SiteProfile
		{
			Name = ((Control)_name).Text,
			Network = ((Control)_net).Text,
			Mask = ((Control)_mask).Text,
			PingIp = ((Control)_ping).Text,
			FallbackGateway = ((Control)_gw).Text,
			AdapterKeyword = ((Control)_kw).Text,
			Hosts = new List<HostEntry>()
		};
		foreach (DataGridViewRow item in (IEnumerable)_grid.Rows)
		{
			DataGridViewRow val = item;
			if (!val.IsNewRow)
			{
				string text = Convert.ToString(val.Cells[0].Value);
				string text2 = Convert.ToString(val.Cells[1].Value);
				if (!string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(text2))
				{
					siteProfile.Hosts.Add(new HostEntry(text, text2));
				}
			}
		}
		siteProfile.Normalize();
		string text3 = NetUtil.Validate(siteProfile);
		if (text3 == null && _otherNames.Contains(siteProfile.Name))
		{
			text3 = "已存在同名地点：" + siteProfile.Name;
		}
		if (text3 != null)
		{
			MessageBox.Show((IWin32Window)this, text3, "输入有误", (MessageBoxButtons)0, (MessageBoxIcon)48);
			return;
		}
		string text4 = NetUtil.NetworkAddress(siteProfile.Network, siteProfile.Mask);
		if (text4 != siteProfile.Network)
		{
			DialogResult val2 = MessageBox.Show((IWin32Window)this, string.Format("网段 {0} 不是网络地址，按掩码应为 {1}。\n是否自动改为 {1}？", siteProfile.Network, text4), ((Control)this).Text, (MessageBoxButtons)3, (MessageBoxIcon)32);
			if ((int)val2 == 2)
			{
				return;
			}
			if ((int)val2 == 6)
			{
				siteProfile.Network = text4;
			}
		}
		Result = siteProfile;
		((Form)this).DialogResult = (DialogResult)1;
		((Form)this).Close();
	}
}
