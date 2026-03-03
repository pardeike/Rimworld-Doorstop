using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using static HarmonyLib.AccessTools;

namespace ILReloaderLib;

// example attribute to mark methods and constructors as reloadable
// copy this class into your own project to create your own attribute
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
internal class ReloadableAttribute : Attribute { }

internal static class Tools
{
	static readonly string reloadableTypeName = typeof(ReloadableAttribute).Name;
	static readonly Dictionary<short, System.Reflection.Emit.OpCode> opcodeCache = CreateOpcodeCache();

	internal delegate void DetourMethodDelegate(MethodBase method, MethodBase replacement);
	internal static readonly DetourMethodDelegate DetourMethod = MethodDelegate<DetourMethodDelegate>(Method("HarmonyLib.PatchTools:DetourMethod"));


	internal static Action<string> Logger = s => Console.WriteLine(s);
	internal static void LogMessage(this string log) => Logger($"[Info] {log}");
	internal static void LogWarning(this string log) => Logger($"[Warn] {log}");
	internal static void LogError(this string log) => Logger($"[Error] {log}");

	internal static bool IsReloadable(this MethodBase method) => method.GetCustomAttributes(true).Any(a => a.GetType().Name == reloadableTypeName);
	internal static bool IsCecilReloadable(this MethodDefinition method) => method.CustomAttributes.Any(a => a.AttributeType.Name == reloadableTypeName);

	static Dictionary<short, System.Reflection.Emit.OpCode> CreateOpcodeCache()
	{
		var cache = new Dictionary<short, System.Reflection.Emit.OpCode>();
		var emitOpcodeFields = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode));
		foreach (var field in emitOpcodeFields)
		{
			var opcode = (System.Reflection.Emit.OpCode)field.GetValue(null);
			cache[opcode.Value] = opcode;
		}
		return cache;
	}

	internal static string WithoutFileExtension(this string filePath)
	{
		var directory = Path.GetDirectoryName(filePath);
		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
		return Path.Combine(directory, fileNameWithoutExtension);
	}

	internal static string Id(this MethodBase member)
	{
		return new StringBuilder(128)
			.Append(member.DeclaringType.FullName)
			.Append('.')
			.Append(member.Name)
			.Append('(')
			.Append(string.Join(", ", member.GetParameters().Select(p => p.ParameterType.FullName)))
			.Append(')')
			.ToString();
	}

	internal static string Id(this MethodDefinition member)
	{
		return new StringBuilder(128)
			.Append(member.DeclaringType.FullName)
			.Append('.')
			.Append(member.Name)
			.Append('(')
			.Append(string.Join(", ", member.Parameters.Select(p => p.ParameterType.FullName)))
			.Append(')')
			.ToString();
	}

	internal static IEnumerable<MethodBase> AllReloadableMembers(this Type type)
	{
		foreach (var member in GetDeclaredMethods(type).Where(IsReloadable))
			yield return member;
		foreach (var member in GetDeclaredConstructors(type).Where(IsReloadable))
			yield return member;
	}

	internal static IEnumerable<MethodDefinition> AllReloadableMembers(this TypeDefinition type)
	{
		foreach (var member in type.GetMethods().Where(IsCecilReloadable))
			yield return member;
		foreach (var member in type.GetConstructors().Where(IsCecilReloadable))
			yield return member;
	}

	internal static bool NamesMatch(AssemblyName a, AssemblyName b)
	{
		if (a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) == false)
			return false;

		var at = a.GetPublicKeyToken();
		var bt = b.GetPublicKeyToken();
		if (bt != null && bt.Length > 0 && TokenEquals(at, bt) == false)
			return false;

		return string.IsNullOrEmpty(b.CultureInfo.Name)
			|| string.Equals(a.CultureInfo.Name ?? "", b.CultureInfo.Name, StringComparison.OrdinalIgnoreCase);
	}

	internal static bool TokenEquals(byte[] a, byte[] b)
	{
		if (a == null || b == null)
			return a == b;
		if (a.Length != b.Length)
			return false;
		for (var i = 0; i < a.Length; i++)
			if (a[i] != b[i])
				return false;
		return true;
	}

	internal static System.Reflection.Emit.OpCode ConvertOpcode(Mono.Cecil.Cil.OpCode opcode)
	{
		if (opcodeCache.TryGetValue(opcode.Value, out var emitOpcode))
			return emitOpcode;
		throw new NotSupportedException($"Opcode {opcode.Name} (0x{opcode.Value:X4}) is not supported");
	}

	internal static object ConvertOperand(object operand, ILGenerator il)
	{
		if (operand is MethodReference methodRef)
			return ResolveMethodBase(methodRef);
		if (operand is PropertyReference property)
			return ResolveProperty(property);
		if (operand is FieldReference field)
			return ResolveField(field);
		if (operand is TypeReference type)
			return ResolveType(type);
		if (operand is VariableDefinition variable)
			return il.DeclareLocal(ResolveType(variable.VariableType), variable.IsPinned);
		if (operand is ParameterDefinition parameter)
			return parameter.Index + 1;
		return operand;
	}

	internal static MethodBase ResolveMethodBase(MethodReference methodReference)
	{
		var declaringType = ResolveType(methodReference.DeclaringType);
		var parameters = methodReference.Parameters.ToArray();
		var parameterTypes = new Type[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
			parameterTypes[i] = ResolveType(parameters[i].ParameterType);
		if (methodReference.Name is ".ctor" or ".cctor")
			return DeclaredConstructor(declaringType, parameterTypes);
		if (methodReference is GenericInstanceMethod genericMethod)
		{
			var genericTypes = new Type[genericMethod.GenericArguments.Count];
			for (var i = 0; i < genericMethod.GenericArguments.Count; i++)
				genericTypes[i] = ResolveType(genericMethod.GenericArguments[i]);
			return DeclaredMethod(declaringType, methodReference.Name, parameterTypes, genericTypes);
		}
		return DeclaredMethod(declaringType, methodReference.Name, parameterTypes);
	}

	internal static PropertyInfo ResolveProperty(PropertyReference property)
	{
		var declaringType = ResolveType(property.DeclaringType);
		return DeclaredProperty(declaringType, property.Name);
	}

	internal static FieldInfo ResolveField(FieldReference field)
	{
		var declaringType = ResolveType(field.DeclaringType);
		return DeclaredField(declaringType, field.Name);
	}

	internal static Type ResolveType(TypeReference typeReference)
	{
		Type type;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			type = asm.GetType(typeReference.FullName);
			if (type != null)
			{
				if (type.Assembly.ReflectionOnly)
					throw new TypeLoadException($"Resolved type {typeReference.FullName} to assembly {asm.FullName}, but that assembly is reflection-only");
				return type;
			}
		}

		type = Type.GetType(typeReference.FullName);
		if (type != null)
		{
			if (type.Assembly.ReflectionOnly)
				throw new TypeLoadException($"Resolved type {typeReference.FullName} to type within a reflection-only assembly");
			return type;
		}

		throw new TypeLoadException($"Could not resolve type {typeReference.FullName}");
	}
}
