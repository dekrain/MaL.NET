namespace mal.Types
{
	// A reference type which holds a mutable reference to a value
	public class Atom : Value
	{
		public Value Value { get; set; }

		public override ValueTypeCode TypeCode => ValueTypeCode.Atom;

		protected override bool EqualsImpl(Value other) => Value == ((Atom)other).Value;

		public Atom(Value? value = null)
		{
			Value = value ?? Singleton.Nil;
		}
	}
}
