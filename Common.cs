using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>Big-endian / little-endian integer helpers.</summary>
    internal static class Bin
    {
        public static uint U32BE(byte[] d, int o)
        {
            return (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
        }

        public static void PutU32BE(byte[] d, int o, uint v)
        {
            d[o] = (byte)(v >> 24);
            d[o + 1] = (byte)(v >> 16);
            d[o + 2] = (byte)(v >> 8);
            d[o + 3] = (byte)v;
        }

        public static void WriteU32BE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        public static ushort U16BE(byte[] d, int o)
        {
            return (ushort)(d[o] << 8 | d[o + 1]);
        }

        public static uint U32LE(byte[] d, int o)
        {
            return BitConverter.ToUInt32(d, o);
        }

        public static ushort U16LE(byte[] d, int o)
        {
            return BitConverter.ToUInt16(d, o);
        }

        public static void PutU32LE(byte[] d, int o, uint v)
        {
            d[o] = (byte)v;
            d[o + 1] = (byte)(v >> 8);
            d[o + 2] = (byte)(v >> 16);
            d[o + 3] = (byte)(v >> 24);
        }

        public static int U24LE(byte[] d, int o)
        {
            return d[o] | d[o + 1] << 8 | d[o + 2] << 16;
        }

        public static byte[] Slice(byte[] d, int o, int n)
        {
            byte[] r = new byte[n];
            Buffer.BlockCopy(d, o, r, 0, n);
            return r;
        }

        public static bool StartsWith(byte[] d, string ascii)
        {
            if (d.Length < ascii.Length)
            {
                return false;
            }

            for (int i = 0; i < ascii.Length; i++)
            {
                if (d[i] != (byte)ascii[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static int IndexOf(byte[] d, byte[] pattern, int start)
        {
            for (int i = start; i <= d.Length - pattern.Length; i++)
            {
                int k = 0;
                while (k < pattern.Length && d[i + k] == pattern[k])
                {
                    k++;
                }

                if (k == pattern.Length)
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static class Hash
    {
        public static byte[] Sha1(byte[] data, int offset, int count)
        {
            using (SHA1 sha = SHA1.Create())
            {
                return sha.ComputeHash(data, offset, count);
            }
        }

        /// <summary>First 16 hex digits of SHA1 — the "h" field of the asset files.</summary>
        public static string Short(byte[] data)
        {
            return Short(data, 0, data.Length);
        }

        public static string Short(byte[] data, int offset, int count)
        {
            byte[] sha = Sha1(data, offset, count);
            StringBuilder sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
            {
                sb.Append(sha[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }

    internal static class ShiftJis
    {
        /// <summary>Code page 932 that throws instead of silently replacing characters.</summary>
        public static readonly Encoding Strict = Encoding.GetEncoding(
            932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>Reads the *.jsonl asset files (one JSON object per line).</summary>
    internal static class JsonLines
    {
        public static List<Dictionary<string, object>> Read(string path)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            int lineNo = 0;
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                lineNo++;
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                try
                {
                    rows.Add(json.Deserialize<Dictionary<string, object>>(line));
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(Path.GetFileName(path) + " " + lineNo + "번째 줄을 읽을 수 없습니다: " + ex.Message);
                }
            }

            return rows;
        }

        public static string Str(Dictionary<string, object> row, string key)
        {
            object v;
            return row.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        public static int Int(Dictionary<string, object> row, string key)
        {
            return Convert.ToInt32(row[key]);
        }

        public static bool Bool(Dictionary<string, object> row, string key)
        {
            object v;
            return row.TryGetValue(key, out v) && v is bool && (bool)v;
        }
    }

    /// <summary>Progress / log sink used by every patch step.</summary>
    internal sealed class PatchLog
    {
        private readonly Action<int, string, bool> sink;

        public PatchLog(Action<int, string, bool> sink)
        {
            this.sink = sink;
        }

        /// <summary>Moves the progress bar and writes a line to the log.</summary>
        public void Stage(int percent, string message)
        {
            sink(percent, message, true);
        }

        /// <summary>Writes a detail line to the log (progress unchanged).</summary>
        public void Info(string message)
        {
            sink(-1, "  " + message, true);
        }
    }
}
