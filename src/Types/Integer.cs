namespace mal.Types
{
	public class Integer : Value
	{
		public readonly int Value;

		public Integer(int value)
		{
			Value = value;
		}

		public override bool IsTruthy => Value != 0;

		protected override bool EqualsImpl(Value other) => Value == ((Integer)other).Value;

		public override ValueTypeCode TypeCode => ValueTypeCode.Integer;
	}

	public static class IntegerArithmetic
	{
		public static Integer Add(params Integer[] nums)
		{
			int sum = 0;
			foreach (var item in nums)
				sum += item.Value;
			return new Integer(sum);
		}

		public static Integer Subtract(Integer first, params Integer[] nums)
		{
			int res = first.Value;
			foreach (var item in nums)
				res -= item.Value;
			return new Integer(res);
		}

		public static Integer Multiply(params Integer[] nums)
		{
			int sum = 1;
			foreach (var item in nums)
				sum *= item.Value;
			return new Integer(sum);
		}

		public static Integer Divide(Integer first, params Integer[] nums)
		{
			int res = first.Value;
			foreach (var item in nums)
			{
				if (item.Value == 0)
					throw new RuntimeError("Division by 0");
				res /= item.Value;
			}
			return new Integer(res);
		}
	}
}
