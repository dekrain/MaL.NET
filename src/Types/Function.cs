using System;
using System.Linq;
using mal.Jit;

namespace mal.Types
{
	// MaL function type
	public class Function : ByteCodeFunction, IInvocable
	{
		public readonly Value Body;
		public readonly string[]? Params;
		public readonly string? VariadicParam;

		public bool HasInnerScope => Params != null;
		public static string[] EmptyParams => Array.Empty<string>();

		public Function(Value body, Env closure, JITInterpreterContext jitContext, string[]? parameters = null)
			: base(closure)
		{
			Body = body;
			Params = parameters;
			if (Params is not null)
			{
				int varArgIdx = Array.IndexOf(Params, "&");
				if (varArgIdx != -1)
				{
					if (varArgIdx != Params.Length - 2)
						throw new RuntimeError("Function variadic variable must be last");
					VariadicParam = Params[^1];
					Array.Resize(ref Params, Params.Length - 2);
				}
				Bytecode = jitContext.FunctionStub;
			}
			else
			{
				Bytecode = jitContext.ScriptStub;
			}
		}

		public override ValueTypeCode TypeCode => ValueTypeCode.MalFunction;
	}
}
