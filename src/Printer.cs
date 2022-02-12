using System;
using System.IO;

using mal.Types;

using Environment = System.Environment;

namespace mal
{
	public class Printer
	{
		public readonly TextWriter OutputStream;
		public bool IsRepr = false;
		public bool IsTTY = false;
		public bool ShowSpecial = false;

		public Printer(TextWriter outputStream, bool repr = false, bool detectTTY = false, bool showSpecial = false)
		{
			OutputStream = outputStream;
			IsRepr = repr;
			if (detectTTY)
				DetectTTY();
			ShowSpecial = showSpecial;
		}

		public void DetectTTY()
		{
			// I hope there was a better way to do this
			IsTTY = OutputStream == Console.Out && !Console.IsOutputRedirected;
		}

		public void PrintValue(Value value)
		{
			switch (value)
			{
				case Symbol sym:
					using (UseColor(Colors.Symbol))
					{
						OutputStream.Write(sym.Value);
					}
					break;
				case Integer num:
					using (UseColor(Colors.Number))
					{
						OutputStream.Write(num.Value);
					}
					break;
				case Singleton:
					if (value.IsNil)
					{
						using (UseColor(Colors.Nil))
						{
							OutputStream.Write("nil");
						}
					}
					else
					{
						using (UseColor(Colors.Bool))
						{
							OutputStream.Write(value.IsTrue ? "true" : "false");
						}
					}
					break;
				case Keyword kw:
					using (UseColor(Colors.Keyword))
					{
						OutputStream.Write(":" + kw.Value);
					}
					break;
				case Types.String str:
					if (IsRepr)
					{
						using (UseColor(Colors.String))
						{
							OutputStream.Write('"');
							OutputStream.Write(Reader.EscapeString(str.Value));
							OutputStream.Write('"');
						}
					}
					else
					{
						OutputStream.Write(str.Value);
					}
					break;
				case ListBase list:
					if (ShowSpecial && list.IsInterned)
					{
						using (UseColor(Colors.Special))
							OutputStream.Write("+Interned ");
					}
					OutputStream.Write(list.IsVector ? '[' : '(');
					bool first = true;
					foreach (var v in list)
					{
						if (!first)
							OutputStream.Write(' ');
						else
							first = false;

						PrintValue(v);
					}
					OutputStream.Write(list.IsVector ? ']' : ')');
					break;
				case ByteCodeFunction func:
					using (UseColor(Colors.Special))
						OutputStream.Write("<function>");
					break;
				case NativeFunction func:
					using (UseColor(Colors.Special))
						OutputStream.Write("<native function>");
					break;
				case Atom atom:
					using (UseColor(Colors.Special))
						OutputStream.Write("<atom>");
					break;
				default:
					OutputStream.Write("<unknown>");
					break;
			}
		}

		private enum Color
		{
			Blank,
			Black,
			Red,
			Green,
			Yellow,
			Blue,
			Magenta,
			Cyan,
			White,
			Gray,
			LightRed,
			LightGreen,
			LightYellow,
			LightBlue,
			LightMagenta,
			LightCyan,
			LightWhite,
		}

		private static class Colors
		{
			public const Color Symbol = Color.White;
			public const Color Keyword = Color.LightMagenta;
			public const Color String = Color.LightGreen;
			public const Color Number = Color.LightYellow;
			public const Color Nil = Color.Gray;
			public const Color Bool = Color.LightCyan;
			public const Color Special = Color.Blue;
		}

		private interface IColorWriter
		{
			void WriteColor(TextWriter outputStream, Color color);

			static IColorWriter Writer;

			static IColorWriter()
			{
				switch (Environment.OSVersion.Platform)
				{
					case PlatformID.Win32NT:
						Writer = new ColorWriterConsole();
						break;
					case PlatformID.Unix:
						Writer = new ColorWriterUnix();
						break;
					case PlatformID.Other:
					default:
						Writer = new ColorWriterEmpty();
						break;
				}
			}
		}

		private class ColorWriterEmpty : IColorWriter
		{
			public void WriteColor(TextWriter outputStream, Color color)
			{}
		}

		private class ColorWriterConsole : IColorWriter
		{
			private static ConsoleColor? DefaultColor;

			public ColorWriterConsole()
			{
				if (!DefaultColor.HasValue)
				{
					DefaultColor = Console.ForegroundColor;
				}
			}
			public void WriteColor(TextWriter outputStream, Color color)
			{
				// !!Warning: stream independent
				if (color == Color.Blank)
				{
					Console.ForegroundColor = DefaultColor!.Value;
					return;
				}
				int colorValue = (int)color - 1;
				bool hasRed   = (colorValue & 1) != 0;
				bool hasGreen = (colorValue & 2) != 0;
				bool hasBlue  = (colorValue & 4) != 0;
				bool isLight  = (colorValue & 8) != 0;
				Console.ForegroundColor =
					(hasRed   ? ConsoleColor.DarkRed   : ConsoleColor.Black) |
					(hasGreen ? ConsoleColor.DarkGreen : ConsoleColor.Black) |
					(hasBlue  ? ConsoleColor.DarkBlue  : ConsoleColor.Black) |
					(isLight  ? ConsoleColor.DarkGray  : ConsoleColor.Black);
			}
		}

		private class ColorWriterUnix : IColorWriter
		{
			public void WriteColor(TextWriter outputStream, Color color)
			{
				string colorString;
				if (color == Color.Blank)
					colorString = string.Empty;
				else if ((((int)color - 1) & 8) != 0)
					colorString = (90 + (int)color - 9).ToString();
				else
					colorString = (30 + (int)color - 1).ToString();
				outputStream.Write($"\u001b[{colorString}m");
			}
		}

		private ColorContext UseColor(Color color) => new ColorContext(color, OutputStream, IsTTY);

		private struct ColorContext : IDisposable
		{
			readonly Color color;
			readonly TextWriter output;
			readonly bool useColors;

			public ColorContext(Color color, TextWriter output, bool useColors)
			{
				this.color = color;
				this.output = output;
				this.useColors = useColors;

				if (useColors)
					WriteColor(color);
			}

			public void Dispose()
			{
				if (useColors)
					WriteColor(Color.Blank);
			}

			private void WriteColor(Color color)
			{
				IColorWriter.Writer.WriteColor(output, color);
			}
		}
	}
}
