using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace EnvSwitchToolbox;

internal static class Program
{
	public const string AppTitle = "环境切换工具箱";

	public static bool IsAdmin()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.SetUnhandledExceptionMode((UnhandledExceptionMode)2);
		Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
		{
			MessageBox.Show("发生未处理的错误：\n" + e.Exception, "环境切换工具箱", (MessageBoxButtons)0, (MessageBoxIcon)16);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			MessageBox.Show("发生未处理的错误：\n" + e.ExceptionObject, "环境切换工具箱", (MessageBoxButtons)0, (MessageBoxIcon)16);
		};
		if (!IsAdmin() && (int)MessageBox.Show("本工具需要管理员权限才能修改 hosts 和路由。\n是否以管理员身份重新启动？\n（选「否」将以受限模式打开，修改操作会失败）", "环境切换工具箱", (MessageBoxButtons)4, (MessageBoxIcon)48) == 6)
		{
			try
			{
				Process.Start(new ProcessStartInfo(Application.ExecutablePath)
				{
					UseShellExecute = true,
					Verb = "runas"
				});
				return;
			}
			catch (Exception ex)
			{
				MessageBox.Show("提权失败：" + ex.Message, "环境切换工具箱", (MessageBoxButtons)0, (MessageBoxIcon)16);
			}
		}
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\EnvSwitchToolbox_SingleInstance", out createdNew))
		{
			if (!createdNew)
			{
				MessageBox.Show("环境切换工具箱已经在运行。", "环境切换工具箱", (MessageBoxButtons)0, (MessageBoxIcon)64);
			}
			else
			{
				Application.Run((Form)new MainForm());
			}
		}
	}
}
