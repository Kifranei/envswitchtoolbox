using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EnvSwitchToolbox;

[DataContract]
public class AppConfig
{
	[DataMember(Name = "version", Order = 1)]
	public int Version { get; set; }

	[DataMember(Name = "profiles", Order = 2)]
	public List<SiteProfile> Profiles { get; set; }

	/// <summary>界面主题：system（跟随系统，默认）/ light / dark。2.0.0 新增，1.0.0 读取时会忽略该字段。</summary>
	[DataMember(Name = "theme", Order = 3, EmitDefaultValue = false)]
	public string Theme { get; set; }

	public AppConfig()
	{
		Version = 2;
		Profiles = new List<SiteProfile>();
		Theme = "system";
	}
}
