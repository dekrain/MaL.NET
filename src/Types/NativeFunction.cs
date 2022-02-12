using System;
using System.Linq;

namespace mal.Types
{
	// A native function reference, that cannot be optimized
	public class NativeFunction : Value, IInvocable
	{
		public delegate Value Function(params Value[] args);
		Function function;
		Type[]? staticParams;
		Type? variadicParam;

		public NativeFunction(Function function, Type[]? typeSig = null)
		{
			this.function = function;
			ParseSignature(typeSig);
		}

		/*public static NativeFunction New<D>(D function) where D : Delegate
		{
			return new NativeFunction(function);
		}*/

		public Value Invoke(Interpreter interpreter, params Value[] args)
		{
			TypeCheck(args);
			return (Value)function.Invoke(args)!;
		}

		public override ValueTypeCode TypeCode => ValueTypeCode.BuiltinFunction;

		private void ParseSignature(Type[]? signature)
		{
			if (signature == null)
				return;
			int end = 0;
			foreach (var type in signature)
			{
				if (variadicParam != null)
					throw new RuntimeError("Variadic parameter must be last");
				if (type.IsSZArray)
				{
					variadicParam = ValidateType(type.GetElementType()!);
					continue;
				}
				ValidateType(type);
				++end;
			}
			staticParams = signature.Take(end).ToArray();
		}

		private Type ValidateType(Type type)
		{
			if (!type.IsAssignableTo(typeof(Value)))
				throw new RuntimeError("Type must be a MaL type");
			return type;
		}

		private void TypeCheck(Value[] args)
		{
			if (staticParams == null)
				return;
			if (args.Length < staticParams.Length)
				throw new RuntimeError("Too few arguments passed");
			else if (variadicParam == null && args.Length > staticParams.Length)
				throw new RuntimeError("Too many arguments passed");
			foreach (var (arg, sig) in args.Zip(staticParams))
			{
				if (!sig.IsAssignableFrom(arg.GetType()))
					throw new RuntimeError("Wrong argument type passed");
			}
			foreach (var arg in args.Skip(staticParams.Length))
			{
				if (!variadicParam!.IsAssignableFrom(arg.GetType()))
					throw new RuntimeError("Wrong variadic argument type passed");
			}
		}
	}
}
