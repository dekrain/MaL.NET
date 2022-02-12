using System;
using System.Collections.Generic;
using System.Linq;
using mal.Types;
using mal.Jit;
using System.Diagnostics;

namespace mal
{
	public struct InterpreterOptions
	{
		public bool DumpBytecode;
		public bool DumpPrecompiledBytecode;
		public bool IgnoreDecorationsInBuiltinForms;
		public bool CompatibleLetSyntax;
	}

	public class Interpreter
	{
		readonly ListInterner ListInterner = new();
		public Printer Printer;
		public Env GlobalEnvironment = new Env();
		private readonly JITInterpreterContext jitContext;

		public static Interpreter ProgramInterpreter { get; private set; } = null!;

		public InterpreterOptions Options;

		private static System.Reflection.FieldInfo[] cachedOptionsFieldPath;

		static Interpreter()
		{
			cachedOptionsFieldPath = new[]
			{
				typeof(Interpreter).GetField(nameof(Options), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!,
			};
		}

		public Interpreter(Printer printer, InterpreterOptions options)
		{
			Printer = printer;
			ProgramInterpreter = this;
			Options = options;

			jitContext = new JITInterpreterContext(this);

			this.InstallNativeBuiltins();
			this.CompileBuiltins();
			this.LoadBootstrapScript();

			/*foreach (var mem in typeof(InterpreterOptions).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
			{
				GlobalEnvironment.Define("&" + mem.Name, optionToValue(mem.GetValue(Options)));
			}*/

			GlobalEnvironment.LookupHook += (name) =>
			{
				if (name.Length > 1 && name[0] == '&')
				{
					var mem = typeof(InterpreterOptions).GetField(name[1..], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
					if (mem == null)
						throw new RuntimeError("Unknown interpreter option");
					return optionToValue(mem.GetValueDirect(TypedReference.MakeTypedReference(this, cachedOptionsFieldPath))!);
				}
				return null;
			};

			GlobalEnvironment.DefineHook += (name, value) =>
			{
				if (name.Length > 1 && name[0] == '&')
				{
					var mem = typeof(InterpreterOptions).GetField(name[1..], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
					if (mem == null)
						throw new RuntimeError("Unknown interpreter option");
					mem.SetValueDirect(TypedReference.MakeTypedReference(this, cachedOptionsFieldPath), valueToOption(value, mem.FieldType));
				}
			};

			Value optionToValue(object opt) => opt switch
			{
				bool b => b ? Singleton.True : Singleton.False,
				_ => throw new InvalidOperationException("Unsupported option type"),
			};

			object valueToOption(Value value, Type type) => value switch
			{
				Singleton s =>
					s.OfKind(SingletonKind.Nil) ? throw new RuntimeError("Option cannot be nil")
					: checkedValue(s.OfKind(SingletonKind.True), type),
				Integer i =>
					checkedValue(i.Value, type),
				Types.String str =>
					checkedValue(str.Value, type),
				_ => throw new RuntimeError("Invalid option type"),
			};

			T checkedValue<T>(T value, Type type)
			{
				if (!type.IsAssignableFrom(typeof(T)))
					throw new RuntimeError("Value doesn't match the option's type");
				return value;
			}
		}

		public Value? Read(string src) => Reader.ReadStringOrEmpty(src, ListInterner);
		public Value ReadScript(string src)
		{
			var builder = new ListBuilder();
			builder.Add(new Symbol("do"));
			builder.AddRange(Reader.ReadStringScript(src, ListInterner));
			return builder.Finish(asVector: false);
		}

		Value? EvalOrNull(Value? prog, Env env) => prog is null ? null : Eval(prog, env);

		void Print(Value? res)
		{
			if (res == null)
				return;
			Printer.PrintValue(res);
			Printer.OutputStream.WriteLine();
		}

		public void rep(string src)
		{
			Print(EvalOrNull(Read(src), GlobalEnvironment));
			//GC.Collect();
			//ListInterner.DumpState();
		}

		internal void rp(string src)
		{
			Print(Read(src));
		}

		public void RepScript(string src)
		{
			Print(EvalOrNull(ReadScript(src), GlobalEnvironment));
		}

		public void EvalScript(string src)
		{
			EvalOrNull(ReadScript(src), GlobalEnvironment);
		}

		/*private Value EvalAst(Value ast)
		{
			return ast switch
			{
				Symbol s => GlobalEnvironment.Get(s.Value),
				ListBase b => b.Map(v => Eval(v)),
				_ => ast,
			};
		}

		public Value Eval(Value ast)
		{
			ast = EvalAst(ast);
			if (ast is ListBase call && !call.IsVector && call.Top != null)
			{
				var callee = call.Top.Head;
				var args = call.Top.Tail.AsArray();

				if (!(callee is IInvocable func))
					throw new RuntimeError("Cannot invoke a non-function");

				return func.Invoke(this, args);
			}
			return ast;
		}*/

		public Value Eval(Value ast, Env env) => new Function(ast, env, jitContext).Invoke(this);

		internal Value EvalFunction(ByteCodeFunction function, Env env, params Value[] args)
		{
			L_Reentry:
			// Bytecode stack interpreter
			var stack = new Stack<Value>(args);
			var fastVars = new List<Value>();
			stack.Push(new Integer(args.Length));
			var names = function.Bytecode!.Names;
			var consts = function.Bytecode.Constants;
			var instructions = function.Bytecode.Instructions;
			bool inTailPosition = false;
			Value[]? tailCallArgs = null;
			IInvocable? tailCallFunction = null;

			int idx = 0;
			while (idx < instructions.Length)
			{
				var instr = instructions[idx];
				++idx;
				if (true)
				{
					// Check tail call position for each instruction
					switch (instr.op)
					{
						case OpCode.Call:
						case OpCode.Discard:
						case OpCode.PopEnv:
						case OpCode.Branch:
							break;

						default:
							if (inTailPosition)
								throw new InvalidOperationException("Cannot execute bytecode in tail position");
							break;
					}
				}
				switch (instr.op)
				{
					case OpCode.LoadConst:
						stack.Push(consts[instr.arg]);
						break;
					case OpCode.LoadName:
						stack.Push(env.Get(names[instr.arg]));
						break;
					case OpCode.LoadFast:
						stack.Push(fastVars[instr.arg]);
						break;
					case OpCode.PrepareForTailCall:
						inTailPosition = true;
						break;
					case OpCode.Call:
						{
							int argCount;
							Value? callee = null;
							if (instr.arg == -1)
							{
								argCount = ((Integer)stack.Pop()).Value;
								callee = stack.Pop();
							}
							else
							{
								argCount = instr.arg;
							}
							var callArgs = new Value[argCount];
							for (int i = 0; i != argCount; ++i)
								callArgs[argCount - i - 1] = stack.Pop();
							if (callee is null)
								callee = stack.Pop();

							if (!(callee is IInvocable func))
								throw new RuntimeError("Cannot invoke a non-function");

							if (inTailPosition)
							{
								if (tailCallFunction is not null)
									throw new InvalidOperationException("Tail call already set up");
								tailCallArgs = callArgs;
								tailCallFunction = func;
								stack.Push(null!);
							}
							else
							{
								stack.Push(func.Invoke(this, callArgs));
							}
							break;
						}
					case OpCode.BuildList:
					case OpCode.BuildVector:
						{
							int argCount;
							ListNode? list = null;
							if (instr.arg == -1)
							{
								argCount = ((Integer)stack.Pop()).Value;
							}
							else
							{
								argCount = instr.arg;
							}
							for (int i = 0; i != argCount; ++i)
							{
								list = new ListNode(stack.Pop(), list);
							}
							stack.Push(new ListBase(list, instr.op == OpCode.BuildVector));
							break;
						}
					case OpCode.DupTop:
						{
							var val = stack.Peek();
							for (int i = 0; i != instr.arg; ++i)
								stack.Push(val);
							break;
						}
					case OpCode.Discard:
						for (int i = 0; i != instr.arg; ++i)
							stack.Pop();
						break;
					case OpCode.MakeFunction:
						{
							var body = stack.Pop();
							var paramList = instr.arg == -1 ? null : names[instr.LoHalf..(instr.LoHalf + instr.HiHalf)];
							stack.Push(new Function(body, env, jitContext, paramList));
							break;
						}
					case OpCode.Branch:
						idx = instr.arg;
						break;
					case OpCode.BranchIfTrue:
						if (stack.Pop().IsTruthy)
							idx = instr.arg;
						break;
					case OpCode.BranchIfFalse:
						if (!stack.Pop().IsTruthy)
							idx = instr.arg;
						break;
					case OpCode.DefineName:
						env.Define(names[instr.arg], stack.Pop());
						break;
					case OpCode.StoreFast:
						if (instr.arg >= fastVars.Count)
							for (int i = fastVars.Count - 1; i != instr.arg; ++i)
								fastVars.Add(null!);
						fastVars[instr.arg] = stack.Pop();
						break;
					case OpCode.PushEnv:
						env = new Env(env);
						break;
					case OpCode.PopEnv:
						env = env.Outer!;
						break;
					case OpCode.AssertTrue:
						if (!stack.Pop().IsTruthy)
							throw new RuntimeError(AssertionMessages.Get(instr.AssertId));
						break;
					case OpCode.AssertFalse:
						if (stack.Pop().IsTruthy)
							throw new RuntimeError(AssertionMessages.Get(instr.AssertId));
						break;
					case OpCode.RotateTop:
						{
							if (instr.arg > 0)
							{
								var val = stack.Pop();
								var list = new Value[instr.arg];
								for (var i = 0; i != instr.arg; ++i)
									list[instr.arg - i - 1] = stack.Pop();
								stack.Push(val);
								foreach (var item in list)
									stack.Push(item);
							}
							else if (instr.arg < 0)
							{
								var list = new Value[-instr.arg];
								for (var i = 0; i != -instr.arg; ++i)
									list[-instr.arg - i - 1] = stack.Pop();
								var val = stack.Pop();
								foreach (var item in list)
									stack.Push(item);
								stack.Push(val);
							}
							break;
						}
					case OpCode.UnpackCons:
					{
						var list = (ListBase)stack.Pop();
						stack.Push(new ListBase(list.Top!.Tail, isVector: list.IsVector));
						stack.Push(list.Top.Head);
						break;
					}
					case OpCode.BuildCons:
					{
						var top = stack.Pop();
						var next = (ListBase)stack.Pop();
						stack.Push(new ListBase(new ListNode(top, next.Top), isVector: next.IsVector));
						break;
					}
					case OpCode.TypeCheck:
						{
							var val = stack.Peek();
							stack.Push(val.CheckType((ValueTypeCode)instr.arg) ? Singleton.True : Singleton.False);
							break;
						}
					case OpCode.Add:
						{
							Integer b = (Integer)stack.Pop(), a = (Integer)stack.Pop();
							stack.Push(new Integer(a.Value + b.Value));
							break;
						}
					case OpCode.Neg:
						{
							Integer i = (Integer)stack.Pop();
							stack.Push(new Integer(-i.Value));
							break;
						}
					case OpCode.Mul:
						{
							Integer b = (Integer)stack.Pop(), a = (Integer)stack.Pop();
							stack.Push(new Integer(a.Value * b.Value));
							break;
						}
					case OpCode.AddImm:
						{
							Integer i = (Integer)stack.Pop();
							stack.Push(new Integer(i.Value + instr.arg));
							break;
						}
					case OpCode.CmpEq:
						{
							Value b = stack.Pop(), a = stack.Pop();
							stack.Push((a == b) ? Singleton.True : Singleton.False);
							break;
						}
					case OpCode.CmpLt:
						{
							Integer b = (Integer)stack.Pop(), a = (Integer)stack.Pop();
							stack.Push((a.Value < b.Value) ? Singleton.True : Singleton.False);
							break;
						}
					case OpCode.RefCreate:
						stack.Push(new Atom(stack.Pop()));
						break;
					case OpCode.RefLoad:
						stack.Push(((Atom)stack.Pop()).Value);
						break;
					case OpCode.RefStore:
					{
						Atom at = (Atom)stack.Pop();
						at.Value = stack.Pop();
						break;
					}
					case OpCode.CompileFunction:
						JIT.CompileFunction((Function)function, instr.arg != 0);
						break;
					default:
						throw new InvalidOperationException("Invalid opcode");
				}
			}
			if (stack.Count != 1)
				throw new InvalidOperationException($"Expected one return value, got {stack.Count}");
			if (tailCallFunction is ByteCodeFunction f)
			{
				if (stack.Peek() is not null)
					throw new InvalidOperationException("Tail call return value is not returned");
				if (f is Function mf && f.Bytecode is null)
					JIT.CompileFunction(mf);

				// Clean up environment
				function = f;
				env = f.Scope;
				args = tailCallArgs!;
				goto L_Reentry;
			}
			else if (tailCallFunction is not null)
			{
				if (stack.Peek() is not null)
					throw new InvalidOperationException("Tail call return value is not returned");
				return tailCallFunction.Invoke(this, tailCallArgs!);
			}
			return stack.Peek();
		}
	}
}
