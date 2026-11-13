using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace EnvSwitchToolbox;

public static class ConfigStore
{
	public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EnvSwitchToolbox");

	public static string FilePath => Path.Combine(Dir, "profiles.json");

	public static AppConfig Load(out string warning)
	{
		warning = null;
		try
		{
			if (File.Exists(FilePath))
			{
				AppConfig appConfig = ReadFile(FilePath);
				if (appConfig != null)
				{
					return appConfig;
				}
			}
		}
		catch (Exception ex2)
		{
			try
			{
				File.Copy(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
			}
			catch
			{
			}
			warning = "配置文件读取失败，已重置为空配置（原文件已备份为 .broken-*）：" + ex2.Message;
		}
		AppConfig appConfig2 = new AppConfig
		{
			Profiles = Defaults.BuiltIns()
		};
		try
		{
			Save(appConfig2);
		}
		catch (Exception ex3)
		{
			warning = warning + " 保存配置失败：" + ex3.Message;
		}
		return appConfig2;
	}

	/// <summary>读取任意路径的配置文件（用于启动加载和「导入」）。</summary>
	public static AppConfig ReadFile(string path)
	{
		using FileStream fileStream = File.OpenRead(path);
		AppConfig appConfig = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(fileStream);
		if (appConfig == null || appConfig.Profiles == null)
		{
			return null;
		}
		foreach (SiteProfile profile in appConfig.Profiles)
		{
			profile.Normalize();
		}
		return appConfig;
	}

	public static void Save(AppConfig cfg)
	{
		Directory.CreateDirectory(Dir);
		WriteFile(cfg, FilePath);
	}

	/// <summary>把配置写到任意路径（用于保存和「导出」），先写临时文件再替换。</summary>
	public static void WriteFile(AppConfig cfg, string path)
	{
		string text = path + ".tmp";
		using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write))
		{
			XmlDictionaryWriter val = JsonReaderWriterFactory.CreateJsonWriter((Stream)fileStream, (Encoding)new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false, true, "  ");
			try
			{
				((XmlObjectSerializer)new DataContractJsonSerializer(typeof(AppConfig))).WriteObject(val, (object)cfg);
				((XmlWriter)val).Flush();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		File.Move(text, path);
	}
}
