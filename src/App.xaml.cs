using Microsoft.UI.Xaml;

namespace EnvSwitchToolbox;

public partial class App : Application
{
	private MainWindow _window;

	public App()
	{
		InitializeComponent();
		UnhandledException += (s, e) =>
		{
			e.Handled = true;
			try
			{
				_window?.LogError("发生未处理的错误：" + e.Message);
				_ = Dialogs.Message("发生未处理的错误：\n" + e.Exception);
			}
			catch
			{
			}
		};
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		_window = new MainWindow();
		_window.Activate();
	}
}
