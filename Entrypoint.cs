using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Doorstop
{
	public class Entrypoint
	{
		internal static Reloader reloader;

		public static void Start()
		{
			var harmony = new Harmony("brrainz.doorstop");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			reloader = new Reloader();
		}
	}

	public class ShutdownHandler : MonoBehaviour
	{
		public void OnApplicationQuit()
		{
			Entrypoint.reloader.DeleteAllFiles();
		}
	}

	[HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
	static class UIRoot_Entry_Init_Patch
	{
		static void Postfix()
		{
			var obj = new GameObject("RimWorldDoorstopObject");
			Object.DontDestroyOnLoad(obj);
			obj.AddComponent<ShutdownHandler>();
		}
	}

	[HarmonyPatch(typeof(ModAssemblyHandler), nameof(ModAssemblyHandler.ReloadAll))]
	static class ModAssemblyHandler_ReloadAll_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return instructions.MethodReplacer(
				SymbolExtensions.GetMethodInfo(() => Assembly.LoadFile("")),
				SymbolExtensions.GetMethodInfo(() => Reloader.LoadFile(""))
			);
		}
	}
}