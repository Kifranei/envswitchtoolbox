using System.Runtime.Serialization;

namespace EnvSwitchToolbox;

[DataContract]
public class HostEntry
{
	[DataMember(Name = "domain", Order = 1)]
	public string Domain { get; set; }

	[DataMember(Name = "ip", Order = 2)]
	public string Ip { get; set; }

	public HostEntry()
	{
	}

	public HostEntry(string domain, string ip)
	{
		Domain = domain;
		Ip = ip;
	}

	public HostEntry Clone()
	{
		return new HostEntry(Domain, Ip);
	}
}
