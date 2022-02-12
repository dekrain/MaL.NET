using System;
using System.Collections.Generic;
using System.Linq;
using mal.Jit;
using mal.Types;

using Label = mal.Jit.JIT.CompilerContext.Label;
using CompilerContext = mal.Jit.JIT.CompilerContext;
using System.IO;

namespace mal
{
	public static class CoreLib
	{

		private static readonly Dictionary<string, NativeFunction> nativeHelpers = new Dictionary<string, NativeFunction>
		{
			["-"] = new NativeFunction(args => IntegerArithmetic.Subtract((Integer)args[0], args.AsSpan().Slice(1).ToArray().Cast<Integer>().ToArray()), new[] {typeof(Integer), typeof(Integer[])}),
			["+"] = new NativeFunction(args => IntegerArithmetic.Add(args.Cast<Integer>().ToArray()), new[] {typeof(Integer[])}),
		};

		public static void CompileBuiltins(this Interpreter interpreter)
		{
			void Add(string name, Action<CompilerContext> builder)
			{
				interpreter.GlobalEnvironment[name] = new ByteCodeFunction(interpreter.GlobalEnvironment, JIT.BuildFunction(builder));
			}

			void u_FallbackOp(CompilerContext c, string fallbackName, Action<CompilerContext> inner)
			{
				Label fallback = new(), exit = new();

				c.Emit(OpCode.DupTop, 1);
				c.EmitInt(OpCode.LoadConst, 2);
				c.Emit(OpCode.CmpEq);
				c.EmitBranch(OpCode.BranchIfFalse, ref fallback);
				c.Emit(OpCode.Discard, 1);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Integer);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Integer);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RotateTop, 1);
				inner(c);
				c.EmitBranch(OpCode.Branch, ref exit);
				c.BindLabel(ref fallback);
				c.EmitConst(OpCode.LoadConst, nativeHelpers[fallbackName]);
				c.Emit(OpCode.RotateTop, 1);
				// No tail call because it always calls a native function
				c.Emit(OpCode.Call, -1);
				c.BindLabel(ref exit);
			}

			Add("+", c => u_FallbackOp(c, "+", c =>
			{
				c.Emit(OpCode.Add);
			}));

			Add("-", c => u_FallbackOp(c, "-", c =>
			{
				c.Emit(OpCode.Neg);
				c.Emit(OpCode.Add);
			}));

			void u_BinaryIntOp(CompilerContext c)
			{
				c.EmitInt(OpCode.LoadConst, 2);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Integer);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Integer);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RotateTop, 1);
			}

			void u_Negate(CompilerContext c)
			{
				Label bFalse = new(), bEnd = new();

				c.EmitBranch(OpCode.BranchIfFalse, ref bFalse);
				c.EmitConst(OpCode.LoadConst, Singleton.False);
				c.EmitBranch(OpCode.Branch, ref bEnd);
				c.BindLabel(ref bFalse);
				c.EmitConst(OpCode.LoadConst, Singleton.True);
				c.BindLabel(ref bEnd);
			}

			Add("<", c =>
			{
				u_BinaryIntOp(c);
				c.Emit(OpCode.CmpLt);
			});

			Add(">", c =>
			{
				u_BinaryIntOp(c);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.CmpLt);
			});

			Add("<=", c =>
			{
				u_BinaryIntOp(c);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.CmpLt);
				u_Negate(c);
			});

			Add(">=", c =>
			{
				u_BinaryIntOp(c);
				c.Emit(OpCode.CmpLt);
				u_Negate(c);
			});

			Add("=", c =>
			{
				c.EmitInt(OpCode.LoadConst, 2);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.CmpEq);
			});

			Add("neg", c =>
			{
				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Integer);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.Neg);
			});

			Add("not", c =>
			{
				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				u_Negate(c);
			});

			Add("list", c =>
			{
				// Yes, that's it!
				c.Emit(OpCode.BuildList, -1);
			});

			Add("vector", c =>
			{
				c.Emit(OpCode.BuildVector, -1);
			});

			Add("apply", c =>
			{
				Label loopBegin = new(), loopEnd = new();

				// [callee] [argList] [#args] =>
				c.EmitInt(OpCode.LoadConst, 2);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.StoreFast, 0);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Sequence);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				// => [argList]

				c.EmitInt(OpCode.LoadConst, 0);
				c.Emit(OpCode.RotateTop, 1);
				// [callArgs...] [#callArgs] [argList] =>
				c.Emit(OpCode.TypeCheck, ValueTypeCode.EmptySequence);
				c.EmitBranch(OpCode.BranchIfTrue, ref loopEnd);
				c.BindLabel(ref loopBegin);
				c.Emit(OpCode.UnpackCons);
				// => [callArgs...] [#callArgs] [restArgs] [firstEl]
				c.Emit(OpCode.RotateTop, 2);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.AddImm, 1);
				c.Emit(OpCode.RotateTop, 1);
				// => [callArgs...] [newArg] [#callArgs+1] [restArgs]
				c.Emit(OpCode.TypeCheck, ValueTypeCode.EmptySequence);
				c.EmitBranch(OpCode.BranchIfFalse, ref loopBegin);

				// [callArgs...] [#callArgs] [emptyList] =>
				c.BindLabel(ref loopEnd);
				c.Emit(OpCode.Discard, 1);
				c.Emit(OpCode.LoadFast, 0);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.PrepareForTailCall);
				c.Emit(OpCode.Call, -1);
			});

			Add("count", c =>
			{
				Label loop = new(), loopEnd = new(), notSeq = new(), end = new();

				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Sequence);
				c.EmitBranch(OpCode.BranchIfFalse, ref notSeq);

				c.EmitInt(OpCode.LoadConst, 0);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.EmptySequence);
				c.EmitBranch(OpCode.BranchIfTrue, ref loopEnd);
				c.BindLabel(ref loop);
				c.Emit(OpCode.UnpackCons);
				c.Emit(OpCode.Discard, 1);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.AddImm, 1);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.EmptySequence);
				c.EmitBranch(OpCode.BranchIfFalse, ref loop);

				c.BindLabel(ref loopEnd);
				c.Emit(OpCode.Discard, 1);
				c.EmitBranch(OpCode.Branch, ref end);

				c.BindLabel(ref notSeq);
				c.Emit(OpCode.Discard, 1);
				c.EmitConst(OpCode.LoadConst, Singleton.Nil);

				c.BindLabel(ref end);
			});

			Add("cons", c =>
			{
				c.EmitInt(OpCode.LoadConst, 2);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Sequence);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.BuildCons);
			});

			void u_CheckType(CompilerContext c, ValueTypeCode typeCode)
			{
				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.TypeCheck, typeCode);
				c.Emit(OpCode.RotateTop, 1);
				c.Emit(OpCode.Discard, 1);
			}

			Add("list?", c => u_CheckType(c, ValueTypeCode.List));
			Add("vector?", c => u_CheckType(c, ValueTypeCode.Vector));
			Add("seq?", c => u_CheckType(c, ValueTypeCode.Sequence));
			Add("empty?", c => u_CheckType(c, ValueTypeCode.EmptySequence));
			Add("nil?", c => u_CheckType(c, ValueTypeCode.Nil));
			Add("atom?", c => u_CheckType(c, ValueTypeCode.Atom));

			Add("eval", c =>
			{
				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.MakeFunction, -1);
				c.Emit(OpCode.PrepareForTailCall);
				c.Emit(OpCode.Call, 0);
			});

			Add("atom", c =>
			{
				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.RefCreate);
			});

			Add("deref", c =>
			{
				c.EmitInt(OpCode.LoadConst, 1);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Atom);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RefLoad);
			});

			Add("reset!", c =>
			{
				c.EmitInt(OpCode.LoadConst, 2);
				c.Emit(OpCode.CmpEq);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
				c.Emit(OpCode.DupTop, 1);
				c.Emit(OpCode.RotateTop, -2);
				c.Emit(OpCode.TypeCheck, ValueTypeCode.Atom);
				c.Emit(OpCode.AssertTrue, Assertion.ArgumentTypeMismatch);
				c.Emit(OpCode.RefStore);
			});
		}

		public static void InstallNativeBuiltins(this Interpreter interpreter)
		{
			var env = interpreter.GlobalEnvironment;

			env["*"] = new NativeFunction(args => IntegerArithmetic.Multiply(args.Cast<Integer>().ToArray()), new[] {typeof(Integer[])});
			env["/"] = new NativeFunction(args => IntegerArithmetic.Divide((Integer)args[0], args.AsSpan().Slice(1).ToArray().Cast<Integer>().ToArray()), new[] {typeof(Integer), typeof(Integer[])});

			env["pr-str"] = new NativeFunction(args =>
			{
				var writer = new StringWriter();
				var printer = new Printer(writer, repr: true);
				bool first = true;
				foreach (var value in args)
				{
					if (first)
						first = false;
					else
						printer.OutputStream.Write(' ');
					printer.PrintValue(value);
				}
				return new Types.String(writer.ToString());
			});

			env["str"] = new NativeFunction(args =>
			{
				var writer = new StringWriter();
				var printer = new Printer(writer);
				foreach (var value in args)
				{
					printer.PrintValue(value);
				}
				return new Types.String(writer.ToString());
			});

			env["prn"] = new NativeFunction(args =>
			{
				bool first = true;
				foreach (var value in args)
				{
					if (first)
						first = false;
					else
						interpreter.Printer.OutputStream.Write(' ');
					interpreter.Printer.PrintValue(value);
				}
				interpreter.Printer.OutputStream.WriteLine();
				return Singleton.Nil;
			});

			env["println"] = new NativeFunction(args =>
			{
				var printer = new Printer(interpreter.Printer.OutputStream);
				bool first = true;
				foreach (var value in args)
				{
					if (first)
						first = false;
					else
						printer.OutputStream.Write(' ');
					printer.PrintValue(value);
				}
				printer.OutputStream.WriteLine();
				return Singleton.Nil;
			});

			env["read-string"] = new NativeFunction(args =>
			{
				var source = (Types.String)args[0];
				try
				{
					return Interpreter.ProgramInterpreter.Read(source.Value) ?? throw new RuntimeError("Expected value");
				}
				catch (Reader.MalSyntaxError error)
				{
					throw new RuntimeError(error.Message);
				}
			}, new[] { typeof(Types.String) });

			env["read-script"] = new NativeFunction(args =>
			{
				var source = (Types.String)args[0];
				try
				{
					return Interpreter.ProgramInterpreter.ReadScript(source.Value);
				}
				catch (Reader.MalSyntaxError error)
				{
					throw new RuntimeError(error.Message);
				}
			}, new[] { typeof(Types.String) });

			env["slurp"] = new NativeFunction(args =>
			{
				var fileName = (Types.String)args[0];
				try
				{
					using (var file = File.OpenText(fileName.Value))
					{
						return new Types.String(file.ReadToEnd());
					}
				}
				catch (IOException error)
				{
					throw new RuntimeError(error.Message);
				}
			}, new[] { typeof(Types.String) });
		}

		internal static void LoadBootstrapScript(this Interpreter interpreter)
		{
			var fname = Path.Join(Environment.CurrentDirectory, "bootstrap.mal");
			using (var file = File.OpenText(fname))
			{
				interpreter.EvalScript(file.ReadToEnd());
			}
		}
	}
}
