using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace EnvSwitchToolbox;

[DataContract]
public class SiteProfile
{
	[DataMember(Name = "name", Order = 1)]
	public string Name { get; set; }

	[DataMember(Name = "network", Order = 2)]
	public string Network { get; set; }

	[DataMember(Name = "mask", Order = 3)]
	public string Mask { get; set; }

	[DataMember(Name = "hosts", Order = 4)]
	public List<HostEntry> Hosts { get; set; }

	[DataMember(Name = "pingIp", Order = 5)]
	public string PingIp { get; set; }

	[DataMember(Name = "fallbackGateway", Order = 6)]
	public string FallbackGateway { get; set; }

	[DataMember(Name = "adapterKeyword", Order = 7)]
	public string AdapterKeyword { get; set; }

	public SiteProfile()
	{
		Hosts = new List<HostEntry>();
		AdapterKeyword = "Realtek";
	}

	public SiteProfile Clone()
	{
		return new SiteProfile
		{
			Name = Name,
			Network = Network,
			Mask = Mask,
			PingIp = PingIp,
			FallbackGateway = FallbackGateway,
			AdapterKeyword = AdapterKeyword,
			Hosts = (Hosts ?? new List<HostEntry>()).Select((HostEntry h) => h.Clone()).ToList()
		};
	}

	public void Normalize()
	{
		if (Hosts == null)
		{
			Hosts = new List<HostEntry>();
		}
		Hosts = Hosts.Where((HostEntry h) => h != null && !string.IsNullOrWhiteSpace(h.Domain)).ToList();
		foreach (HostEntry host in Hosts)
		{
			host.Domain = (host.Domain ?? "").Trim();
			host.Ip = (host.Ip ?? "").Trim();
		}
		Name = (Name ?? "").Trim();
		Network = (Network ?? "").Trim();
		Mask = (Mask ?? "").Trim();
		PingIp = (PingIp ?? "").Trim();
		FallbackGateway = (FallbackGateway ?? "").Trim();
		AdapterKeyword = (string.IsNullOrWhiteSpace(AdapterKeyword) ? "Realtek" : AdapterKeyword.Trim());
	}

	public override string ToString()
	{
		return Name;
	}
}
