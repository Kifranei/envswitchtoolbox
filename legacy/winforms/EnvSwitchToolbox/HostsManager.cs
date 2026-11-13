using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EnvSwitchToolbox;

public static class HostsManager
{
	public const string Marker = "# ENVSWITCH";

	public static string HostsPath
	{
		get
		{
			string text = Environment.GetEnvironmentVariable("windir");
			if (string.IsNullOrEmpty(text))
			{
				text = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			}
			return Path.Combine(text, "System32", "drivers", "etc", "hosts");
		}
	}

	public static string ReadText(string path, out bool hadBom)
	{
		byte[] array = ReadAllBytesRetry(path);
		hadBom = array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191;
		int num = (hadBom ? 3 : 0);
		try
		{
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array, num, array.Length - num);
		}
		catch (DecoderFallbackException)
		{
			return Encoding.Default.GetString(array, num, array.Length - num);
		}
	}

	private static byte[] ReadAllBytesRetry(string path)
	{
		Exception ex = null;
		for (int i = 0; i < 5; i++)
		{
			try
			{
				using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				using MemoryStream memoryStream = new MemoryStream();
				fileStream.CopyTo(memoryStream);
				return memoryStream.ToArray();
			}
			catch (IOException ex2)
			{
				ex = ex2;
				Thread.Sleep(300);
			}
		}
		throw new IOException("无法读取 hosts 文件（可能被占用）：" + ex.Message, ex);
	}

	public static List<string> SplitLines(string text)
	{
		List<string> list = Regex.Split(text, "\r\n|\n|\r").ToList();
		if (list.Count > 0 && list[list.Count - 1].Length == 0)
		{
			list.RemoveAt(list.Count - 1);
		}
		return list;
	}

	private static Regex DomainRegex(string domain)
	{
		return new Regex("(?<![A-Za-z0-9\\-_.])" + Regex.Escape(domain) + "(?![A-Za-z0-9\\-_.])", RegexOptions.IgnoreCase);
	}

	private static bool IsOldHeader(string line)
	{
		string text = line.Trim();
		return text.StartsWith(Marker, StringComparison.OrdinalIgnoreCase) || text.StartsWith("#" + Marker.TrimStart('#', ' '), StringComparison.OrdinalIgnoreCase);
	}

	public static List<string> BuildNewLines(List<string> lines, SiteProfile target, IEnumerable<string> allDomains, out int removed)
	{
		List<Regex> source = (from d in allDomains
			where !string.IsNullOrWhiteSpace(d)
			select d.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).Select(DomainRegex).ToList();
		List<string> list = new List<string>();
		int num = -1;
		removed = 0;
		foreach (string line in lines)
		{
			if (IsOldHeader(line) || source.Any((Regex r) => r.IsMatch(line)))
			{
				if (num < 0)
				{
					num = list.Count;
				}
				removed++;
			}
			else
			{
				list.Add(line);
			}
		}
		List<string> list2 = new List<string> { Marker + ": " + target.Name };
		foreach (HostEntry host in target.Hosts)
		{
			list2.Add(host.Ip + "  " + host.Domain);
		}
		if (num < 0)
		{
			if (list.Count > 0 && list[list.Count - 1].Trim().Length > 0)
			{
				list.Add("");
			}
			list.AddRange(list2);
		}
		else
		{
			list.InsertRange(num, list2);
		}
		return list;
	}

	public static void Switch(SiteProfile target, IEnumerable<SiteProfile> allProfiles, LogFn log)
	{
		string hostsPath = HostsPath;
		log("hosts 路径：" + hostsPath, LogLevel.Detail);
		if (!File.Exists(hostsPath))
		{
			log("hosts 文件不存在，将新建", LogLevel.Warn);
			Directory.CreateDirectory(Path.GetDirectoryName(hostsPath));
			File.WriteAllBytes(hostsPath, new byte[0]);
		}
		ClearReadOnly(hostsPath, log);
		string text = hostsPath + ".bak";
		try
		{
			if (File.Exists(text))
			{
				File.SetAttributes(text, FileAttributes.Normal);
			}
			File.Copy(hostsPath, text, overwrite: true);
			log("已备份到 " + text, LogLevel.Ok);
		}
		catch (Exception ex)
		{
			throw new Exception("备份 hosts 失败，已中止（未修改 hosts）：" + ex.Message, ex);
		}
		bool hadBom;
		List<string> lines = SplitLines(ReadText(hostsPath, out hadBom));
		IEnumerable<string> allDomains = (from h in allProfiles.SelectMany((SiteProfile p) => p.Hosts ?? new List<HostEntry>())
			select h.Domain).Concat(target.Hosts.Select((HostEntry h) => h.Domain));
		int removed;
		List<string> values = BuildNewLines(lines, target, allDomains, out removed);
		log($"移除旧条目 {removed} 行，写入 {target.Hosts.Count} 条新条目", LogLevel.Info);
		foreach (HostEntry host in target.Hosts)
		{
			log("  " + host.Ip + "  " + host.Domain, LogLevel.Detail);
		}
		string s = string.Join("\r\n", values) + "\r\n";
		byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s);
		WriteRetry(hostsPath, bytes, log);
		log("hosts 已写入（UTF-8 无 BOM，CRLF）", LogLevel.Ok);
		FlushDns(log);
	}

	private static void ClearReadOnly(string path, LogFn log)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
				log("已清除 hosts 的只读属性", LogLevel.Warn);
			}
		}
		catch (Exception ex)
		{
			log("清除只读属性失败：" + ex.Message, LogLevel.Warn);
		}
	}

	private static void WriteRetry(string path, byte[] bytes, LogFn log)
	{
		Exception ex = null;
		for (int i = 1; i <= 6; i++)
		{
			try
			{
				using FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
				fileStream.Write(bytes, 0, bytes.Length);
				fileStream.Flush(flushToDisk: true);
				return;
			}
			catch (UnauthorizedAccessException ex2)
			{
				ex = ex2;
				ClearReadOnly(path, log);
			}
			catch (IOException ex3)
			{
				ex = ex3;
			}
			log($"写入 hosts 失败（第 {i} 次），重试中：{ex.Message}", LogLevel.Warn);
			Thread.Sleep(500);
		}
		throw new Exception("写入 hosts 失败：" + ((ex != null) ? ex.Message : "") + "\n可能原因：未以管理员运行；或被杀毒软件/安全软件（如 360、火绒、Defender 的 hosts 防护）拦截或锁定。\n可在安全软件中放行后重试；原文件备份在 hosts.bak。");
	}

	public static void FlushDns(LogFn log)
	{
		ProcResult procResult = ProcRunner.Run("ipconfig.exe", "/flushdns", ProcRunner.OemEncoding, 30000);
		if (procResult.ExitCode == 0)
		{
			log("已执行 ipconfig /flushdns", LogLevel.Ok);
		}
		else
		{
			log("ipconfig /flushdns 返回 " + procResult.ExitCode + "：" + procResult.Output.Trim(), LogLevel.Warn);
		}
	}

	public static string DetectCurrent(IList<SiteProfile> profiles)
	{
		List<string> list;
		try
		{
			list = SplitLines(ReadText(HostsPath, out var _));
		}
		catch (Exception ex)
		{
			return "无法读取 hosts：" + ex.Message;
		}
		string markerName = null;
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in list)
		{
			string text = item.Trim();
			if (text.StartsWith(Marker + ":", StringComparison.OrdinalIgnoreCase))
			{
				markerName = text.Substring(Marker.Length + 1).Trim();
			}
			else
			{
				if (text.Length == 0 || text.StartsWith("#"))
				{
					continue;
				}
				int num = text.IndexOf('#');
				if (num >= 0)
				{
					text = text.Substring(0, num);
				}
				string[] array = text.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length < 2)
				{
					continue;
				}
				for (int i = 1; i < array.Length; i++)
				{
					if (!map.ContainsKey(array[i]))
					{
						map[array[i]] = array[0];
					}
				}
			}
		}
		string value;
		List<SiteProfile> list2 = profiles.Where((SiteProfile p) => p.Hosts.Count > 0 && p.Hosts.All((HostEntry h) => map.TryGetValue(h.Domain, out value) && value == h.Ip)).ToList();
		List<string> list3 = (from d in (from h in profiles.SelectMany((SiteProfile p) => p.Hosts)
				select h.Domain).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			where map.ContainsKey(d)
			select d + "→" + map[d]).ToList();
		if (list2.Count == 1)
		{
			return list2[0].Name;
		}
		if (list2.Count > 1)
		{
			SiteProfile siteProfile = list2.FirstOrDefault((SiteProfile p) => p.Name == markerName);
			return ((siteProfile != null) ? siteProfile.Name : list2[0].Name) + "（与 " + string.Join("、", list2.Select((SiteProfile p) => p.Name)) + " 配置相同）";
		}
		if (list3.Count == 0)
		{
			return "未配置（hosts 中无相关域名）";
		}
		return "未匹配任何地点：" + string.Join("，", list3);
	}
}
