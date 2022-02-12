using System;
using System.Collections.Generic;
using System.Linq;
using mal.Types;

namespace mal.Jit
{
	public class CompileError : RuntimeError
	{
		public CompileError(string message)
			: base(message)
		{}
	}

	public class JITInterpreterContext
	{
		public readonly ByteCode ScriptStub;
		public readonly ByteCode FunctionStub;

		public JITInterpreterContext(Interpreter interpreter)
		{
			JIT.CompilerContext scriptStubCompiler = new JIT.CompilerContext();
			JIT.CompilerContext functionStubCompiler = new JIT.CompilerContext();

			scriptStubCompiler.Emit(OpCode.CompileFunction, 0);
			functionStubCompiler.Emit(OpCode.CompileFunction, 1);

			ScriptStub = scriptStubCompiler.Finish();
			FunctionStub = functionStubCompiler.Finish();
		}
	}

	public static class JIT
	{
		public class CompilerContext
		{
			protected List<Instruction> instructions = new();
			protected List<Value> constants = new();
			protected List<string> names = new();

			public struct Label
			{
				public int? bindIndex;
				public int? destIndex;
			}

			public void Emit(OpCode op, int arg = 0) => instructions.Add(new(op, arg));
			public void Emit(OpCode op, Assertion arg) => instructions.Add(new(op, (int)arg));
			public void Emit(OpCode op, ValueTypeCode arg) => instructions.Add(new(op, (int)arg));

			public void Emit4B(OpCode op, byte arg0, byte arg1, byte arg2, byte arg3) => instructions.Add(new(op, arg0 | (arg1 << 8) | (arg2 << 16) | (arg3 << 24)));
			public void Emit2S(OpCode op, ushort argLo, ushort argHi) => instructions.Add(new(op, argLo | (argHi << 16)));

			public void EmitConst(OpCode op, Value value) => instructions.Add(new(op, AddConst(value)));
			public void EmitInt(OpCode op, int value) => instructions.Add(new(op, AddConst(new Integer(value))));
			public void EmitName(OpCode op, string name) => instructions.Add(new(op, AddName(name)));

			public void EmitBranch(OpCode op, ref Label dest)
			{
				dest.bindIndex = instructions.Count;
				Emit(op, dest.destIndex ?? -1);
			}

			public void BindLabel(ref Label label)
			{
				label.destIndex = instructions.Count;
				if (label.bindIndex.HasValue)
					instructions[label.bindIndex.Value] = new Instruction(instructions[label.bindIndex.Value].op, label.destIndex.Value);
			}

			public int AddName(string name)
			{
				int i = names.IndexOf(name);
				if (i == -1)
				{
					i = names.Count;
					names.Add(name);
				}
				return i;
			}

			public int AddConst(Value value)
			{
				int i = constants.IndexOf(value);
				if (i == -1)
				{
					i = constants.Count;
					constants.Add(value);
				}
				return i;
			}

			public ByteCode Finish()
			{
				return new ByteCode(instructions.ToArray(), constants.ToArray(), names.ToArray());
			}
		}

		private class FunctionCompilerContext : CompilerContext
		{
			private readonly Function function;
			private readonly bool isFunction;

			const int cIdxNil = 0;

			public FunctionCompilerContext(Function function, bool isFunction)
			{
				this.function = function;
				this.isFunction = isFunction;
				/* 0 => */ constants.Add(Singleton.Nil);
			}

			public void Compile()
			{
				// Initial stack layout:
				// [arg0] [arg1]... [argN] [#args: Int]
				// Final stack layout:
				// [retV]
				if (isFunction)
				{
					EmitConst(OpCode.LoadConst, new Integer(function.Params!.Length));
					if (function.VariadicParam is null)
					{
						Emit(OpCode.CmpEq);
						Emit(OpCode.AssertTrue, Assertion.ArgumentCountMismatch);
					}
					else
					{
						// [args...] [#args] [#params] =>
						Emit(OpCode.DupTop, 1);
						Emit(OpCode.RotateTop, -2);
						Emit(OpCode.DupTop, 1);
						Emit(OpCode.RotateTop, -2);
						Emit(OpCode.CmpLt);
						Emit(OpCode.AssertFalse, Assertion.ArgumentCountMismatch);
					}
					Emit(OpCode.PushEnv);
					if (function.VariadicParam is not null)
					{
						Emit(OpCode.RotateTop, 1);
						// => [args...] [#args] [#params]
						Emit(OpCode.Neg);
						Emit(OpCode.Add);
						Emit(OpCode.BuildList, -1);
						EmitName(OpCode.DefineName, function.VariadicParam);
					}
					foreach (var param in function.Params.Reverse())
					{
						EmitName(OpCode.DefineName, param);
					}
				}
				else
				{
					EmitConst(OpCode.LoadConst, new Integer(0));
					Emit(OpCode.CmpEq);
					Emit(OpCode.AssertTrue, Assertion.EmptyFunctionParams);
				}
				Compile(function.Body, true);
				if (isFunction)
				{
					Emit(OpCode.PopEnv);
				}
			}

			void Compile(Value ast, bool inTailPosition)
			{
				if (ast is ListBase call && !call.IsVector && call.Top is not null)
				{
					var callee = call.Top.Head;
					var args = call.Top.Tail;
					int argCount = args.GetCount();

					// Handle special forms
					if (callee is Symbol s)
					{
						string sym = s.Value;
						if (
							(sym.EndsWith('*') || sym.EndsWith('!'))
							&& Interpreter.ProgramInterpreter.Options.IgnoreDecorationsInBuiltinForms)
						{
							sym = sym.Substring(0, sym.Length - 1);
						}

						switch (sym)
						{
							case "do":
							{
								if (args == null)
									throw new CompileError("'do takes at least one argument");
								for (var list = args; list is not null; list = list.Tail)
								{
									Compile(list.Head, inTailPosition & list.Tail is null);
									if (list.Tail is not null)
										Emit(OpCode.Discard, 1);
								}
								return;
							}

							case "if":
							{
								if (argCount != 2 && argCount != 3)
									throw new CompileError("'if takes two or three arguments");
								var cond = args!.Head;
								var ifTrue = args.Tail!.Head;
								var ifFalse = args.Tail.Tail?.Head;
								Label bFalse = new Label(), bEnd = new Label();
								Compile(cond, false);
								EmitBranch(OpCode.BranchIfFalse, ref bFalse);
								Compile(ifTrue, inTailPosition);
								EmitBranch(OpCode.Branch, ref bEnd);
								BindLabel(ref bFalse);
								if (ifFalse != null)
									Compile(ifFalse, inTailPosition);
								else
									Emit(OpCode.LoadConst, cIdxNil);
								BindLabel(ref bEnd);
								return;
							}

							case "fn":
							{
								if (argCount != 2)
									throw new CompileError("'if takes two arguments");
								var fParams = args!.Head;
								var body = args.Tail!.Head;
								if (!(fParams is ListBase paramList))
									throw new CompileError("First argument of 'if must be a symbol list");
								var paramArray = paramList.Top.ToArray();
								if (!paramArray.All(v => v is Symbol))
									throw new CompileError("First argument of 'if must be a symbol list");
								int namesStart = names.Count;
								names.AddRange(from param in paramArray select ((Symbol)param).Value);
								EmitConst(OpCode.LoadConst, body);
								Emit2S(OpCode.MakeFunction, (ushort)namesStart, (ushort)paramArray.Length);
								return;
							}

							case "def":
							{
								if (argCount != 2)
									throw new CompileError("'def takes two arguments");
								var name = args!.Head;
								var value = args.Tail!.Head;
								if (!(name is Symbol symName))
									throw new CompileError("First argument of 'def must be a name");
								Compile(value, false);
								Emit(OpCode.DupTop, 1);
								EmitName(OpCode.DefineName, symName.Value);
								return;
							}

							case "let":
							{
								if (argCount != 2)
									throw new CompileError("'let takes two arguments");
								var body = args!.Tail!.Head;
								if (!(args.Head is ListBase defList))
									throw new CompileError("First argument of 'let must be a list of definition pairs");
								Emit(OpCode.PushEnv);
								IEnumerable<Value> definitions =
										Interpreter.ProgramInterpreter.Options.CompatibleLetSyntax
										? defList.GetSubListEnumerator(2) : defList;
								foreach (var entry in definitions)
								{
									if (!(entry is ListBase entryPair) || entryPair.Count < 2)
										throw new CompileError("First argument of 'let must be a list of definition pairs");
									if (!(entryPair.Top!.Head is Symbol name))
										throw new CompileError("First element of definition pair must be a name");
									Compile(entryPair.Top.Tail!.Head, false);
									EmitName(OpCode.DefineName, name.Value);
								}
								Compile(body, inTailPosition);
								Emit(OpCode.PopEnv);
								return;
							}

							case "quote":
							{
								if (argCount != 1)
									throw new CompileError("'quote takes one argument");
								EmitConst(OpCode.LoadConst, args!.Head);
								return;
							}

							case "unquote":
							case "splice-unquote":
							{
								throw new CompileError($"'{sym} special form is only available within 'quasiquote");
							}

							case "quasiquote":
							{
								if (argCount != 1)
									throw new CompileError("'quasiquote takes one argument");

								var root = args!.Head;
								CompileQuasiQuote(root);
								return;
							}

							default:
								break;
						}
					}

					Compile(callee, false);
					foreach (var arg in args)
						Compile(arg, false);

					if (inTailPosition)
						Emit(OpCode.PrepareForTailCall);
					Emit(OpCode.Call, argCount);
				}
				else
				{
					switch (ast)
					{
						case Symbol symbol:
							EmitName(OpCode.LoadName, symbol.Value);
							break;

						case ListBase list:
							int elCount = 0;
							foreach (var el in list)
							{
								Compile(el, false);
								++elCount;
							}

							Emit(list.IsVector ? OpCode.BuildVector : OpCode.BuildList, elCount);
							break;

						default:
							EmitConst(OpCode.LoadConst, ast);
							break;
					}
				}
			}

			enum QuasiQuoteState
			{
				Quote,
				Concat,
			}

			void CompileQuasiQuote(Value item)
			{
				// Final:
				// (a b c d) -> '(a b c d) -> Load{(a b c d)}
				// (a b (unqoute c) d) -> (concat '(a b) c '(d)) Load{(a b)} !c Load{(d)} Concat{3}
				// ((unqoute a) (b c (splice-unquote d) e)) -> (list a (concat '(b c) d '(e))) -> !a Load{(b c)} !d Load{(e)} Concat{3} List{2}
				// (a (b c) (d e (splice-unqoute f))) -> (concat '(a (b c)) (list (concat '(d e) f))) -> Load{(a (b c))} Load{(d e)} !f Concat{2} List{1} Concat{2}
				// Simple:
				// (a b c d) -> (list 'a 'b 'c 'd) -> Load{a} Load{b} Load{c} Load{d} List{4}
				// (a b (unqoute c) d) -> (list 'a 'b c 'd) -> Load{a} Load{b} !c Load{d} List{4}
				// ((unqoute a) (b c (splice-unquote d) e)) -> (list a (concat (list 'b 'c) d (list 'e))) -> !a Load{b} Load{c} List{2} !d Load{e} List{1} Concat{3} List{2}

				var states = new Stack<(QuasiQuoteState state, int itemCount)>();
				if (item is ListBase list)
				{
					if (!list.IsVector && list.Top?.Head is Symbol sym)
					{
						switch (sym.Value)
						{
							case "unquote":
							{
								if (list.Count != 2)
									throw new CompileError("'unquote special form takes 1 argument");
								Compile(list.Top.Tail!.Head, false);
								return;
							}

							case "splice-unquote":
							{
								if (list.Count != 2)
									throw new CompileError("'splice-unquote special form takes 1 argument");
								EmitConst(OpCode.LoadConst, list.Top.Tail!.Head);
								return;
							}
						}
					}

					states.Push((QuasiQuoteState.Quote, 0));
					foreach (var element in list)
					{
						CompileQuasiQuote(element);
						if (states.Peek().state == QuasiQuoteState.Quote)
							++states.Peek().itemCount;
					}
				}
				else
				{
					EmitConst(OpCode.LoadConst, item);
				}
			}
		}

		public static void CompileFunction(Function function, bool isFunction)
		{
			var context = new FunctionCompilerContext(function, isFunction);
			context.Compile();
			function.Bytecode = context.Finish();
			if (Interpreter.ProgramInterpreter.Options.DumpBytecode)
				function.Bytecode.Dump();
		}

		public static ByteCode BuildFunction(Action<CompilerContext> builder)
		{
			var context = new CompilerContext();
			builder(context);
			var bytecode = context.Finish();
			if (Interpreter.ProgramInterpreter.Options.DumpPrecompiledBytecode)
				bytecode.Dump();
			return bytecode;
		}
	}
}
