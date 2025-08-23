using System;
using System.Reflection;

namespace RimWorldDoorstop;

public class Entrypoint
{
	static readonly string[] assemblies =
	[
		"0Harmony.dll",
		"Mono.Cecil.dll",
		"Mono.Cecil.Mdb.dll",
		"Mono.Cecil.Pdb.dll",
		"Mono.Cecil.Rocks.dll"
	];

	public static void Start()
	{
		foreach (var assemblyName in assemblies)
			Assembly.Load(LoadResourceBytes(assemblyName));
		Type.GetType("RimWorldDoorstop.Reloader").GetMethod("Start").Invoke(null, null);
	}

	static byte[] LoadResourceBytes(string resourceName)
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
		var data = new byte[stream.Length];
		stream.Read(data, 0, data.Length);
		return data;
	}
}