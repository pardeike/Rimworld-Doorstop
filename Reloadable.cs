using System;

namespace Doorstop
{
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
	public class ReloadableAttribute : Attribute
	{
	}
}