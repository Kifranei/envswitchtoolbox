using System.Drawing;
using System.Windows.Forms;

namespace EnvSwitchToolbox;

public class TextViewForm : Form
{
	public TextViewForm(string title, string text)
	{
		((ContainerControl)this).AutoScaleDimensions = new SizeF(96f, 96f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).Text = title;
		((Form)this).ClientSize = new Size(900, 600);
		((Form)this).StartPosition = (FormStartPosition)4;
		TextBox val = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			ScrollBars = (ScrollBars)3,
			WordWrap = false,
			Dock = (DockStyle)5,
			Font = new Font("Consolas", 10f),
			BackColor = Color.White,
			Text = text
		};
		((TextBoxBase)val).SelectionStart = 0;
		((TextBoxBase)val).SelectionLength = 0;
		((Control)this).Controls.Add((Control)val);
		((Form)this).Icon = MainForm.AppIcon;
	}
}
