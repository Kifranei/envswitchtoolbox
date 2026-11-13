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

	public AppConfig()
	{
		Version = 2;
		Profiles = new List<SiteProfile>();
	}
}
