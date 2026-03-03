using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;

namespace ILReloaderLib;

public class Reloader
{
	static readonly Harmony harmony;
	static bool MethodBodyReader_HandleNativeMethod_Prefix(MethodBase ___method) => ___method.ReflectedType == null;

	internal static readonly Dictionary<string, MethodBase> reloadableMembers = [];
	static readonly List<FileSystemWatcher> watchers = [];
	static readonly int reloadDelay = int.TryParse(Environment.GetEnvironmentVariable("ILRELOADER_DELAY"), out var d) ? d : 1;

	static Reloader()
	{
		harmony = new Harmony("brrainz.doorstop");
		var original = AccessTools.Method("HarmonyLib.MethodBodyReader:HandleNativeMethod");
		if (original != null)
		{
			var prefix = SymbolExtensions.GetMethodInfo(() => MethodBodyReader_HandleNativeMethod_Prefix(default));
			_ = harmony.Patch(original, prefix: new HarmonyMethod(prefix));
		}
	}

	public static void SetLogger(Action<string> logger) => Tools.Logger = logger;

	public static void FixAssemblyLoading(MethodBase method)
	{
		var transpiler = SymbolExtensions.GetMethodInfo(() => AssemblyLoadingPatcher.Transpiler(default));
		_ = harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
	}

	public static void Watch(string directory)
		=> watchers.Add(CreateWatcher(directory));

	static int counter = 0;
	static DynamicMethod TranspilerFactory(MethodBase originalMethod)
	{
		var t_cInstr = typeof(IEnumerable<CodeInstruction>);
		var attributes = System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static;
		var dm = new DynamicMethod($"TransientTranspiler{++counter}", attributes, CallingConventions.Standard, t_cInstr, [t_cInstr, typeof(ILGenerator)], typeof(Reloader), true);
		var il = dm.GetILGenerator();
		il.Emit(OpCodes.Ldstr, originalMethod.Id());
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => GetInstructions(default, default)));
		il.Emit(OpCodes.Ret);
		return dm;
	}

	static IEnumerable<CodeInstruction> GetInstructions(string methodId, ILGenerator il)
	{
		var harmonyInstructions = CecilConverter.Convert(il, methodId);
		foreach (var instr in harmonyInstructions)
			yield return instr;
	}

	static void Patch(AssemblyDefinition newAssembly)
	{
		var harmony = new Harmony("brrainz.ilreloader");
		newAssembly.Modules.SelectMany(m => m.Types).SelectMany(Tools.AllReloadableMembers)
			.Do(replacementMethod =>
			{
				try
				{
					if (reloadableMembers.TryGetValue(replacementMethod.Id(), out var originalMethod))
					{
						$"patching {originalMethod.FullDescription()} with {replacementMethod}".LogMessage();
						var originalId = originalMethod.Id();
						if (!string.IsNullOrEmpty(originalId))
						{
							CecilConverter.Register(originalId, replacementMethod);
							harmony.Unpatch(originalMethod, HarmonyPatchType.Transpiler, harmony.Id);
							var m_TranspilerFactory = SymbolExtensions.GetMethodInfo(() => TranspilerFactory(default));
							var transpilerFactory = new HarmonyMethod(m_TranspilerFactory) { priority = int.MaxValue };
							_ = harmony.Patch(originalMethod, transpiler: transpilerFactory);
						}
						else
						{
							$"cannot patch - originalId: '{originalId}', replacementMethod: {replacementMethod}".LogError();
						}
					}
				}
				catch (Exception ex)
				{
					$"error processing replacement method {replacementMethod}: {ex}".LogError();
				}
			});
	}

	static FileSystemWatcher CreateWatcher(string directory)
	{
		var watcher = new FileSystemWatcher(directory, "*.dll")
		{
			IncludeSubdirectories = true,
			EnableRaisingEvents = true,
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
		};
		watcher.Error += (_, e) => e.GetException().ToString().LogError();
		watcher.Changed += (_, e) =>
		{
			var path = e.FullPath;
			if (path.Replace('\\', '/').Contains("/obj/"))
				return;
			changedFiles.Add(path);
		};
		return watcher;
	}

	static readonly Debouncer changedFiles = new(TimeSpan.FromSeconds(reloadDelay), path =>
	{
		try
		{
			$"reloading {path}".LogMessage();
			using var readStream = File.OpenRead(path);
			using var assembly = AssemblyDefinition.ReadAssembly(readStream);
			Patch(assembly);
		}
		catch (Exception ex)
		{
			$"error during reloading {path}: {ex}".LogError();
		}
	});
}
