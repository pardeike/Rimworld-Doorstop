using HarmonyLib;
using System.Reflection;

namespace ILReloaderLib;

static class AssemblyLoadingPatcher
{
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		return instructions
			.MethodReplacer(
				SymbolExtensions.GetMethodInfo(() => Assembly.LoadFrom("")),
				SymbolExtensions.GetMethodInfo(() => LoadFrom(""))
			);
	}

	static Assembly RegisterTypes(Assembly assembly)
	{
		assembly
			.GetTypes()
			.SelectMany(type => Tools.AllReloadableMembers(type))
			.Do(member =>
			{
				$"registered: {member.DeclaringType.FullName}.{member.Name}".LogMessage();
				Reloader.reloadableMembers[member.Id()] = member;
			});
		return assembly;
	}

	static Assembly LoadFrom(string path)
	{
		$"loading {path}".LogMessage();
		var pe = File.ReadAllBytes(path);
		var pdbPath = Path.ChangeExtension(path, ".pdb");
		var pdb = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;
		var asm = pdb != null ? Assembly.Load(pe, pdb) : Assembly.Load(pe);
		return RegisterTypes(asm);
	}
}
