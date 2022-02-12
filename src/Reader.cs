using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using mal.Types;

namespace mal
{
	class Reader
	{
		public class MalSyntaxError : Exception
		{
			public MalSyntaxError(string message)
				: base(message)
			{}
		}

		IList<string> tokens;
		int position = 0;
		ListInterner listInterner;

		static Regex matcher = new Regex(
			@"[\s,]*(~@|[\[\]{}\(\)'`~^@]|""(?:\\.|[^\\""])*""?|;.*|[^\s\[\]{}\('""`,;\)]*)",
			RegexOptions.Compiled | RegexOptions.ECMAScript
		);

		private Reader(IList<string> tokens, ListInterner listInterner)
		{
			this.tokens = tokens;
			this.listInterner = listInterner;
		}


		public string? GetNext()
		{
			if (position < tokens.Count)
				return tokens[position ++];
			return null;
		}

		public void Skip()
		{
			if (position < tokens.Count)
				++ position;
		}

		private string RequireToken(string? token)
		{
			return token ?? throw new MalSyntaxError("Unexpected End of Input");
		}

		public string? Current
		{
			get
			{
				if (position < tokens.Count)
					return tokens[position];
				return null;
			}
		}

		public int Position => position;
		public int TokensLength => tokens.Count;

		public static Reader TokenizeString(string source, ListInterner listInterner)
		{
			var matches = matcher.Matches(source);
			var tokens = new List<string>();
			foreach (var match in matches as IEnumerable<Match>)
			{
				string tok = match.Groups[1].Value;
				if (tok.Length != 0 && tok[0] != ';')
					tokens.Add(tok);
			}
			return new Reader(tokens, listInterner);
		}

		public void DumpTokens()
		{
			foreach (var token in tokens)
			{
				Console.WriteLine($@"""{token}""");
			}
		}

		public static Value? ReadStringOrEmpty(string source, ListInterner listInterner)
		{
			var reader = TokenizeString(source, listInterner);
			if (reader.TokensLength == 0)
				return null;
			Value value = reader.ReadForm();
			if (reader.Current is not null)
				throw new MalSyntaxError("Excpected end of input");
			return value;
		}

		public static Value ReadString(string source, ListInterner listInterner)
		{
			var reader = TokenizeString(source, listInterner);
			Value value = reader.ReadForm();
			if (reader.Current is not null)
				throw new MalSyntaxError("Excpected end of input");
			return value;
		}

		public static Value[] ReadStringScript(string source, ListInterner listInterner)
		{
			var reader = TokenizeString(source, listInterner);
			var values = new List<Value>();
			while (reader.Current is not null)
				values.Add(reader.ReadForm());
			return values.ToArray();
		}

		private Value ReadForm()
		{
			string token = RequireToken(Current);
			switch (token)
			{
				case "(":
					Skip();
					return ReadList(")", false);
				case "[":
					Skip();
					return ReadList("]", true);
				case ")":
				case "]":
					throw new MalSyntaxError("Unexpected closing brace");
				/* Special macros */
				case "'":
					Skip();
					return new ListBuilder() {
						new Symbol("quote"), ReadForm()
					}.FinishIntern(listInterner, asVector: false);
				case "`":
					Skip();
					return new ListBuilder() {
						new Symbol("quasiquote"), ReadForm()
					}.FinishIntern(listInterner, asVector: false);
				case "~":
					Skip();
					return new ListBuilder() {
						new Symbol("unquote"), ReadForm()
					}.FinishIntern(listInterner, asVector: false);
				case "~@":
					Skip();
					return new ListBuilder() {
						new Symbol("splice-unquote"), ReadForm()
					}.FinishIntern(listInterner, asVector: false);
				case "@":
					Skip();
					return new ListBuilder() {
						new Symbol("deref"), ReadForm()
					}.FinishIntern(listInterner, asVector: false);
				case "^":
					Skip();
					var meta = ReadForm();
					return new ListBuilder() {
						new Symbol("with-meta"), ReadForm(), meta
					}.FinishIntern(listInterner, asVector: false);
				default:
					return ReadAtom(RequireToken(GetNext()));
			}
		}

		private ListBase ReadList(string end, bool isVector)
		{
			var builder = new ListBuilder();
			while (RequireToken(Current) != end)
			{
				builder.Add(ReadForm());
			}
			Skip();
			return builder.FinishIntern(listInterner, asVector: isVector);
		}

		private Value ReadAtom(string token)
		{
			if (char.IsDigit(token, 0)
				|| (token[0] == '-' && token.Length >= 2 && char.IsDigit(token, 1)))
			{
				if (int.TryParse(token, out int result))
					return new Integer(result);
				throw new MalSyntaxError("Inavlid numeric literal");
			}
			switch (token)
			{
				case "nil":
					return Singleton.Nil;
				case "true":
					return Singleton.True;
				case "false":
					return Singleton.False;
			}
			switch (token[0])
			{
				case ':':
					return new Keyword(token.Substring(1));
				case '"':
					/* Return string */
					if (token.Length < 2 || token[^1] != '"')
						throw new MalSyntaxError("Malformed string");
					return new Types.String(ParseString(token[1..^1]));
			}
			return new Symbol(token);
		}

		public static string ParseString(string str)
		{
			char Get(int idx) => idx >= str.Length ? throw new MalSyntaxError("Unfinished escape sequence") : str[idx];

			var builder = new StringBuilder(str.Length);
			for (int idx = 0; idx < str.Length; ++idx)
			{
				if (str[idx] == '\\')
				{
					++idx;
					switch (Get(idx))
					{
						case '0':
							builder.Append((char)0);
							break;
						case 'b':
							builder.Append((char)0x08);
							break;
						case 't':
							builder.Append((char)0x09);
							break;
						case 'n':
							builder.Append((char)0x0A);
							break;
						case 'e':
							builder.Append((char)0x1B);
							break;
						case 'x':
						{
							idx += 2;
							Get(idx);
							if (!ushort.TryParse(str[(idx-1)..(idx+1)], System.Globalization.NumberStyles.HexNumber, null, out ushort res))
								throw new MalSyntaxError("Invalid character number");
							builder.Append((char)res);
							break;
						}
						case 'u':
						{
							idx += 4;
							Get(idx);
							if (!ushort.TryParse(str[(idx-3)..(idx+1)], System.Globalization.NumberStyles.HexNumber, null, out ushort res))
								throw new MalSyntaxError("Invalid character number");
							builder.Append((char)res);
							break;
						}
						case '\\':
							builder.Append('\\');
							break;
						case '"':
							builder.Append('"');
							break;

						default:
							throw new MalSyntaxError("Invalid escape sequence code");
					}
				}
				else
				{
					builder.Append(str[idx]);
				}
			}
			return builder.ToString();
		}

		public static string EscapeString(string str)
		{
			var builder = new StringBuilder(str.Length);
			foreach (char ch in str)
			{
				if (ch < 0x20 || ch == '\\' || ch == '"')
				{
					switch ((byte)ch)
					{
						case 0:
							builder.Append(@"\0");
							break;
						case 0x08:
							builder.Append(@"\b");
							break;
						case 0x09:
							builder.Append(@"\t");
							break;
						case 0x0A:
							builder.Append(@"\n");
							break;
						case 0x1B:
							builder.Append(@"\e");
							break;
						case 0x22:
							builder.Append(@"\""");
							break;
						case 0x5C:
							builder.Append(@"\\");
							break;
						default:
							builder.Append(@$"\x{(ushort)ch:X2}");
							break;
					}
				}
				else if (ch > 0x7E && ch < 0x100)
				{
					builder.Append($@"\x{(ushort)ch:X2}");
				}
				else if (ch > 0x100)
				{
					builder.Append($@"\u{(ushort)ch:X4}");
				}
				else
				{
					builder.Append(ch);
				}
			}
			return builder.ToString();
		}
	}
}
