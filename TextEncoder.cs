using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// Korean text -> game bytes (same rules as tools/script_tool.py Encoder).
    ///
    /// Hangul goes through the charmap (SJIS codes of the redrawn glyph slots);
    /// everything else is cp932. Half-width punctuation, digits and Latin
    /// letters become the original full-width glyphs, which the game already
    /// positions correctly. Control codes (#Name[4], #n, %d ...) stay ASCII.
    /// </summary>
    internal sealed class TextEncoder
    {
        private static readonly Dictionary<char, char> Punct = new Dictionary<char, char>
        {
            { ' ', '\u3000' }, { '.', '\uFF0E' }, { ',', '\uFF0C' }, { '!', '\uFF01' },
            { '?', '\uFF1F' }, { '~', '\uFF5E' }, { '\u301C', '\uFF5E' }, { ':', '\uFF1A' },
            { ';', '\uFF1B' }, { '(', '\uFF08' }, { ')', '\uFF09' }, { '-', '\uFF0D' },
            { '\u0027', '\u2019' }, { '\u00B7', '\u30FB' }, { '/', '\uFF0F' }, { '&', '\uFF06' },
            { '%', '\uFF05' }, { '+', '\uFF0B' }, { '=', '\uFF1D' }, { '*', '\uFF0A' },
            { '#', '\uFF03' }, { '[', '\uFF3B' }, { ']', '\uFF3D' }, { '<', '\uFF1C' },
            { '>', '\uFF1E' }, { '@', '\uFF20' },
        };

        /// <summary>Written as a 1-byte ASCII space (the game draws it full width).</summary>
        private const char EnSpace = '\u2002';

        private static readonly Regex Control = new Regex(@"#(?:Name|Scale|Speed|Color)\[[^\]]*\]|#n|%[0-9]*[dsx]");

        /// <summary>Full-width ， and ． sit at the left of their cell: drop a following word space.</summary>
        private static readonly Regex SpaceAfterComma = new Regex("([,.\uFF0C\uFF0E]) +");

        private readonly Dictionary<char, byte[]> map = new Dictionary<char, byte[]>();

        public TextEncoder(string charmapPath)
        {
            foreach (string line in File.ReadLines(charmapPath, Encoding.UTF8))
            {
                if (line.Length < 3)
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                string hex = parts[1].Trim();
                byte[] code = new byte[hex.Length / 2];
                for (int i = 0; i < code.Length; i++)
                {
                    code[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }

                map[parts[0][0]] = code;
            }
        }

        /// <summary>Hangul character -> glyph slot code (also used by the DLC patcher).</summary>
        public bool TryGetGlyph(char ch, out byte[] code)
        {
            return map.TryGetValue(ch, out code);
        }

        public byte[] Encode(string text)
        {
            StringBuilder normalized = new StringBuilder();
            int pos = 0;
            foreach (Match m in Control.Matches(text))
            {
                normalized.Append(Normalize(text.Substring(pos, m.Index - pos)));
                normalized.Append(m.Value);
                pos = m.Index + m.Length;
            }

            normalized.Append(Normalize(text.Substring(pos)));

            List<byte> output = new List<byte>(normalized.Length * 2);
            char[] one = new char[1];
            foreach (char ch in normalized.ToString())
            {
                if (ch == EnSpace)
                {
                    output.Add(0x20);
                    continue;
                }

                byte[] code;
                if (map.TryGetValue(ch, out code))
                {
                    output.AddRange(code);
                    continue;
                }

                try
                {
                    one[0] = ch;
                    output.AddRange(ShiftJis.Strict.GetBytes(one));
                }
                catch (EncoderFallbackException)
                {
                    throw new InvalidDataException(String.Format("글리프가 없는 문자 '{0}' (U+{1:X4}): {2}", ch, (int)ch, text));
                }
            }

            return output.ToArray();
        }

        private static string Normalize(string text)
        {
            text = SpaceAfterComma.Replace(text, "$1");
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                char ch = c;
                char mapped;
                if (Punct.TryGetValue(ch, out mapped))
                {
                    ch = mapped;
                }
                else if ((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    ch = (char)(ch + 0xFEE0);
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }
    }
}
