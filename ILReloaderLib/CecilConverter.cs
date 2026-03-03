using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection.Emit;

namespace ILReloaderLib;

static class CecilConverter
{
	static readonly Dictionary<string, MethodDefinition> replacementMembers = [];

	internal static void Register(string methodId, MethodDefinition replacementMethod)
		=> replacementMembers[methodId] = replacementMethod;

	internal static CodeInstruction[] Convert(ILGenerator il, string methodId)
	{
		if (replacementMembers.TryGetValue(methodId, out var replacementMethod) == false)
		{
			$"could not find replacement method".LogError();
			return [];
		}

		var body = replacementMethod.Body;
		var cecilInstructions = body.Instructions.ToArray();

		var branchTargets = new HashSet<int>();
		for (var i = 0; i < cecilInstructions.Length; i++)
		{
			var cecilInstr = cecilInstructions[i];
			if (cecilInstr.Operand is Instruction targetInstr)
			{
				var targetIndex = Array.IndexOf(cecilInstructions, targetInstr);
				if (targetIndex >= 0)
					_ = branchTargets.Add(targetIndex);
			}
			else if (cecilInstr.Operand is Instruction[] switchTargets)
			{
				foreach (var target in switchTargets)
				{
					var targetIndex = Array.IndexOf(cecilInstructions, target);
					if (targetIndex >= 0)
						_ = branchTargets.Add(targetIndex);
				}
			}
		}

		foreach (var handler in body.ExceptionHandlers)
		{
			var tryStartIndex = Array.IndexOf(cecilInstructions, handler.TryStart);
			var tryEndIndex = Array.IndexOf(cecilInstructions, handler.TryEnd);
			var handlerStartIndex = Array.IndexOf(cecilInstructions, handler.HandlerStart);
			var handlerEndIndex = Array.IndexOf(cecilInstructions, handler.HandlerEnd);

			if (tryStartIndex >= 0) _ = branchTargets.Add(tryStartIndex);
			if (tryEndIndex >= 0) _ = branchTargets.Add(tryEndIndex);
			if (handlerStartIndex >= 0) _ = branchTargets.Add(handlerStartIndex);
			if (handlerEndIndex >= 0) _ = branchTargets.Add(handlerEndIndex);

			if (handler.FilterStart != null)
			{
				var filterStartIndex = Array.IndexOf(cecilInstructions, handler.FilterStart);
				if (filterStartIndex >= 0) _ = branchTargets.Add(filterStartIndex);
			}
		}

		var labels = new Dictionary<int, Label>();
		foreach (var targetIndex in branchTargets)
			labels[targetIndex] = il.DefineLabel();

		var harmonyInstructions = new CodeInstruction[cecilInstructions.Length];
		for (var i = 0; i < cecilInstructions.Length; i++)
		{
			var cecilInstr = cecilInstructions[i];
			var opcode = Tools.ConvertOpcode(cecilInstr.OpCode);
			object operand = null;

			if (cecilInstr.Operand is Instruction targetInstr)
			{
				var targetIndex = Array.IndexOf(cecilInstructions, targetInstr);
				if (targetIndex >= 0 && labels.TryGetValue(targetIndex, out var targetLabel))
					operand = targetLabel;
			}
			else if (cecilInstr.Operand is Instruction[] switchTargets)
			{
				var switchLabels = new Label[switchTargets.Length];
				for (var j = 0; j < switchTargets.Length; j++)
				{
					var targetIndex = Array.IndexOf(cecilInstructions, switchTargets[j]);
					if (targetIndex >= 0 && labels.TryGetValue(targetIndex, out var targetLabel))
						switchLabels[j] = targetLabel;
				}
				operand = switchLabels;
			}
			else
				operand = Tools.ConvertOperand(cecilInstr.Operand, il);

			var harmonyInstr = new CodeInstruction(opcode, operand);
			if (labels.TryGetValue(i, out var label))
				harmonyInstr.labels.Add(label);
			harmonyInstructions[i] = harmonyInstr;
		}

		foreach (var handler in body.ExceptionHandlers)
		{
			var tryStartIndex = Array.IndexOf(cecilInstructions, handler.TryStart);
			var tryEndIndex = Array.IndexOf(cecilInstructions, handler.TryEnd);
			var handlerStartIndex = Array.IndexOf(cecilInstructions, handler.HandlerStart);
			var handlerEndIndex = Array.IndexOf(cecilInstructions, handler.HandlerEnd);

			if (tryStartIndex >= 0 && tryEndIndex >= 0 && handlerStartIndex >= 0 && handlerEndIndex >= 0)
			{
				var blockType = handler.HandlerType switch
				{
					ExceptionHandlerType.Catch => ExceptionBlockType.BeginCatchBlock,
					ExceptionHandlerType.Finally => ExceptionBlockType.BeginFinallyBlock,
					ExceptionHandlerType.Fault => ExceptionBlockType.BeginFaultBlock,
					ExceptionHandlerType.Filter => ExceptionBlockType.BeginExceptFilterBlock,
					_ => ExceptionBlockType.BeginExceptionBlock
				};

				var beginBlock = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock, handler.CatchType != null ? Tools.ResolveType(handler.CatchType) : null);
				harmonyInstructions[tryStartIndex].blocks.Add(beginBlock);

				var handlerBlock = new ExceptionBlock(blockType, handler.CatchType != null ? Tools.ResolveType(handler.CatchType) : null);
				harmonyInstructions[handlerStartIndex].blocks.Add(handlerBlock);

				if (handlerEndIndex < harmonyInstructions.Length)
				{
					var endBlock = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
					harmonyInstructions[handlerEndIndex].blocks.Add(endBlock);
				}
			}
		}

		return harmonyInstructions;
	}
}
