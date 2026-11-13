using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;

namespace EnvSwitchToolbox;

public static class RouteManager
{
	private static string LoadScript(string endsWith)
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		string[] manifestResourceNames = executingAssembly.GetManifestResourceNames();
		foreach (string text in manifestResourceNames)
		{
			if (!text.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			using Stream stream = executingAssembly.GetManifestResourceStream(text);
			using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
			return streamReader.ReadToEnd();
		}
		throw new Exception("内部错误：找不到内嵌的 " + endsWith);
	}

	private static string Q(string s)
	{
		return "'" + (s ?? "").Replace("'", "''") + "'";
	}

	public static LogLevel LevelOf(string line)
	{
		string text = line.TrimStart(Array.Empty<char>());
		if (text.StartsWith("[OK]") || text.StartsWith("[+]"))
		{
			return LogLevel.Ok;
		}
		if (text.StartsWith("[!!]") || text.StartsWith("[Error]") || text.StartsWith("[超时]"))
		{
			return LogLevel.Error;
		}
		if (text.StartsWith("[~]"))
		{
			return LogLevel.Warn;
		}
		if (text.StartsWith("[-]"))
		{
			return LogLevel.Detail;
		}
		return LogLevel.Info;
	}

	private static int RunScript(SiteProfile p, string scriptResource, string actionLabel, LogFn log)
	{
		int num = NetUtil.MaskToPrefix(p.Mask);
		if (num < 0)
		{
			throw new Exception("子网掩码无效：" + p.Mask);
		}
		string text = NetUtil.NetworkAddress(p.Network, p.Mask);
		if (text == null)
		{
			throw new Exception("网段无效：" + p.Network);
		}
		if (text != p.Network)
		{
			log("网段 " + p.Network + " 按掩码规范化为 " + text, LogLevel.Warn);
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("$Net = " + Q(text));
		stringBuilder.AppendLine("$Mask = " + Q(p.Mask));
		stringBuilder.AppendLine("$PrefixLen = " + num);
		stringBuilder.AppendLine("$Keyword = " + Q(p.AdapterKeyword));
		stringBuilder.AppendLine("$FallbackGW = " + Q(p.FallbackGateway));
		string contents = stringBuilder.ToString() + LoadScript(scriptResource);
		string text2 = Path.Combine(Path.GetTempPath(), "EnvSwitchToolbox");
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text2, Path.GetFileNameWithoutExtension(scriptResource).ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N") + ".ps1");
		File.WriteAllText(text3, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		int exitCode;
		try
		{
			string text4 = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell\\v1.0\\powershell.exe");
			if (!File.Exists(text4))
			{
				text4 = "powershell.exe";
			}
			log($"调用 PowerShell {actionLabel}：{text}/{num} 掩码 {p.Mask}，网卡关键字 '{p.AdapterKeyword}'", LogLevel.Info);
			exitCode = ProcRunner.Run(text4, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + text3 + "\"", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 120000, delegate(string line)
			{
				if (line.Trim().Length > 0)
				{
					log(line, LevelOf(line));
				}
			}).ExitCode;
		}
		finally
		{
			try
			{
				File.Delete(text3);
			}
			catch
			{
			}
		}
		HostsManager.FlushDns(log);
		return exitCode;
	}

	public static bool Fix(SiteProfile p, LogFn log)
	{
		int num = RunScript(p, "RouteFix.ps1", "修复路由", log);
		switch (num)
		{
		case 0:
			log("路由修复完成", LogLevel.Ok);
			log("提示：拔掉专线 / 断开有线后，若希望该网段改走 Tailscale，请点「删除修复路由」。", LogLevel.Info);
			break;
		case 5:
			log("路由已添加，但 Tailscale 路由仍在抢占该网段（见上方提示）", LogLevel.Error);
			break;
		default:
			log("路由修复未成功（退出码 " + num + "）", LogLevel.Error);
			break;
		}
		PingTest(p.PingIp, log);
		return num == 0;
	}

	public static bool Delete(SiteProfile p, LogFn log)
	{
		int num = RunScript(p, "RouteDelete.ps1", "删除修复路由", log);
		switch (num)
		{
		case 0:
			log("删除修复路由完成", LogLevel.Ok);
			break;
		case 6:
			log("路由已删除，但恢复网卡 AutomaticMetric 失败（见上方提示）", LogLevel.Warn);
			break;
		case 7:
			log("路由已删除，但补加 Tailscale 路由失败（见上方提示）", LogLevel.Warn);
			break;
		default:
			log("删除修复路由未完全成功（退出码 " + num + "）", LogLevel.Error);
			break;
		}
		PingTest(p.PingIp, log);
		if (num != 0 && num != 6)
		{
			return num == 7;
		}
		return true;
	}

	public static void PingTest(string ip, LogFn log)
	{
		if (string.IsNullOrWhiteSpace(ip))
		{
			log("未设置 Ping 测试 IP，跳过", LogLevel.Detail);
			return;
		}
		int num = 0;
		using (Ping ping = new Ping())
		{
			for (int i = 0; i < 2; i++)
			{
				try
				{
					PingReply pingReply = ping.Send(ip, 1500);
					if (pingReply.Status == IPStatus.Success)
					{
						num++;
						log($"Ping {ip}: 时间={pingReply.RoundtripTime}ms TTL={((pingReply.Options != null) ? pingReply.Options.Ttl : 0)}", LogLevel.Detail);
					}
					else
					{
						log("Ping " + ip + ": " + pingReply.Status, LogLevel.Detail);
					}
				}
				catch (Exception ex)
				{
					log("Ping " + ip + " 异常：" + ex.Message, LogLevel.Detail);
				}
			}
		}
		if (num > 0)
		{
			log("[OK] " + ip + " 可达", LogLevel.Ok);
		}
		else
		{
			log("[!!] " + ip + " 不可达", LogLevel.Error);
		}
	}

	public static string RoutePrint()
	{
		return ProcRunner.Run("route.exe", "print -4", ProcRunner.OemEncoding, 30000).Output;
	}
}
