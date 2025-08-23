using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using static HarmonyLib.AccessTools;

namespace RimWorldDoorstop;

[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
public class ReloadableAttribute : Attribute
{
}

internal static class Tools
{
	static readonly string reloadableTypeName = typeof(ReloadableAttribute).Name;

	internal delegate void DetourMethodDelegate(MethodBase method, MethodBase replacement);
	internal static readonly DetourMethodDelegate DetourMethod = MethodDelegate<DetourMethodDelegate>(Method("HarmonyLib.PatchTools:DetourMethod"));

	internal static void LogMessage(this string log) => UIRoot_UIRootUpdate_Patch.messages.Enqueue(log);
	internal static void LogWarning(this string log) => UIRoot_UIRootUpdate_Patch.warnings.Enqueue(log);
	internal static void LogError(this string log) => UIRoot_UIRootUpdate_Patch.errors.Enqueue(log);

	internal static bool ReflectIsReloadable(this MethodBase method) => method.GetCustomAttributesData().Any(a => a.AttributeType.Name == reloadableTypeName);
	internal static bool IsReloadable(this MethodBase method) => method.CustomAttributes.Any(a => a.AttributeType.Name == reloadableTypeName);

	internal static string WithoutFileExtension(this string filePath)
	{
		var directory = Path.GetDirectoryName(filePath);
		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
		return Path.Combine(directory, fileNameWithoutExtension);
	}

	internal static string Id(this MethodBase member)
	{
		var sb = new StringBuilder(128);
		sb.Append(member.DeclaringType.FullName);
		sb.Append('.');
		sb.Append(member.Name);
		sb.Append('(');
		sb.Append(string.Join(", ", member.GetParameters().Select(p => p.ParameterType.FullName)));
		sb.Append(')');
		return sb.ToString();
	}

	internal static IEnumerable<MethodBase> AllReloadableMembers(this Type type, bool reflectionOnly)
	{
		Func<MethodBase, bool> isReloadable = reflectionOnly ? ReflectIsReloadable : IsReloadable;
		foreach (var member in type.GetMethods(all).Where(isReloadable))
			yield return member;
		foreach (var member in type.GetConstructors(all).Where(isReloadable))
			yield return member;
	}

	internal static Type[] ReflectAllTypes(this Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			return [.. e.Types.Where(t => t != null)];
		}
	}

	internal static void Copy(string source, string target, int n)
	{
		var assembly = AssemblyDefinition.ReadAssembly(source);
		assembly.Name.Name = $"{assembly.Name.Name}-{n}";
		assembly.Name.PublicKey = null;
		assembly.Name.HasPublicKey = false;
		assembly.Write(target);
	}
}