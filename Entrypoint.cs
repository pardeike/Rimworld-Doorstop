
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace Doorstop;

public class Entrypoint
{
	const string doorstopConfigFile = "doorstop_config.ini";

	/*static void Log(string msg)
	{
		File.AppendAllText("doorstop_log.txt", $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
	}*/

	public static void Start()
	{
		var assemblyNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
		foreach (var assemblyName in assemblyNames.Where(name => name.StartsWith("Mono.Cecil.")))
			Load(assemblyName);

		var regex = new Regex(@"^harmony_library *= *(.+)");
		var harmonyFilePath = File.Exists(doorstopConfigFile) ? File
			.ReadAllLines(doorstopConfigFile)
			.Select((line => regex.Matches(line)))
			.Where(match => match.Count > 0)
			.Select(match => match[0].Groups[1].Value.Trim())
			.Where(File.Exists)
			.FirstOrDefault() : null;
		if (harmonyFilePath == null)
			return;

		var path = Path.GetFullPath(harmonyFilePath);
		var data = File.ReadAllBytes(path);
		var harmony = Assembly.Load(data);
		if (harmony == null)
			return;

		var reloader = Load("ILReloaderLib.dll");

		var tReloader = reloader.GetType("ILReloaderLib.Reloader");
		// tReloader.GetMethod("SetLogger", BindingFlags.Public | BindingFlags.Static).Invoke(null, [(Action<string>)Log]);
		var mModAssemblyHandler = typeof(ModAssemblyHandler).GetMethod("ReloadAll", BindingFlags.Public | BindingFlags.Instance);
		tReloader.GetMethod("FixAssemblyLoading", BindingFlags.Public | BindingFlags.Static).Invoke(null, [mModAssemblyHandler]);
		tReloader.GetMethod("Watch", BindingFlags.Public | BindingFlags.Static).Invoke(null, ["Mods"]);
	}

	static Assembly Load(string assemblyName)
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(assemblyName);
		var data = new byte[stream.Length];
		stream.Read(data, 0, data.Length);
		return Assembly.Load(data);
	}
}