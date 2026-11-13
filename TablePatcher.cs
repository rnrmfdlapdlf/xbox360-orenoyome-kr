using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// rom_root/Table/*.bin (CSVB) text tables: minigame speech bubbles and the
    /// quiz (same as tools/csvb.py build).
    ///
    /// Little-endian chunks: 'CSVB' size, then tag[4] + size (incl. 8-byte header):
    ///   Info  u16 row_size, u16 rows
    ///   TblB  rows (row_size each) + string pool; string cells hold an offset
    ///         relative to the TblB data start (cp932, NUL terminated)
    ///   DevI  table name[32], u16 ncols, ..., ncols * (name[16], u16 type,
    ///         u16 count, u16, u16 ofs), row labels at the end. type 4 = string.
    /// The pool is rebuilt in row order with identical strings shared and padded to 4.
    /// </summary>
    internal static class TablePatcher
    {
        private sealed class Cell
        {
            public int Row;
            public string Column;
            public int Index;
            public int CellOffset;   // inside the table body
            public byte[] Original;
        }

        private sealed class Translation
        {
            public string Hash;
            public string Korean;
            public bool Clear;
        }

        public static void Apply(string tableDir, PatchAssets assets, TextEncoder encoder, PatchLog log)
        {
            Dictionary<string, Dictionary<string, Translation>> byFile = new Dictionary<string, Dictionary<string, Translation>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> row in JsonLines.Read(assets.TablePath))
            {
                string file = JsonLines.Str(row, "file");
                Dictionary<string, Translation> cells;
                if (!byFile.TryGetValue(file, out cells))
                {
                    cells = new Dictionary<string, Translation>();
                    byFile[file] = cells;
                }

                string key = Key(JsonLines.Int(row, "row"), JsonLines.Str(row, "col"), JsonLines.Int(row, "i"));
                cells[key] = new Translation
                {
                    Hash = JsonLines.Str(row, "h"),
                    Korean = JsonLines.Str(row, "ko"),
                    Clear = JsonLines.Bool(row, "clear"),
                };
            }

            List<string> names = new List<string>(byFile.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                string path = Path.Combine(tableDir, name);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("테이블 파일을 찾을 수 없습니다: " + name, path);
                }

                byte[] original = File.ReadAllBytes(path);
                byte[] patched = Build(name, original, byFile[name], encoder);
                File.WriteAllBytes(path, patched);
                log.Info(String.Format("{0}: 문장 {1:N0}개 ({2:N0} -> {3:N0} bytes)", name, byFile[name].Count, original.Length, patched.Length));
            }
        }

        private static string Key(int row, string column, int index)
        {
            return row + "/" + column + "/" + index;
        }

        private static Dictionary<string, int[]> Chunks(byte[] d)
        {
            if (!Bin.StartsWith(d, "CSVB"))
            {
                throw new InvalidDataException("CSVB 테이블이 아닙니다.");
            }

            Dictionary<string, int[]> chunks = new Dictionary<string, int[]>();
            int p = 8;
            while (p + 8 <= d.Length)
            {
                string tag = Encoding.ASCII.GetString(d, p, 4);
                int n = (int)Bin.U32LE(d, p + 4);
                chunks[tag] = new[] { p, n };
                if (tag == "Term" || n < 8)
                {
                    break;
                }

                p += n;
            }

            foreach (string need in new[] { "Info", "TblB", "DevI" })
            {
                if (!chunks.ContainsKey(need))
                {
                    throw new InvalidDataException("CSVB 청크가 없습니다: " + need);
                }
            }

            return chunks;
        }

        private static byte[] Build(string name, byte[] d, Dictionary<string, Translation> translations, TextEncoder encoder)
        {
            Dictionary<string, int[]> c = Chunks(d);
            int ip = c["Info"][0];
            int rowSize = Bin.U16LE(d, ip + 8);
            int rows = Bin.U16LE(d, ip + 10);
            int tp = c["TblB"][0];
            int tn = c["TblB"][1];
            int bodyStart = tp + 8;
            int dp = c["DevI"][0];

            // string columns
            int ncols = Bin.U16LE(d, dp + 40);
            List<Tuple<string, int, int>> stringColumns = new List<Tuple<string, int, int>>();   // name, count, ofs
            for (int i = 0; i < ncols; i++)
            {
                int o = dp + 44 + i * 24;
                int len = 0;
                while (len < 16 && d[o + len] != 0)
                {
                    len++;
                }

                string colName = Encoding.ASCII.GetString(d, o, len);
                int type = Bin.U16LE(d, o + 16);
                int count = Bin.U16LE(d, o + 18);
                int ofs = Bin.U16LE(d, o + 22);
                if (type == 4)
                {
                    stringColumns.Add(Tuple.Create(colName, count, ofs));
                }
            }

            List<Cell> cells = new List<Cell>();
            for (int r = 0; r < rows; r++)
            {
                foreach (Tuple<string, int, int> col in stringColumns)
                {
                    for (int i = 0; i < col.Item2; i++)
                    {
                        int cell = r * rowSize + col.Item3 + i * 4;
                        int so = (int)Bin.U32LE(d, bodyStart + cell);
                        int start = bodyStart + so;
                        int end = Array.IndexOf(d, (byte)0, start);
                        cells.Add(new Cell { Row = r, Column = col.Item1, Index = i, CellOffset = cell, Original = Bin.Slice(d, start, end - start) });
                    }
                }
            }

            // every translation must hit a cell with the expected original text
            Dictionary<string, Cell> cellByKey = new Dictionary<string, Cell>();
            foreach (Cell cell in cells)
            {
                cellByKey[Key(cell.Row, cell.Column, cell.Index)] = cell;
            }

            foreach (KeyValuePair<string, Translation> t in translations)
            {
                Cell cell;
                if (!cellByKey.TryGetValue(t.Key, out cell) || Hash.Short(cell.Original) != t.Value.Hash)
                {
                    throw new InvalidDataException(String.Format("{0} {1}: 원본 문장이 다릅니다. 지원하는 원본 ISO인지 확인해 주세요.", name, t.Key));
                }
            }

            int poolStart = rows * rowSize;
            byte[] table = Bin.Slice(d, bodyStart, poolStart);
            MemoryStream pool = new MemoryStream();
            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach (Cell cell in cells)
            {
                Translation t;
                byte[] raw;
                if (!translations.TryGetValue(Key(cell.Row, cell.Column, cell.Index), out t))
                {
                    raw = cell.Original;
                }
                else if (t.Clear)
                {
                    raw = new byte[0];             // translation needs fewer lines
                }
                else if (t.Korean.Length > 0)
                {
                    raw = encoder.Encode(t.Korean);
                }
                else
                {
                    raw = cell.Original;
                }

                string id = Convert.ToBase64String(raw);
                int at;
                if (!seen.TryGetValue(id, out at))          // identical strings share one pool entry
                {
                    at = poolStart + (int)pool.Length;
                    seen[id] = at;
                    pool.Write(raw, 0, raw.Length);
                    pool.WriteByte(0);
                }

                Bin.PutU32LE(table, cell.CellOffset, (uint)at);
            }

            while ((table.Length + pool.Length) % 4 != 0)
            {
                pool.WriteByte(0);
            }

            MemoryStream output = new MemoryStream();
            output.Write(d, 0, tp);
            output.Write(Encoding.ASCII.GetBytes("TblB"), 0, 4);
            byte[] size = BitConverter.GetBytes((uint)(table.Length + pool.Length + 8));
            output.Write(size, 0, 4);
            output.Write(table, 0, table.Length);
            byte[] poolBytes = pool.ToArray();
            output.Write(poolBytes, 0, poolBytes.Length);
            output.Write(d, tp + tn, d.Length - (tp + tn));
            byte[] result = output.ToArray();
            Bin.PutU32LE(result, 4, (uint)result.Length);
            return result;
        }
    }
}
