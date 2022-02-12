using System;

namespace mal.Types
{
	public abstract class Value : IEquatable<Value>
	{
		public bool IsNil => Singleton.IsNilValue(this);
		public bool IsTrue => Singleton.IsTrueValue(this);
		public bool IsFalse => Singleton.IsFalseValue(this);
		public bool IsBool => IsTrue || IsFalse;

		public virtual bool IsTruthy => true;

		public bool MetaEquals(Value? other) => Equals(other);

		public bool Equals(Value? other)
		{
			return other != null && GetType() == other.GetType() && EqualsImpl(other);
		}

		public bool CheckType(ValueTypeCode typeCode)
		{
			if (typeCode >= ValueTypeCode.SpecialFirst)
			{
				switch (typeCode)
				{
					case ValueTypeCode.Sequence:
						return TypeCode == ValueTypeCode.List || TypeCode == ValueTypeCode.Vector;
					case ValueTypeCode.EmptySequence:
						if (this is ListBase list)
							return list.Top is null;
						return false;
					case ValueTypeCode.Callable:
						return TypeCode == ValueTypeCode.MalFunction || TypeCode == ValueTypeCode.BuiltinFunction;
					case ValueTypeCode.CharSequence:
						return TypeCode == ValueTypeCode.String || TypeCode == ValueTypeCode.Keyword || TypeCode == ValueTypeCode.Symbol;
				}
			}
			return typeCode == TypeCode;
		}

		protected virtual bool EqualsImpl(Value other) => object.ReferenceEquals(this, other);

		public abstract ValueTypeCode TypeCode { get; }

		public static bool operator==(Value? lhs, Value? rhs) => lhs?.Equals(rhs) ?? rhs is null;
		public static bool operator!=(Value? lhs, Value? rhs) => !(lhs == rhs);
	}

	public enum ValueTypeCode
	{
		Nil,
		Boolean,
		Integer,
		List,
		Vector,
		HashMap,
		String,
		Keyword,
		Symbol,
		MalFunction,
		BuiltinFunction,
		Atom,

		// Specials
		SpecialFirst,
		Sequence = SpecialFirst,
		EmptySequence,
		Callable,
		CharSequence,
	}

	public enum SingletonKind
	{
		Nil,
		True,
		False,
	}

	public class Singleton : Value
	{

		public SingletonKind Kind;

		public static Singleton Nil => new Singleton{ Kind = SingletonKind.Nil };
		public static Singleton True => new Singleton{ Kind = SingletonKind.True };
		public static Singleton False => new Singleton{ Kind = SingletonKind.False };

		public override bool IsTruthy => OfKind(SingletonKind.True);

		public static bool IsNilValue(Value v) => v is Singleton n && n.OfKind(SingletonKind.Nil);
		public static bool IsTrueValue(Value v) => v is Singleton n && n.OfKind(SingletonKind.True);
		public static bool IsFalseValue(Value v) => v is Singleton n && n.OfKind(SingletonKind.False);

		public bool OfKind(SingletonKind kind) => Kind == kind;

		protected override bool EqualsImpl(Value other) => Kind == ((Singleton)other).Kind;

		public override ValueTypeCode TypeCode => OfKind(SingletonKind.Nil) ? ValueTypeCode.Nil : ValueTypeCode.Boolean;
	}
}
