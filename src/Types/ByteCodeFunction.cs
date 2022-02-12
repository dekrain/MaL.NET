using mal.Jit;

namespace mal.Types
{
	public class ByteCodeFunction : Value, IInvocable
	{
		public readonly Env Scope;
		internal ByteCode? Bytecode;

		internal ByteCodeFunction(Env scope, ByteCode? bytecode = null)
		{
			Scope = scope;
			Bytecode = bytecode;
		}

		public virtual Value Invoke(Interpreter interpreter, params Value[] args)
		{
			return interpreter.EvalFunction(this, Scope, args);
		}

		public override ValueTypeCode TypeCode => ValueTypeCode.BuiltinFunction;
	}
}
