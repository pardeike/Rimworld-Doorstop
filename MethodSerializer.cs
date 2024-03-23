using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using dnlib.DotNet.Writer;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Doorstop
{
	internal static class MethodSerializer
	{
		private class TokenProvider : ITokenProvider
		{
			public void Error(string message)
			{
			}

			public MDToken GetToken(object o)
			{
				if (o is string)
					return new MDToken((Table)0x70, 1);
				else if (o is IMDTokenProvider token)
					return token.MDToken;
				else if (o is StandAloneSig sig)
					return sig.MDToken;

				return new MDToken();
			}

			public MDToken GetToken(IList<TypeSig> locals, uint origToken)
			{
				return new MDToken(origToken);
			}
		}

		static readonly FieldInfo codeSizeField = AccessTools.Field(typeof(MethodBodyWriter), "codeSize");

		internal static byte[] SerializeInstructions(CilBody body)
		{
			var writer = new MethodBodyWriter(new TokenProvider(), body);
			writer.Write();
			var codeSize = (int)(uint)codeSizeField.GetValue(writer);
			var newCode = new byte[codeSize];
			Array.Copy(writer.Code, writer.Code.Length - codeSize, newCode, 0, codeSize);
			return newCode;
		}
	}
}
