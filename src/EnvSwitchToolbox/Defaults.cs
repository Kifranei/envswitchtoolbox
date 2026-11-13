using System.Collections.Generic;

namespace EnvSwitchToolbox;

/// <summary>
/// 开源版不内置任何地点。首次运行时配置为空，请在界面中「新增」或「导入」。
/// 示例配置见仓库 examples/profiles.example.json。
/// </summary>
public static class Defaults
{
	public static List<SiteProfile> BuiltIns()
	{
		return new List<SiteProfile>();
	}
}
