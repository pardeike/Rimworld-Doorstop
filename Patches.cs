using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Doorstop
{
	public class ShutdownHandler : MonoBehaviour
	{
		public void OnApplicationQuit()
		{
			Reloader.instance.DeleteAllFiles();
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
				SymbolExtensions.GetMethodInfo(() => Assembly.LoadFrom("")),
				SymbolExtensions.GetMethodInfo(() => Reloader.LoadOriginalAssembly(""))
			);
		}

		static void Postfix()
		{
			Reloader.RewriteAssemblyResolving();
		}
	}

	[HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootUpdate))]
	static class UIRoot_UIRootUpdate_Patch
	{
		internal static readonly Queue<string> messages = new();
		internal static readonly Queue<string> warnings = new();
		internal static readonly Queue<string> errors = new();

		static void Postfix()
		{
			if (messages.TryDequeue(out var message))
				Log.Message(message);
			if (warnings.TryDequeue(out var warning))
				Log.Warning(warning);
			if (errors.TryDequeue(out var error))
				Log.Error(error);
		}
	}
}
