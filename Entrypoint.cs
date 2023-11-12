using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Doorstop
{
	public class Entrypoint
	{
		public const string HarmonyId = "brrainz.doorstop";
		public static readonly Harmony harmony = new(HarmonyId);
		public static MethodInfo original, patch;

		// entrypoint of unity doorstep
		public static void Start()
		{
			var original = AccessTools.PropertyGetter(typeof(CultureInfo), nameof(CultureInfo.Name));
			patch = harmony.Patch(original, postfix: new HarmonyMethod(typeof(Entrypoint), nameof(LatePatching)));
		}

		// called after the base game has loaded by a method that is used early on
		// to avoid that this is running later again, we unpatch it while it is running
		public static void LatePatching()
		{
			try
			{
				Log.Message($"Patching the base game from doorstop");
				harmony.UnpatchAll(HarmonyId);

				var original = AccessTools.Method(typeof(ModAssemblyHandler), nameof(ModAssemblyHandler.ReloadAll));
				harmony.Patch(original, prefix: new HarmonyMethod(typeof(Entrypoint), nameof(ReloadAllReplacement)));
			}
			catch (Exception ex)
			{
				Log.Error($"Exception patching: {ex}");
			}
		}

		// this prefix replaces the original method and loads all assemblies via Assembly.LoadFile
		// to allow better debugging (relations to pdb files are kept)
		public static bool ReloadAllReplacement(ModContentPack ___mod, List<Assembly> ___loadedAssemblies)
		{
			Log.Message($"Loading mod {___mod.Name}");
			var items = ModContentPack.GetAllFilesForModPreserveOrder(___mod, "Assemblies/", (string e) => e.ToLower() == ".dll", null).Select(f => f.Item2);
			foreach (var item in items)
			{
				var assembly = Assembly.LoadFile(item.FullName);
				Log.Message($"- loaded {assembly.FullName}");
				GenTypes.ClearCache();
				___loadedAssemblies.Add(assembly);
			}
			return false;
		}
	}
}