using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using dnlib.DotNet;
using HarmonyLib;

namespace Doorstop
{
	static class Translator
	{
		static Assembly markerAsm;

		static readonly Dictionary<string, Type> typeCache = [];
		static readonly Dictionary<string, Type> typeSigCache = [];
		static readonly Dictionary<Type, MemberInfo[]> memberCache = [];
		static readonly Dictionary<MethodBase, ParameterInfo[]> paramsCache = [];

		static MemberInfo[] GetMembers(Type t)
		{
			if (memberCache.TryGetValue(t, out var cached) == false)
				return memberCache[t] = t.GetMembers(AccessTools.all);
			return cached;
		}

		static ParameterInfo[] GetParams(MethodBase m)
		{
			if (paramsCache.TryGetValue(m, out var cached) == false)
				return paramsCache[m] = m.GetParameters();
			return cached;
		}

		internal static object TranslateRef(object dnRef)
		{
			if (dnRef is string)
				return dnRef;

			if (dnRef is TypeSig sig)
				return TranslateTypeSig(sig);

			if (dnRef is not IMemberRef member)
				return null;

			if (member.IsField)
			{
				var declType = (Type)TranslateRef(member.DeclaringType);
				var field = declType.GetField(member.Name, AccessTools.all);
				return field;
			}

			if (member.IsMethod && member is IMethod method)
			{
				var declType = (Type)TranslateRef(member.DeclaringType);
				var origMembers = GetMembers(declType);

				Type[] genericArgs = null;
				if (method.IsMethodSpec && method is MethodSpec spec)
				{
					method = spec.Method;
					var generic = spec.GenericInstMethodSig;
					genericArgs = generic.GenericArguments.Select(t => (Type)TranslateRef(t)).ToArray();
				}

				var openType = declType.IsGenericType ? declType.GetGenericTypeDefinition() : declType;
				var members = GetMembers(openType);
				MemberInfo ret = null;

				if (genericArgs == null)
				{
					// If a method is unambiguous by name, param count and first param type, return it
					// This is a very good heuristic
					foreach (var m in origMembers.OfType<MethodBase>())
					{
						if (m.Name != method.Name)
							continue;
						if (GetParams(m).Length != method.GetParamCount())
							continue;
						if (GetParams(m).Length > 0 && GetParams(m)[0].ParameterType != (Type)TranslateRef(method.GetParam(0)))
							continue;

						if (ret == null)
							ret = m;
						else
						{
							ret = null;
							break;
						}
					}
				}

				if (ret != null)
				{
					return ret;
				}

				for (var i = 0; i < members.Length; i++)
				{
					var typeMember = members[i];
					if (typeMember is not MethodBase m)
						continue;
					if (MethodSigMatch(m, method) == false)
						continue;

					if (genericArgs != null)
						return (origMembers[i] as MethodInfo).MakeGenericMethod(genericArgs);

					return origMembers[i];
				}

				return null;
			}

			if (member.IsType && member is IType type)
			{
				if (type is TypeSpec spec)
					return TranslateRef(spec.TypeSig);

				var aqn = type.AssemblyQualifiedName;
				if (typeCache.TryGetValue(aqn, out var cached) == false)
					return typeCache[aqn] = Type.GetType(aqn);

				return cached;
			}

			return null;
		}

		static Type TranslateTypeSig(TypeSig sig)
		{
			var aqn = sig.AssemblyQualifiedName;
			if (typeSigCache.TryGetValue(aqn, out var cached))
				return cached;

			if (markerAsm == null)
				markerAsm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("hotswapmarkerassembly"), AssemblyBuilderAccess.ReflectionOnly);

			var flatGenerics = new Queue<TypeSig>();
			CollectGenerics(sig, flatGenerics);

			Assembly AsmResolver(AssemblyName aname)
			{
				if (aname.Name == "<<<NULL>>>")
					return markerAsm;

				return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(asm => asm.GetName().Name == aname.Name);
			}

			Type TypeResolver(Assembly asm, string s, bool flag)
			{
				var generic = flatGenerics.Dequeue();

				if (asm != markerAsm)
					return asm.GetType(s);

				if (generic is GenericSig g)
				{
					Type translated = null;

					if (g.HasOwnerMethod)
						translated = (TranslateRef(g.OwnerMethod) as MethodInfo).GetGenericArguments()[g.Number];
					else if (g.HasOwnerType)
						translated = (TranslateRef(g.OwnerType) as Type).GetGenericArguments()[g.Number];

					return translated;
				}

				return null;
			}

			var result = Type.GetType(aqn, AsmResolver, TypeResolver, false);
			typeSigCache[aqn] = result;
			return result;
		}

		internal static bool MethodSigMatch(MethodBase m, IMethod method)
		{
			return m.Name == method.Name && new SigComparer().Equals(method.Module.Import(m), method);
		}

		static void CollectGenerics(TypeSig sig, Queue<TypeSig> flatGenerics)
		{
			while (sig.Next != null)
				sig = sig.Next;

			flatGenerics.Enqueue(sig);

			if (sig is GenericInstSig generic)
				foreach (var arg in generic.GenericArguments)
					CollectGenerics(arg, flatGenerics);
		}
	}
}
