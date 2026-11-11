using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TransTool
{
    internal static class AsciiCodec
    {
        internal static string Encode(string input, int radix, bool escapes)
        {
            var output = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                int value = input[i];
                if (escapes && value == '\\') value = ReadEscape(input, ref i);
                if (value > 127)
                {
                    int point = char.IsSurrogatePair(input, i) ? char.ConvertToUtf32(input, i) : value;
                    throw new FormatException("第 " + (i + 1) + " 个文本位置含有非 ASCII 字符 U+" +
                        point.ToString("X4") + "。ASCII 的范围为 0–127。");
                }

                if (output.Length > 0) output.Append(' ');
                if (radix == 16) output.Append(value.ToString("X2"));
                else if (radix == 2) output.Append(Convert.ToString(value, 2).PadLeft(8, '0'));
                else output.Append(value.ToString(CultureInfo.InvariantCulture));
            }
            return output.ToString();
        }

        internal static string Decode(string input, int radix, out int count)
        {
            var values = new List<int>();
            for (int i = 0; i < input.Length;)
            {
                if (IsSeparator(input[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < input.Length && !IsSeparator(input[i])) i++;
                values.Add(ReadCode(input.Substring(start, i - start), radix, values.Count + 1));
            }

            count = values.Count;
            var output = new StringBuilder();
            foreach (int value in values)
            {
                switch (value)
                {
                    case 0: output.Append(@"\0"); break;
                    case 9: output.Append(@"\t"); break;
                    case 10: output.Append(@"\n"); break;
                    case 13: output.Append(@"\r"); break;
                    case 92: output.Append(@"\\"); break;
                    default:
                        if (value < 32 || value == 127) output.Append(@"\x" + value.ToString("X2"));
                        else output.Append((char)value);
                        break;
                }
            }
            return output.ToString();
        }

        private static int ReadEscape(string input, ref int index)
        {
            int start = index++;
            if (index < input.Length)
            {
                switch (input[index])
                {
                    case '0': return 0;
                    case 't': return 9;
                    case 'n': return 10;
                    case 'r': return 13;
                    case '\\': return 92;
                    case 'x':
                        if (index + 2 < input.Length)
                        {
                            int high = Digit(input[index + 1]);
                            int low = Digit(input[index + 2]);
                            if (high >= 0 && low >= 0 && high < 8)
                            {
                                index += 2;
                                return high * 16 + low;
                            }
                        }
                        break;
                }
            }
            throw new FormatException("第 " + (start + 1) + @" 个文本位置的转义无效。支持 \0、\t、\n、\r、\\ 和 \x00–\x7F。");
        }

        private static int ReadCode(string token, int radix, int number)
        {
            int index = 0;
            if (token.Length >= 2 && token[0] == '0')
            {
                char prefix = char.ToLowerInvariant(token[1]);
                if (prefix == 'x' || (prefix == 'b' && (token.Length > 2 || radix != 16)))
                {
                    radix = prefix == 'x' ? 16 : 2;
                    index = 2;
                }
            }

            int value = 0;
            while (index < token.Length)
            {
                int digit = Digit(token[index++]);
                if (digit < 0 || digit >= radix) break;
                value = value * radix + digit;
                if (value > 127) break;
                if (index == token.Length) return value;
            }

            string example = radix == 16 ? "00–7F" : radix == 2 ? "00000000–01111111" : "0–127";
            string preview = token.Length <= 24 ? token : token.Substring(0, 24) + "…";
            throw new FormatException("第 " + number + " 项“" + preview + "”无效。此进制的 ASCII 范围为 " + example + "。");
        }

        private static bool IsSeparator(char character)
        {
            return char.IsWhiteSpace(character) || character == ',' || character == '，' ||
                character == ';' || character == '；';
        }

        private static int Digit(char character)
        {
            if (character >= '0' && character <= '9') return character - '0';
            if (character >= 'A' && character <= 'F') return character - 'A' + 10;
            if (character >= 'a' && character <= 'f') return character - 'a' + 10;
            return -1;
        }
    }
}
