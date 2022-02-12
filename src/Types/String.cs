namespace mal.Types
{
	public abstract class StringData : Value
	{
		public readonly string Value;

		public bool IsInterned => string.IsInterned(Value) != null;

		public StringData(string value)
		{
			Value = value;
		}

		protected override bool EqualsImpl(Value other) => Value == ((StringData)other).Value;
	}

	public class String : StringData
	{
		public String(string value) : base(value) {}

		public override ValueTypeCode TypeCode => ValueTypeCode.String;
	}

	public class Symbol : StringData
	{
		public Symbol(string value) : base(value) {}

		public override ValueTypeCode TypeCode => ValueTypeCode.Symbol;
	}

	public class Keyword : StringData
	{
		public Keyword(string value) : base(value) {}

		public override ValueTypeCode TypeCode => ValueTypeCode.Keyword;
	}
}
