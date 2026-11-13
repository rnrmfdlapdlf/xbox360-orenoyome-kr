using System;
using System.Collections.Generic;
using System.IO;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// STCM2B script (big-endian), same as tools/stcm2.py.
    ///
    ///   0x000  "STCM2B ..." header, 0x020 export_table_offset, export_count
    ///   ....   "CODE_START_\0" + command stream
    ///   ....   "EXPORT_DATA\0" + export_count * (kind, name[32], addr)
    ///
    /// Command: [kind][op][argc][length] + argc * 12-byte args + extra area.
    /// kind 1 -> op is the absolute address of a local function (call).
    /// Arg v = 0xffffff41: t1 = absolute address of a command (jump).
    /// Arg v pointing inside its own command: pointer to a data object
    /// [0][nwords][1][nbytes] + payload in the extra area (text lives here).
    ///
    /// Rebuilding relocates call targets, jump targets, object pointers,
    /// export addresses and the export table offset.
    /// </summary>
    internal sealed class Stcm2Script
    {
        public const uint Label = 0xFFFFFF41;

        private static readonly byte[] CodeStartMarker = System.Text.Encoding.ASCII.GetBytes("CODE_START_\0");
        private static readonly byte[] ExportMarker = System.Text.Encoding.ASCII.GetBytes("EXPORT_DATA\0");

        public sealed class DataObject
        {
            public int Offset;      // offset inside the extra area
            public byte[] Payload;  // nbytes (NUL padded to 4)
        }

        public sealed class Command
        {
            public int Addr;
            public uint Kind;
            public uint Op;
            public uint[][] Args;            // [v, t1, t2]
            public byte[] Extra;
            public int RawLength;
            public List<DataObject> Objects; // null when the extra area is not a pure object list

            public int ExtraStart { get { return Addr + 16 + 12 * Args.Length; } }
        }

        private readonly byte[] prefix;
        private readonly List<byte[]> exportNames = new List<byte[]>();   // 36 bytes: kind + name
        private readonly List<uint> exportAddrs = new List<uint>();
        private readonly int codeStart;

        public List<Command> Commands { get; private set; }

        public Stcm2Script(byte[] d)
        {
            if (!Bin.StartsWith(d, "STCM2B"))
            {
                throw new InvalidDataException("STCM2B 스크립트가 아닙니다.");
            }

            int expOff = (int)Bin.U32BE(d, 0x20);
            int expCnt = (int)Bin.U32BE(d, 0x24);
            if (expOff < 12 || Bin.IndexOf(d, ExportMarker, expOff - 12) != expOff - 12)
            {
                throw new InvalidDataException("EXPORT_DATA 표식이 없습니다.");
            }

            int cs = Bin.IndexOf(d, CodeStartMarker, 0);
            if (cs < 0)
            {
                throw new InvalidDataException("CODE_START_ 표식이 없습니다.");
            }

            codeStart = cs + 12;
            int codeEnd = expOff - 12;
            prefix = Bin.Slice(d, 0, codeStart);
            for (int i = 0; i < expCnt; i++)
            {
                int e = expOff + i * 40;
                exportNames.Add(Bin.Slice(d, e, 36));
                exportAddrs.Add(Bin.U32BE(d, e + 36));
            }

            for (int p = expOff + 40 * expCnt; p < d.Length; p++)
            {
                if (d[p] != 0)
                {
                    throw new InvalidDataException("export 테이블 뒤에 예상하지 못한 데이터가 있습니다.");
                }
            }

            Commands = new List<Command>();
            int o = codeStart;
            while (o < codeEnd)
            {
                uint kind = Bin.U32BE(d, o);
                uint op = Bin.U32BE(d, o + 4);
                int argc = (int)Bin.U32BE(d, o + 8);
                int len = (int)Bin.U32BE(d, o + 12);
                if (kind > 1 || argc < 0 || len < 16 + 12 * argc || len % 4 != 0 || o + len > codeEnd)
                {
                    throw new InvalidDataException(String.Format("잘못된 명령 (0x{0:x})", o));
                }

                Command c = new Command { Addr = o, Kind = kind, Op = op, RawLength = len, Args = new uint[argc][] };
                for (int i = 0; i < argc; i++)
                {
                    int a = o + 16 + 12 * i;
                    c.Args[i] = new[] { Bin.U32BE(d, a), Bin.U32BE(d, a + 4), Bin.U32BE(d, a + 8) };
                }

                int ex0 = o + 16 + 12 * argc;
                c.Extra = Bin.Slice(d, ex0, o + len - ex0);
                c.Objects = ParseObjects(c.Extra);
                Commands.Add(c);
                o += len;
            }

            if (o != codeEnd)
            {
                throw new InvalidDataException("명령 스트림이 EXPORT_DATA에서 끝나지 않습니다.");
            }
        }

        private static List<DataObject> ParseObjects(byte[] extra)
        {
            List<DataObject> objs = new List<DataObject>();
            int p = 0;
            while (p < extra.Length)
            {
                if (p + 16 > extra.Length)
                {
                    return null;
                }

                uint z = Bin.U32BE(extra, p);
                uint nw = Bin.U32BE(extra, p + 4);
                uint one = Bin.U32BE(extra, p + 8);
                uint nb = Bin.U32BE(extra, p + 12);
                if (z != 0 || one != 1 || nb != nw * 4 || p + 16 + (long)nb > extra.Length)
                {
                    return null;
                }

                objs.Add(new DataObject { Offset = p, Payload = Bin.Slice(extra, p + 16, (int)nb) });
                p += 16 + (int)nb;
            }

            return objs;
        }

        /// <summary>
        /// Every data object referenced from an argument: key "cmd:arg" -> object
        /// (same ids as the JSON files of script_tool.py).
        /// </summary>
        public Dictionary<string, DataObject> Strings()
        {
            Dictionary<string, DataObject> result = new Dictionary<string, DataObject>();
            for (int ci = 0; ci < Commands.Count; ci++)
            {
                Command c = Commands[ci];
                if (c.Objects == null || c.Objects.Count == 0)
                {
                    continue;
                }

                for (int ai = 0; ai < c.Args.Length; ai++)
                {
                    uint v = c.Args[ai][0];
                    if (v >> 24 == 0xFF)
                    {
                        continue;
                    }

                    long rel = (long)v - c.ExtraStart;
                    foreach (DataObject ob in c.Objects)
                    {
                        if (ob.Offset == rel)
                        {
                            result[ci + ":" + ai] = ob;
                            break;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Object text without the NUL padding.</summary>
        public static byte[] TextOf(DataObject ob)
        {
            int n = Array.IndexOf(ob.Payload, (byte)0);
            return Bin.Slice(ob.Payload, 0, n < 0 ? ob.Payload.Length : n);
        }

        /// <summary>Replaces an object's text (at least one NUL, word aligned).</summary>
        public static void SetText(DataObject ob, byte[] raw)
        {
            int n = (raw.Length + 1 + 3) / 4 * 4;
            byte[] payload = new byte[n];
            Buffer.BlockCopy(raw, 0, payload, 0, raw.Length);
            ob.Payload = payload;
        }

        public byte[] Build()
        {
            // 1) new layout
            Dictionary<int, int> newAddr = new Dictionary<int, int>();
            List<byte[]> extras = new List<byte[]>();
            List<Dictionary<int, int>> objMaps = new List<Dictionary<int, int>>();
            List<int> lengths = new List<int>();
            int o = codeStart;
            foreach (Command c in Commands)
            {
                byte[] extra;
                Dictionary<int, int> omap = null;
                if (c.Objects != null)
                {
                    MemoryStream buf = new MemoryStream();
                    omap = new Dictionary<int, int>();
                    foreach (DataObject ob in c.Objects)
                    {
                        omap[ob.Offset] = (int)buf.Length;
                        Bin.WriteU32BE(buf, 0);
                        Bin.WriteU32BE(buf, (uint)(ob.Payload.Length / 4));
                        Bin.WriteU32BE(buf, 1);
                        Bin.WriteU32BE(buf, (uint)ob.Payload.Length);
                        buf.Write(ob.Payload, 0, ob.Payload.Length);
                    }

                    extra = buf.ToArray();
                }
                else
                {
                    extra = c.Extra;
                }

                int len = 16 + 12 * c.Args.Length + extra.Length;
                newAddr[c.Addr] = o;
                extras.Add(extra);
                objMaps.Add(omap);
                lengths.Add(len);
                o += len;
            }

            int codeEnd = o;

            // 2) write with relocation
            MemoryStream outp = new MemoryStream(codeEnd + 64 + exportNames.Count * 40);
            outp.Write(prefix, 0, prefix.Length);
            for (int i = 0; i < Commands.Count; i++)
            {
                Command c = Commands[i];
                int na = newAddr[c.Addr];
                int oldEx0 = c.ExtraStart;
                int newEx0 = na + 16 + 12 * c.Args.Length;
                uint op = c.Kind == 1 ? Reloc(newAddr, c.Op) : c.Op;
                Bin.WriteU32BE(outp, c.Kind);
                Bin.WriteU32BE(outp, op);
                Bin.WriteU32BE(outp, (uint)c.Args.Length);
                Bin.WriteU32BE(outp, (uint)lengths[i]);
                foreach (uint[] arg in c.Args)
                {
                    uint v = arg[0];
                    uint t1 = arg[1];
                    if (v == Label)
                    {
                        t1 = Reloc(newAddr, t1);
                    }
                    else if (v >> 24 != 0xFF && c.Addr <= v && v < c.Addr + c.RawLength)
                    {
                        int rel = (int)v - oldEx0;
                        if (objMaps[i] != null)
                        {
                            int mapped;
                            if (!objMaps[i].TryGetValue(rel, out mapped))
                            {
                                throw new InvalidDataException(String.Format("객체 중간을 가리키는 포인터 (0x{0:x})", c.Addr));
                            }

                            v = (uint)(newEx0 + mapped);
                        }
                        else
                        {
                            v = (uint)((int)v - c.Addr + na);
                        }
                    }

                    Bin.WriteU32BE(outp, v);
                    Bin.WriteU32BE(outp, t1);
                    Bin.WriteU32BE(outp, arg[2]);
                }

                outp.Write(extras[i], 0, extras[i].Length);
            }

            if (outp.Length != codeEnd)
            {
                throw new InvalidDataException("스크립트 재구성 길이 오류");
            }

            outp.Write(ExportMarker, 0, ExportMarker.Length);
            int expOff = (int)outp.Length;
            for (int i = 0; i < exportNames.Count; i++)
            {
                outp.Write(exportNames[i], 0, 36);
                Bin.WriteU32BE(outp, Reloc(newAddr, exportAddrs[i]));
            }

            while (outp.Length % 16 != 0)
            {
                outp.WriteByte(0);
            }

            byte[] result = outp.ToArray();
            Bin.PutU32BE(result, 0x20, (uint)expOff);
            return result;
        }

        /// <summary>Targets outside this file are kept verbatim.</summary>
        private static uint Reloc(Dictionary<int, int> newAddr, uint addr)
        {
            int mapped;
            return addr <= int.MaxValue && newAddr.TryGetValue((int)addr, out mapped) ? (uint)mapped : addr;
        }
    }
}
