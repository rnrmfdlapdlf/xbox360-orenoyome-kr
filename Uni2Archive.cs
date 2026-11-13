using System;
using System.Collections.Generic;
using System.IO;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// UNI2 archive (big-endian), same as tools/uni2.py.
    ///
    /// Header (sector 0): "UNI2", u32 version, u32 count, u32 toc_sector, u32 data_sector
    /// TOC (toc_sector):  count * (u32 id, u32 rel_sector, u32 n_sectors, u32 size)
    /// Data:              each entry at data_sector + rel_sector, 0x800 aligned, zero padded
    /// </summary>
    internal sealed class Uni2Archive
    {
        private const int Sector = 0x800;

        private readonly byte[] header;
        private readonly int tocSector;
        private readonly int dataSector;

        public sealed class Entry
        {
            public uint Id;
            public byte[] Data;
        }

        public List<Entry> Entries { get; private set; }

        public Uni2Archive(byte[] d)
        {
            if (!Bin.StartsWith(d, "UNI2"))
            {
                throw new InvalidDataException("UNI2 아카이브가 아닙니다.");
            }

            header = Bin.Slice(d, 0, Sector);
            int count = (int)Bin.U32BE(d, 8);
            tocSector = (int)Bin.U32BE(d, 12);
            dataSector = (int)Bin.U32BE(d, 16);
            Entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                int t = tocSector * Sector + i * 16;
                uint id = Bin.U32BE(d, t);
                int rel = (int)Bin.U32BE(d, t + 4);
                int size = (int)Bin.U32BE(d, t + 12);
                long off = (long)(dataSector + rel) * Sector;
                Entries.Add(new Entry { Id = id, Data = Bin.Slice(d, (int)off, size) });
            }
        }

        public static Uni2Archive Load(string path)
        {
            return new Uni2Archive(File.ReadAllBytes(path));
        }

        public Entry Find(uint id)
        {
            foreach (Entry e in Entries)
            {
                if (e.Id == id)
                {
                    return e;
                }
            }

            return null;
        }

        public void Save(string path)
        {
            using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                Write(f);
            }
        }

        public void Write(Stream output)
        {
            MemoryStream toc = new MemoryStream();
            long bodySize = 0;
            foreach (Entry e in Entries)
            {
                int nsec = (e.Data.Length + Sector - 1) / Sector;
                Bin.WriteU32BE(toc, e.Id);
                Bin.WriteU32BE(toc, (uint)(bodySize / Sector));
                Bin.WriteU32BE(toc, (uint)nsec);
                Bin.WriteU32BE(toc, (uint)e.Data.Length);
                bodySize += (long)nsec * Sector;
            }

            int tocSectors = (int)((toc.Length + Sector - 1) / Sector);
            if (tocSector + tocSectors > dataSector)
            {
                throw new InvalidDataException("UNI2 TOC가 데이터 영역을 넘었습니다.");
            }

            byte[] head = new byte[(long)dataSector * Sector];
            Buffer.BlockCopy(header, 0, head, 0, header.Length);
            byte[] tocBytes = toc.ToArray();
            Buffer.BlockCopy(tocBytes, 0, head, tocSector * Sector, tocBytes.Length);
            output.Write(head, 0, head.Length);

            byte[] pad = new byte[Sector];
            foreach (Entry e in Entries)
            {
                output.Write(e.Data, 0, e.Data.Length);
                int rest = (Sector - e.Data.Length % Sector) % Sector;
                output.Write(pad, 0, rest);
            }
        }
    }
}
