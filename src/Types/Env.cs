using System.Collections.Generic;
using System;

namespace mal.Types
{
	public class Env
	{
		public readonly Env? Outer;
		private Dictionary<string, Value> symbols = new();
		public event Action<string, Value>? DefineHook;
		public event Func<string, Value?>? LookupHook;

		public Env(Env? outer = null)
		{
			Outer = outer;
		}

		public Value this[string name]
		{
			set => Define(name, value);
		}

		public void Define(string name, Value value)
		{
			DefineHook?.Invoke(name, value);
			symbols[name] = value;
		}

		public Value? Lookup(string name)
		{
			bool found = false;
			Value? v;
			Env? env = this;
			do
			{
				if ((v = env.LookupHook?.Invoke(name)) != null)
					break;

				found = env.symbols.TryGetValue(name, out v);
				env = env.Outer;
			}
			while (env != null && !found);

			return v;
		}

		public Value Get(string name) => Lookup(name) ?? throw new RuntimeError($"Name {name} not defined");
	}
}
