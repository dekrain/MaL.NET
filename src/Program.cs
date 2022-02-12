using System;
using System.Collections.Generic;
using System.IO;

namespace mal
{
	class Program
	{
		const char CurrentImplementationStep = '6';
		const string Prompt = "user> ";
		static readonly Mono.Terminal.LineEditor LineEditor;
		static Interpreter Interpreter = null!;

		static Program()
		{
			LineEditor = new Mono.Terminal.LineEditor(null)
			{
				HistoryFile = Path.Combine(Environment.CurrentDirectory, ".mal-history")
			};
		}

		enum TestMode
		{
			Eval,
			ParseOnly,
			Echo,
		}

		enum MultilineMode
		{
			Multiline,
			Regex,
		}

		// ASCII hack: 0-9 come before A
		static bool CanRunTestStep(char step) => step <= CurrentImplementationStep;

		static void RunTests()
		{
			string testsDir = Path.Combine(Environment.CurrentDirectory, "tests");
			Environment.CurrentDirectory = testsDir;
			string bootstrapScriptPath = Path.Join(testsDir, "test_std.mal");
			string? bootstrapScript = null;
			if (File.Exists(bootstrapScriptPath))
			{
				using (var file = File.OpenText(bootstrapScriptPath))
					bootstrapScript = file.ReadToEnd();
			}
			foreach (var test in Directory.EnumerateFiles(testsDir))
			{
				string testName = Path.GetFileName(test);
				if (test.EndsWith(".mal") && testName.StartsWith($"step") && CanRunTestStep(testName[4]))
				{
					using (var file = File.OpenText(test))
						RunSingleTest(file, testName, bootstrapScript: bootstrapScript);
				}
			}
		}

		static void CheckTestOutput(IEnumerator<string> output, string expectedOutput, string errHeader, Exception? error, MultilineMode mlmode)
		{
			string? outputLine = null;
			if (error is null)
			{
				if (!output.MoveNext())
					throw new InvalidOperationException($"Enumerator past the end, expected output {expectedOutput}");
				outputLine = output.Current;
			}

			switch (mlmode)
			{
				case MultilineMode.Multiline:
					break;
				case MultilineMode.Regex:
					if (outputLine is not null)
						outputLine = Reader.ParseString(outputLine);
					break;
			}
			if (error is not null || outputLine != expectedOutput)
			{
				if (error is Reader.MalSyntaxError syntaxError)
				{
					Console.Error.Write(
						$"{errHeader}Got syntax error: {syntaxError.Message}\n"
					);
				}
				else if (error is RuntimeError runtimeError)
				{
					Console.Error.Write(
						$"{errHeader}Got runtime error: {runtimeError.Message}\n"
					);
				}
				else if (error is not null)
				{
					Console.Error.Write(
						$"{errHeader}Got fatal error: {error}\n"
					);
					Environment.Exit(1);
				}
				else if (outputLine != expectedOutput)
				{
					Console.Error.Write(
						$"{errHeader}Expected output: {expectedOutput}\nGot: {outputLine}\n"
					);
				}
			}
		}

		static IEnumerator<string> EnumLines(StringReader reader)
		{
			while (reader.ReadLine() is string line)
				yield return line;
		}

		static void RunSingleTest(TextReader file, string name, string? bootstrapScript)
		{
			Console.WriteLine($"Running test: {name}");
			TestMode mode = TestMode.Eval;
			MultilineMode mlmode = MultilineMode.Multiline;
			Interpreter = new Interpreter(
				null!,
				new InterpreterOptions
				{
					IgnoreDecorationsInBuiltinForms = true,
					CompatibleLetSyntax = true,
				}
			);
			if (bootstrapScript is not null)
			{
				Interpreter.EvalScript(bootstrapScript);
			}
			StringWriter? stream = null;
			string? source = null;
			IEnumerator<string>? lineEnumerator = null;
			Exception? error = null;
			bool inDeferrable = false;
			bool inOptional = false;
			while (file.ReadLine() is string line)
			{
				if (line.Length == 0 || line.StartsWith(";;"))
					continue;
				if (line.StartsWith(";=>"))
				{
					lineEnumerator ??= EnumLines(new StringReader(stream?.ToString() ?? string.Empty));
					string expectedOutput = line.Substring(3);
					string errHeader = $@"Test case{
							(inDeferrable ? " (deferrable)" : string.Empty)
						}{
							(inOptional ? " (optional)" : string.Empty)
						} hasn't passed:" + $"\nInput: {source}\n";
					CheckTestOutput(lineEnumerator, expectedOutput, errHeader, error, MultilineMode.Multiline);
				}
				else if (line.StartsWith(";/"))
				{
					lineEnumerator ??= EnumLines(new StringReader(stream?.ToString() ?? string.Empty));
					string expectedOutput = line.Substring(2);
					string errHeader = $@"Test case{
							(inDeferrable ? " (deferrable)" : string.Empty)
						}{
							(inOptional ? " (optional)" : string.Empty)
						} hasn't passed:" + $"\nInput: {source}\n";
					CheckTestOutput(lineEnumerator, expectedOutput, errHeader, error, mlmode);
				}
				else if (line.StartsWith(";&"))
				{
					if (error == null)
					{
						string errHeader = $@"Test case{
							(inDeferrable ? " (deferrable)" : string.Empty)
						}{
							(inOptional ? " (optional)" : string.Empty)
						} hasn't passed:" + $"\nInput: {source}\n";
						Console.Error.Write(
							$"{errHeader}Expected error\n"
						);
					}
				}
				else if (line.StartsWith(";>>> "))
				{
					string directive = line.Substring(5);
					int sep = directive.IndexOf('=');
					var attrName = directive.Substring(0, sep);
					var attrValue = directive.Substring(sep + 1);
					switch (attrName)
					{
						case "deferrable":
							inDeferrable = bool.Parse(attrValue);
							break;
						case "optional":
							inOptional = bool.Parse(attrValue);
							break;
						case "mode":
							mode = Enum.Parse<TestMode>(attrValue);
							break;
						case "mlmode":
							mlmode = Enum.Parse<MultilineMode>(attrValue);
							break;
						case "soft":
							break;
						default:
							throw new InvalidOperationException($"Unknown test {nameof(attrName)}: {attrName}");
					}
				}
				else
				{
					stream = new StringWriter();
					lineEnumerator = null;
					Interpreter.Printer = new Printer(stream, repr: true);
					source = line;
					try
					{
						switch (mode)
						{
							case TestMode.Eval:
								Interpreter.rep(source);
								break;
							case TestMode.ParseOnly:
								Interpreter.rp(source);
								break;
							case TestMode.Echo:
								Interpreter.Printer.OutputStream.Write(source);
								break;
						}
						error = null;
					} catch (Exception err)
					{
						error = err;
						stream = null;
					}
				}
			}
		}

		static void Main(string[] args)
		{
			InterpreterOptions interpreterOptions = new InterpreterOptions{
				DumpBytecode = true,
				DumpPrecompiledBytecode = false,
			};

			foreach (var arg in args)
			{
				switch (arg)
				{
					case "--test":
						RunTests();
						return;
					case "--dump-bytecode":
						interpreterOptions.DumpBytecode = true;
						break;
					case "--no-dump-bytecode":
						interpreterOptions.DumpBytecode = false;
						break;
					case "--dump-builtin-bytecode":
						interpreterOptions.DumpPrecompiledBytecode = true;
						break;
					case "--no-dump-builtin-bytecode":
						interpreterOptions.DumpPrecompiledBytecode = false;
						break;
				}
			}

			Interpreter = new Interpreter(
				new Printer(Console.Out, repr: true, detectTTY: true, showSpecial: true),
				interpreterOptions
			);
			while (LineEditor.Edit(Prompt, null) is string line) {
				try
				{
					Interpreter.rep(line);
				}
				catch (Reader.MalSyntaxError err)
				{
					Console.Error.WriteLine($"Syntax error: {err.Message}");
				}
				catch (RuntimeError err)
				{
					Console.Error.WriteLine($"Runtime error: {err.Message}");
				}
			}
		}
	}
}
