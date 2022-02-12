using mal.Types;
using mal.Extensions.EnumExtensions;

namespace mal.Jit
{
	[System.AttributeUsage(System.AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class ConstantIndexAttribute : System.Attribute
	{
		public ConstantIndexAttribute()
		{
		}
	}

	[System.AttributeUsage(System.AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class NameIndexAttribute : System.Attribute
	{
		public NameIndexAttribute()
		{
		}
	}

	[System.AttributeUsage(System.AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class FunctionParametersAttribute : System.Attribute
	{
		public FunctionParametersAttribute()
		{
		}
	}

	[System.AttributeUsage(System.AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class BranchAttribute : System.Attribute
	{
		public BranchAttribute()
		{
		}
	}

	public enum OpCode
	{
		[ConstantIndex]
		LoadConst, // Arg: #constant
		[NameIndex]
		LoadName, // Arg: #name
		LoadFast, // Arg: #slot
		PrepareForTailCall,
		Call, // Arg: #args, -1 means the arg count is on top and callee is second to top
		BuildList, // Arg: #elements, -1 means the count is on top
		BuildVector, // Arg: #elements, -1 means the count is on top
		DupTop, // Arg: #new_copies
		Discard, // Arg: #elements
		[FunctionParameters]
		MakeFunction, // Arg: (HiHalf)#params (LoHalf)&names_start
		[Branch]
		Branch, // Arg: &dest_pc
		[Branch]
		BranchIfTrue, // Arg: &dest_pc
		[Branch]
		BranchIfFalse, // Arg: &dest_pc
		[NameIndex]
		DefineName, // Arg: #name !Warning: [This instruction may have side effects]
		StoreFast, // Arg: #slot
		PushEnv,
		PopEnv,
		AssertTrue, // Arg: #assert
		AssertFalse, // Arg: #assert
		RotateTop, // Arg: #positions
		UnpackCons, // => ...[restList] [firstElement]
		BuildCons, // [cdr] [car] => [list]

		TypeCheck, // Arg: #type

		Add,
		Neg,
		Mul,
		AddImm, // Arg: value

		CmpEq,
		CmpLt,

		// Atom/Reference operations
		RefCreate,
		RefLoad,
		RefStore,

		CompileFunction, // Arg: [script]0 [function]1
	}

	public enum Assertion : int
	{
		ArgumentCountMismatch,
		EmptyFunctionParams,
		ArgumentTypeMismatch,
	}

	public static class AssertionMessages
	{
		public static readonly string[] Messages =
		{
			"Function argument count mismatch",
			"Function with no parameters called with non-zero parameters",
			"Wrong argument type passed",
		};

		public static string Get(Assertion assertion) => Messages[(int)assertion];
	}

	public readonly struct Instruction
	{
		public readonly OpCode op;
		public readonly int arg;

		#pragma warning disable IDE1006
		public uint uarg => (uint)arg;
		#pragma warning restore IDE1006
		public byte Byte0 => (byte)(uarg & 0xFF);
		public byte Byte1 => (byte)((uarg >> 8) & 0xFF);
		public byte Byte2 => (byte)((uarg >> 16) & 0xFF);
		public byte Byte3 => (byte)((uarg >> 24) & 0xFF);
		public ushort LoHalf => (ushort)(uarg & 0xFFFF);
		public ushort HiHalf => (ushort)((uarg >> 16) & 0xFFFF);
		public Assertion AssertId => (Assertion)arg;

		public Instruction(OpCode op, int arg)
		{
			this.op = op;
			this.arg = arg;
		}
	}

	public class ByteCode
	{
		public readonly Instruction[] Instructions;
		public readonly Value[] Constants;
		public readonly string[] Names;

		internal ByteCode(Instruction[] instructions, Value[] constants, string[] names)
		{
			Instructions = instructions;
			Constants = constants;
			Names = names;
		}

		public void Dump()
		{
			System.Console.WriteLine("Bytecode dump:");
			System.Collections.Generic.SortedSet<int> branchTargetsSet = new();
			for (int idx = 0; idx != Instructions.Length; ++idx)
			{
				ref readonly var instr = ref Instructions[idx];
				if (instr.op.HasCustomAttribute(typeof(BranchAttribute)))
				{
					branchTargetsSet.Add(instr.arg);
				}
			}
			var branchTargets = new int[branchTargetsSet.Count];
			branchTargetsSet.CopyTo(branchTargets);
			int branchIdx = 0;
			int? nextBranch = branchIdx >= branchTargets.Length ? null : branchTargets[branchIdx];
			for (int idx = 0; idx != Instructions.Length; ++idx)
			{
				ref readonly var instr = ref Instructions[idx];
				if (idx == nextBranch)
				{
					System.Console.WriteLine($"POS{idx:x}:");
					++branchIdx;
					nextBranch = branchIdx >= branchTargets.Length ? null : branchTargets[branchIdx];
				}
				System.Console.Write($"[{instr.op}, {instr.arg:X}]");
				if (instr.op.HasCustomAttribute(typeof(ConstantIndexAttribute)))
				{
					System.Console.Write(" constant: ");
					Interpreter.ProgramInterpreter.Printer.PrintValue(Constants[instr.arg]);
				}
				else if (instr.op.HasCustomAttribute(typeof(NameIndexAttribute)))
				{
					System.Console.Write($" name: {Names[instr.arg]}");
				}
				else if (instr.op.HasCustomAttribute(typeof(FunctionParametersAttribute)))
				{
					System.Console.Write($" params#{instr.HiHalf}: {string.Join(' ', Names[instr.LoHalf..(instr.LoHalf+instr.HiHalf)])}");
				}
				else if (instr.op.HasCustomAttribute(typeof(BranchAttribute)))
				{
					System.Console.Write($" -> POS{instr.arg:x}");
				}
				System.Console.WriteLine();
			}
			// Check tail label
			if (nextBranch == Instructions.Length)
			{
				System.Console.WriteLine($"POS{nextBranch:x}:");
				System.Console.WriteLine($"<End>");
			}
		}
	}
}
