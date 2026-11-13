using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EnvSwitchToolbox;

public static class Program
{
	public const string AppTitle = "环境切换工具箱";

	public static bool IsAdmin()
	{
		try
		{
			using WindowsIdentity id = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>以管理员身份重新启动本程序（UAC）。成功返回 true。</summary>
	public static bool RestartElevated(out string error)
	{
		error = null;
		try
		{
			Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" });
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static Mutex _mutex;

	[STAThread]
	private static int Main(string[] args)
	{
		// route print / ipconfig 的输出是 OEM 代码页（中文系统 936），.NET 需要注册代码页编码
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		// 单文件发布时运行库与 resources.pri 被解压到临时目录，需告知 Windows App SDK 去那里找
		if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY")))
		{
			Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
		}
		WinRT.ComWrappersSupport.InitializeComWrappers();
		_mutex = new Mutex(initiallyOwned: true, "Local\\EnvSwitchToolbox_SingleInstance", out bool createdNew);
		if (!createdNew)
		{
			Win32.MessageBoxW(IntPtr.Zero, "环境切换工具箱已经在运行。", AppTitle, Win32.MB_OK | Win32.MB_ICONINFORMATION);
			return 0;
		}
		Application.Start(p =>
		{
			DispatcherQueueSynchronizationContext ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
			SynchronizationContext.SetSynchronizationContext(ctx);
			_ = new App();
		});
		GC.KeepAlive(_mutex);
		return 0;
	}

	/// <summary>重新以管理员启动前释放单实例锁。</summary>
	public static void ReleaseMutex()
	{
		try
		{
			_mutex?.ReleaseMutex();
			_mutex?.Dispose();
			_mutex = null;
		}
		catch
		{
		}
	}
}
