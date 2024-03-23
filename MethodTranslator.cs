using dnlib.DotNet.Emit;
using System;
using System.Linq;
using System.Reflection.Emit;
using dnlib.DotNet;
using OperandType = dnlib.DotNet.Emit.OperandType;
using MonoMod.Utils;

namespace Doorstop
{
	internal static class MethodTranslator
	{
		internal static void TranslateLocals(CilBody methodBody, ILGenerator ilGen)
		{
			foreach (var local in methodBody.Variables)
				ilGen.DeclareLocal((Type)Translator.TranslateRef(local.Type));
		}

		internal static void TranslateRefs(CilBody methodBody, byte[] newCode, DynamicMethodDefinition replacement)
		{
			var pos = 0;
			foreach (var inst in methodBody.Instructions)
			{
				switch (inst.OpCode.OperandType)
				{
					case OperandType.InlineString:
					case OperandType.InlineType:
					case OperandType.InlineMethod:
					case OperandType.InlineField:
					case OperandType.InlineSig:
					case OperandType.InlineTok:
						pos += inst.OpCode.Size;

						var watch = inst.Operand switch
						{
							IType => "IType",
							IMethod => "IMethod",
							_ => "OtherOperand"
						};

						var @ref = Translator.TranslateRef(inst.Operand)
							?? throw new NullReferenceException($"Null translation {inst.Operand} {inst.Operand.GetType()}");

						var token = replacement.AddRef(@ref);
						newCode[pos++] = (byte)(token & 255);
						newCode[pos++] = (byte)(token >> 8 & 255);
						newCode[pos++] = (byte)(token >> 16 & 255);
						newCode[pos++] = (byte)(token >> 24 & 255);

						break;
					default:
						pos += inst.GetSize();
						break;
				}
			}
		}

		internal static void TranslateExceptions(CilBody methodBody, ILGenerator ilGen)
		{
			var dnhandlers = methodBody.ExceptionHandlers.Reverse().ToArray();
			var exinfos = ilGen.ex_handlers = new ILExceptionInfo[dnhandlers.Length];

			for (int i = 0; i < dnhandlers.Length; i++)
			{
				var ex = dnhandlers[i];
				var start = (int)ex.TryStart.Offset;
				var end = (int)ex.TryEnd.Offset;
				var len = end - start;

				exinfos[i].start = start;
				exinfos[i].len = len;

				var handlerStart = (int)ex.HandlerStart.Offset;
				var handlerEnd = (int)ex.HandlerEnd.Offset;
				var handlerLen = handlerEnd - handlerStart;

				var catchType = (Type)null;
				var filterOffset = 0;

				if (ex.CatchType != null)
					catchType = (Type)Translator.TranslateRef(ex.CatchType);
				else if (ex.FilterStart != null)
					filterOffset = (int)ex.FilterStart.Offset;

				exinfos[i].handlers =
				[
					new()
					{
						extype = catchType,
						type = (int)ex.HandlerType,
						start = handlerStart,
						len = handlerLen,
						filter_offset = filterOffset
					}
				];
			}
		}
	}
}
