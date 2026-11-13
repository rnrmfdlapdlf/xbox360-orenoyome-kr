using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// Translates the DLC packages (same as tools/dlc_patch.py).
    ///
    /// Only the Shift-JIS XML file HEADER carries text: the values of
    /// Name / VoiceName / PersonalityName are replaced with Korean glyph codes
    /// (the game copies the raw bytes into 64-byte fields), every other byte is
    /// kept. The package is rebuilt (hash tables, content id) and written as
    /// &lt;new content id&gt;&lt;original suffix&gt; into "&lt;folder&gt;_repacked", keeping
    /// the folder layout (494607D8/00000002/...).
    /// </summary>
    internal static class DlcPatcher
    {
        public const uint TitleId = 0x494607D8;

        private const int FieldMax = 63;   // 64-byte field incl. NUL
        private const int VoiceMax = 15;   // voice names go to 16-byte buffers

        private static readonly HashSet<string> Tags = new HashSet<string> { "Name", "VoiceName", "PersonalityName" };

        /// <summary>Byte-level match (the HEADER is read as Latin-1 so offsets equal byte offsets).</summary>
        private static readonly Regex Element = new Regex(@"<([A-Za-z0-9_]+)>([^<]*)</\1>");

        /// <summary>Package files of this game under a folder (searched recursively).</summary>
        public static List<string> FindPackages(string folder)
        {
            List<string> found = new List<string>();
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(path);
                if (info.Length < 0xB000)
                {
                    continue;
                }

                byte[] head = new byte[0xB000];
                using (FileStream f = File.OpenRead(path))
                {
                    if (f.Read(head, 0, head.Length) != head.Length)
                    {
                        continue;
                    }
                }

                if (StfsPackage.IsPackage(head) && Bin.U32BE(head, 0x360) == TitleId)
                {
                    found.Add(path);
                }
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        public static string Apply(string folder, PatchAssets assets, TextEncoder encoder, PatchLog log)
        {
            folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            List<string> packages = FindPackages(folder);
            if (packages.Count == 0)
            {
                throw new InvalidDataException("폴더에서 이 게임의 DLC를 찾지 못했습니다: " + folder);
            }

            Dictionary<string, string> table = new Dictionary<string, string>();
            foreach (Dictionary<string, object> row in JsonLines.Read(assets.DlcPath))
            {
                table[JsonLines.Str(row, "h")] = JsonLines.Str(row, "ko");
            }

            string output = PathUtil.AvailableDirectory(folder + "_repacked");
            foreach (string path in packages)
            {
                StfsPackage package = new StfsPackage(File.ReadAllBytes(path));
                StfsPackage.FileEntry header = package.Find("HEADER");
                if (header == null)
                {
                    log.Info(Path.GetFileName(path) + ": HEADER 없음, 건너뜀");
                    continue;
                }

                List<string> changes;
                byte[] newHeader = PatchHeader(package.ReadFile(header), table, encoder, out changes);
                byte[] rebuilt = package.Rebuild(header, newHeader);

                string name = Path.GetFileName(path);
                string newName = name.Length >= 40 && String.Equals(name.Substring(0, 40), package.ContentIdHex, StringComparison.OrdinalIgnoreCase)
                    ? StfsPackage.ContentIdOf(rebuilt) + name.Substring(40)
                    : name;
                string relativeDir = Path.GetDirectoryName(path).Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar);
                string targetDir = Path.Combine(output, relativeDir);
                Directory.CreateDirectory(targetDir);
                File.WriteAllBytes(Path.Combine(targetDir, newName), rebuilt);
                log.Info(String.Format("{0} -> {1}  {2}", name.Substring(0, Math.Min(8, name.Length)), newName.Substring(0, Math.Min(8, newName.Length)), String.Join(", ", changes)));
            }

            return output;
        }

        private static byte[] PatchHeader(byte[] raw, Dictionary<string, string> table, TextEncoder encoder, out List<string> changes)
        {
            changes = new List<string>();
            string text = Encoding.GetEncoding(28591).GetString(raw);   // Latin-1: 1 char = 1 byte
            bool isVoice = text.Contains("VOICE");
            MemoryStream output = new MemoryStream();
            int pos = 0;
            foreach (Match m in Element.Matches(text))
            {
                string tag = m.Groups[1].Value;
                if (!Tags.Contains(tag))
                {
                    continue;
                }

                Group value = m.Groups[2];
                string hash = Hash.Short(raw, value.Index, value.Length);
                string korean;
                if (!table.TryGetValue(hash, out korean))
                {
                    throw new InvalidDataException("DLC 번역이 없는 항목입니다: <" + tag + ">");
                }

                byte[] encoded = EncodeValue(korean, encoder);
                int limit = tag == "VoiceName" || (tag == "Name" && isVoice) ? VoiceMax : FieldMax;
                if (encoded.Length > limit)
                {
                    throw new InvalidDataException(String.Format("<{0}>{1} 이(가) {2}바이트입니다 (최대 {3}).", tag, korean, encoded.Length, limit));
                }

                output.Write(raw, pos, value.Index - pos);
                output.Write(encoded, 0, encoded.Length);
                pos = value.Index + value.Length;
                changes.Add(tag + "=" + korean);
            }

            output.Write(raw, pos, raw.Length - pos);
            return output.ToArray();
        }

        /// <summary>Hangul through the glyph map, ASCII as is (no other characters).</summary>
        private static byte[] EncodeValue(string text, TextEncoder encoder)
        {
            List<byte> result = new List<byte>();
            foreach (char ch in text)
            {
                byte[] code;
                if (encoder.TryGetGlyph(ch, out code))
                {
                    result.AddRange(code);
                }
                else if (ch < 0x80)
                {
                    if (ch == '<' || ch == '>' || ch == '&')
                    {
                        throw new InvalidDataException("DLC 번역에 XML 특수문자가 있습니다: " + text);
                    }

                    result.Add((byte)ch);
                }
                else
                {
                    throw new InvalidDataException(String.Format("글리프가 없는 문자 '{0}': {1}", ch, text));
                }
            }

            foreach (byte b in result)
            {
                if (b == (byte)'<' || b == (byte)'>' || b == (byte)'&')
                {
                    throw new InvalidDataException("DLC 번역 코드에 XML 특수 바이트가 있습니다: " + text);
                }
            }

            return result.ToArray();
        }
    }
}
