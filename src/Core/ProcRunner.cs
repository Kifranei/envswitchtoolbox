using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EnvSwitchToolbox;

public static class ProcRunner
{
	private static Encoding _oem;

	public static Encoding OemEncoding
	{
		get
		{
			if (_oem == null)
			{
				try
				{
					_oem = Encoding.GetEncoding((int)GetOEMCP());
				}
				catch
				{
					_oem = Encoding.UTF8;
				}
			}
			return _oem;
		}
	}

	private static Encoding _ansi;

	/// <summary>系统 ANSI 代码页（中文系统为 GBK/936）。.NET 里 Encoding.Default 是 UTF-8，所以这里显式取 ACP。</summary>
	public static Encoding AnsiEncoding
	{
		get
		{
			if (_ansi == null)
			{
				try
				{
					_ansi = Encoding.GetEncoding((int)GetACP());
				}
				catch
				{
					_ansi = Encoding.UTF8;
				}
			}
			return _ansi;
		}
	}

	[DllImport("kernel32.dll")]
	private static extern uint GetOEMCP();

	[DllImport("kernel32.dll")]
	private static extern uint GetACP();

	public static string SysExe(string name)
	{
		return Path.Combine(Environment.SystemDirectory, name);
	}

	public static ProcResult Run(string exe, string args, Encoding enc, int timeoutMs, Action<string> onLine = null)
	{
		string text = (exe.Contains("\\") ? exe : SysExe(exe));
		if (!File.Exists(text))
		{
			text = exe;
		}
		ProcessStartInfo startInfo = new ProcessStartInfo(text, args)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			StandardOutputEncoding = enc,
			StandardErrorEncoding = enc,
			WorkingDirectory = Environment.SystemDirectory
		};
		StringBuilder sb = new StringBuilder();
		object lockObj = new object();
		using Process process = new Process
		{
			StartInfo = startInfo
		};
		DataReceivedEventHandler value = delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (lockObj)
				{
					sb.AppendLine(e.Data);
				}
				if (onLine != null)
				{
					try
					{
						onLine(e.Data);
					}
					catch
					{
					}
				}
			}
		};
		process.OutputDataReceived += value;
		process.ErrorDataReceived += value;
		process.Start();
		try
		{
			process.StandardInput.Close();
		}
		catch
		{
		}
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		if (!process.WaitForExit(timeoutMs))
		{
			try
			{
				process.Kill();
			}
			catch
			{
			}
			lock (lockObj)
			{
				sb.AppendLine("[超时] 进程执行超过 " + timeoutMs / 1000 + " 秒，已终止");
			}
			return new ProcResult
			{
				ExitCode = -1,
				Output = sb.ToString()
			};
		}
		process.WaitForExit();
		lock (lockObj)
		{
			return new ProcResult
			{
				ExitCode = process.ExitCode,
				Output = sb.ToString()
			};
		}
	}
}
