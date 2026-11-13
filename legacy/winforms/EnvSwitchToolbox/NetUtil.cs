using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace EnvSwitchToolbox;

public static class NetUtil
{
	public static bool TryParseV4(string s, out IPAddress ip)
	{
		ip = null;
		if (string.IsNullOrWhiteSpace(s))
		{
			return false;
		}
		s = s.Trim();
		if (s.Count((char c) => c == '.') != 3)
		{
			return false;
		}
		if (!IPAddress.TryParse(s, out IPAddress address) || address.AddressFamily != AddressFamily.InterNetwork)
		{
			return false;
		}
		ip = address;
		return true;
	}

	public static bool IsValidV4(string s)
	{
		IPAddress ip;
		return TryParseV4(s, out ip);
	}

	public static uint ToUInt(IPAddress ip)
	{
		byte[] addressBytes = ip.GetAddressBytes();
		return (uint)((addressBytes[0] << 24) | (addressBytes[1] << 16) | (addressBytes[2] << 8) | addressBytes[3]);
	}

	public static string FromUInt(uint v)
	{
		return $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";
	}

	public static int MaskToPrefix(string mask)
	{
		if (!TryParseV4(mask, out var ip))
		{
			return -1;
		}
		uint num = ToUInt(ip);
		int i;
		for (i = 0; i < 32 && (num & (uint)(int.MinValue >>> i)) != 0; i++)
		{
		}
		uint num2 = ((i != 0) ? ((uint)(-1 << 32 - i)) : 0u);
		if (num != num2)
		{
			return -1;
		}
		return i;
	}

	public static string NetworkAddress(string ip, string mask)
	{
		if (!TryParseV4(ip, out var ip2) || !TryParseV4(mask, out var ip3))
		{
			return null;
		}
		return FromUInt(ToUInt(ip2) & ToUInt(ip3));
	}

	public static bool IsValidDomain(string d)
	{
		if (string.IsNullOrWhiteSpace(d))
		{
			return false;
		}
		return Regex.IsMatch(d.Trim(), "^[A-Za-z0-9]([A-Za-z0-9\\-_]*[A-Za-z0-9])?(\\.[A-Za-z0-9]([A-Za-z0-9\\-_]*[A-Za-z0-9])?)*$");
	}

	public static string Validate(SiteProfile p)
	{
		if (string.IsNullOrWhiteSpace(p.Name))
		{
			return "名称不能为空";
		}
		if (!IsValidV4(p.Network))
		{
			return "内网网段不是有效的 IPv4 地址";
		}
		if (MaskToPrefix(p.Mask) < 0)
		{
			return "子网掩码无效（必须是连续掩码，如 255.255.255.0）";
		}
		if (!string.IsNullOrEmpty(p.PingIp) && !IsValidV4(p.PingIp))
		{
			return "Ping 测试 IP 无效";
		}
		if (!string.IsNullOrEmpty(p.FallbackGateway) && !IsValidV4(p.FallbackGateway))
		{
			return "兜底网关无效";
		}
		if (p.AdapterKeyword.IndexOf('\'') >= 0 || p.AdapterKeyword.IndexOf('"') >= 0)
		{
			return "网卡关键字不能包含引号";
		}
		foreach (HostEntry host in p.Hosts)
		{
			if (!IsValidDomain(host.Domain))
			{
				return "域名无效：" + host.Domain;
			}
			if (!IsValidV4(host.Ip))
			{
				return "域名 " + host.Domain + " 的 IP 无效：" + host.Ip;
			}
		}
		IGrouping<string, HostEntry> grouping = (from h in p.Hosts
			group h by h.Domain.ToLowerInvariant()).FirstOrDefault((IGrouping<string, HostEntry> g) => g.Count() > 1);
		if (grouping != null)
		{
			return "域名重复：" + grouping.Key;
		}
		return null;
	}
}
