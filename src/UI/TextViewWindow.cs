using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EnvSwitchToolbox;

/// <summary>只读文本查看窗口（hosts、路由表），外观和主题与主窗口一致。</summary>
public sealed class TextViewWindow : Window
{
	public TextViewWindow(string title, string text, string theme)
	{
		Title = title;
		Grid root = new Grid();
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		Grid bar = WindowChrome.BuildTitleBar(title, out _);
		root.Children.Add(bar);
		TextBox box = new TextBox
		{
			Text = text ?? "",
			IsReadOnly = true,
			AcceptsReturn = true,
			TextWrapping = TextWrapping.NoWrap,
			FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
			FontSize = 13,
			Margin = new Thickness(12, 4, 12, 12)
		};
		ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
		ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
		Grid.SetRow(box, 1);
		root.Children.Add(box);
		Content = root;
		WindowChrome.Setup(this, root, bar, theme, 900, 600, 400, 300);
		MainWindow.TextWindows.Add(this);
		Closed += (s, e) => MainWindow.TextWindows.Remove(this);
		Root = root;
	}

	public FrameworkElement Root { get; }
}
