using dnlib.DotNet;
using HarmonyLib;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace Doorstop
{
	internal class Reloader
	{
		static int count = 0;

		const string doorstopPrefix = "doorstop_";
		readonly string modsDir;

		delegate DynamicMethodDefinition CreateDynamicMethod(MethodBase original, string suffix, bool debug);
		static readonly MethodInfo m_CreateDynamicMethod = AccessTools.Method("HarmonyLib.MethodPatcher:CreateDynamicMethod");
		static readonly CreateDynamicMethod createDynamicMethod = AccessTools.MethodDelegate<CreateDynamicMethod>(m_CreateDynamicMethod);

		delegate MethodInfo CreateReplacement(object instance, out Dictionary<int, CodeInstruction> finalInstructions);
		static readonly MethodInfo m_CreateReplacement = AccessTools.Method("HarmonyLib.MethodPatcher:CreateReplacement");
		static readonly CreateReplacement createReplacement = AccessTools.MethodDelegate<CreateReplacement>(m_CreateReplacement);

		delegate void DetourMethod(MethodBase method, MethodBase replacement);
		static readonly MethodInfo m_DetourMethod = AccessTools.Method("HarmonyLib.PatchTools:DetourMethod");
		static readonly DetourMethod detourMethod = AccessTools.MethodDelegate<DetourMethod>(m_DetourMethod);

		internal Reloader()
		{
			modsDir = Path.Combine(Directory.GetCurrentDirectory(), "Mods");
			DeleteAllFiles();

			var watcher = new FileSystemWatcher(modsDir)
			{
				Filter = "*.dll",
				IncludeSubdirectories = true,
				EnableRaisingEvents = true
			};

			watcher.Changed += (_, e) =>
			{
				watcher.EnableRaisingEvents = false;
				try
				{
					var path = e.FullPath;
					if (path.StartsWith(doorstopPrefix) || path.Replace('\\', '/').Contains("/obj/"))
						return;

					var assemblyPath = DupFiles(path);
					using var dnModule = ModuleDefMD.Load(assemblyPath);
					UpdateModule(dnModule);
					LongEventHandler.QueueLongEvent(() => Log.Message($"Reloaded {path} [{e.ChangeType}]"), "dll-reloading", false, null);
				}
				finally
				{
					watcher.EnableRaisingEvents = true;
				}
			};

			watcher.Error += (sender, e) => Log.Error(e.GetException().ToString());
		}

		static void UpdateModule(ModuleDefMD dnModule)
		{
			bool AnnotatedType(TypeDef dnTypeTop)
			{
				if (dnTypeTop.HasCustomAttributes == false)
					return false;

				var reloadableTypeName = typeof(ReloadableAttribute).FullName;
				return dnTypeTop.CustomAttributes.Any(a => a.AttributeType.TypeName == reloadableTypeName);
			}

			dnModule.GetTypes()
				.DoIf(AnnotatedType, dnTypeTop =>
				{
					var typeWithAttr = Type.GetType(dnTypeTop.AssemblyQualifiedName);
					var types = AccessTools.InnerTypes(typeWithAttr).Where(IsCompilerGenerated).Concat(typeWithAttr);
					types.OfType<Type>().Do(type => UpdateType(type, dnModule.FindReflection(type.FullName)));
				});
		}

		static void UpdateType(Type systemType, TypeDef dnType)
		{
			if (dnType == null || systemType.IsGenericTypeDefinition)
				return;

			systemType.GetMethods(AccessTools.all)
				.Concat(systemType.GetConstructors(AccessTools.all)
				.Cast<MethodBase>())
				.Do(method =>
				{
					if (method.GetMethodBody() == null || method.IsGenericMethodDefinition)
						return;

					var code = method.GetMethodBody().GetILAsByteArray();
					var dnMethod = dnType.Methods.FirstOrDefault(m => Translator.MethodSigMatch(method, m));
					if (dnMethod == null)
						return;

					var methodBody = dnMethod.Body;
					var newCode = MethodSerializer.SerializeInstructions(methodBody);
					if (code.SequenceEqual(newCode))
						return;

					try
					{
						var patch = createDynamicMethod(method, $"_Reloaded{count++}", false);
						var ilGenerator = patch.GetILGenerator();

						MethodTranslator.TranslateLocals(methodBody, ilGenerator);
						MethodTranslator.TranslateRefs(methodBody, newCode, patch);

						var trv = Traverse.Create(ilGenerator);
						trv.Field("code").SetValue(newCode);
						trv.Field("code_len").SetValue(newCode.Length);
						trv.Field("max_stack").SetValue(methodBody.MaxStack);

						MethodTranslator.TranslateExceptions(methodBody, ilGenerator);

						var replacement = createReplacement(patch, out _);
						detourMethod(method, replacement);
					}
					catch (Exception e)
					{
						Log.Error($"Patching {method.FullDescription()} failed with {e}");
					}
				});
		}

		static bool IsCompilerGenerated(Type type)
		{
			while (type != null)
			{
				if (type.HasAttribute<CompilerGeneratedAttribute>())
					return true;
				type = type.DeclaringType;
			}

			return false;
		}

		internal void DeleteAllFiles()
		{
			DeleteFiles(modsDir, $"{doorstopPrefix}??????????????_*.dll");
			DeleteFiles(modsDir, $"{doorstopPrefix}??????????????_*.pdb");
		}

		static void DeleteFiles(string directory, string searchPattern)
		{
			foreach (var file in Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories))
				try
				{ File.Delete(file); }
				finally { }
		}

		internal static Assembly LoadFile(string path)
		{
			var copyDll = DupFiles(path);
			return Assembly.LoadFile(copyDll);
		}

		static string DupFiles(string path)
		{
			var assembliesDir = Path.GetDirectoryName(path);
			var baseName = Path.GetFileNameWithoutExtension(path);
			var filenamePrefix = $"{doorstopPrefix}{DateTime.Now:yyyyMMddHHmmss}_";

			var originalDll = Path.Combine(assembliesDir, $"{baseName}.dll");
			var copyDll = Path.Combine(assembliesDir, $"{filenamePrefix}{baseName}.dll");
			File.Copy(originalDll, copyDll, true);

			var originalPdb = Path.Combine(assembliesDir, $"{baseName}.pdb");
			if (File.Exists(originalPdb))
			{
				var copyPdb = Path.Combine(assembliesDir, $"{filenamePrefix}{baseName}.pdb");
				File.Copy(originalPdb, copyPdb, true);
			}

			return copyDll;
		}
	}
}
