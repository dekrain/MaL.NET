using System;

namespace mal
{
	public class RuntimeError : Exception
	{
		public RuntimeError(string message)
			: base(message)
		{}
	}
}
