using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// Minimal read-only STFS (LIVE/PIRS) package: file table, file read and a
    /// rebuild with replaced files (same as tools/stfs.py).
    ///
    /// Supported layout (all Ore no Yome DLC): header 0xB000, one hash table
    /// per level, at most 0x70E4 blocks (no level-2 hash table). L0 table of
    /// group g at physical block g*0xAB + (g ? 1 : 0), L1 table at physical
    /// 0xAB, top hash at 0x381, content id = SHA1 of 0x344..0xB000 at 0x32C.
    /// The RSA signature is not regenerated (Xenia does not check it).
    /// </summary>
    internal sealed class StfsPackage
    {
        public sealed class FileEntry
        {
            public int Index;
            public string Path;
            public bool IsDirectory;
            public bool Consecutive;
            public int Blocks;
            public int Start;
            public int Size;
            public int Parent;
        }

        private const int HeaderBase = 0xB000;
        private const int BlockSize = 0x1000;

        private readonly byte[] data;
        private readonly int fileTableBlocks;
        private readonly int fileTableStart;
        private readonly int totalBlocks;

        public uint TitleId { get; private set; }
        public string ContentIdHex { get; private set; }
        public List<FileEntry> Files { get; private set; }

        public StfsPackage(byte[] data)
        {
            this.data = data;
            if (!IsPackage(data))
            {
                throw new InvalidDataException("STFS 패키지가 아닙니다.");
            }

            int headerSize = (int)Bin.U32BE(data, 0x340);
            if (((headerSize + 0xFFF) & 0xF000) != HeaderBase)
            {
                throw new InvalidDataException("지원하지 않는 STFS 구조입니다.");
            }

            TitleId = Bin.U32BE(data, 0x360);
            int vd = 0x379;
            fileTableBlocks = Bin.U16LE(data, vd + 3);
            fileTableStart = Bin.U24LE(data, vd + 5);
            totalBlocks = (int)Bin.U32BE(data, vd + 0x1C);
            ContentIdHex = ToHex(data, 0x32C, 20);
            Files = ReadFileTable();
        }

        public static bool IsPackage(byte[] head)
        {
            return head.Length >= HeaderBase && (Bin.StartsWith(head, "LIVE") || Bin.StartsWith(head, "PIRS"));
        }

        private static string ToHex(byte[] d, int o, int n)
        {
            return BitConverter.ToString(d, o, n).Replace("-", "");
        }

        /// <summary>Data block number -> file offset.</summary>
        private static long BlockOffset(int b)
        {
            int baseCount = (b + 0xAA) / 0xAA;
            if (b >= 0xAA)
            {
                baseCount += (b + 0x70E4) / 0x70E4;
                if (b >= 0x70E4)
                {
                    baseCount += (b + 0x4AF768) / 0x4AF768;
                }
            }

            return HeaderBase + ((long)(b + baseCount) << 12);
        }

        private static long PhysicalOffset(int p)
        {
            return HeaderBase + (long)p * BlockSize;
        }

        private List<FileEntry> ReadFileTable()
        {
            List<FileEntry> list = new List<FileEntry>();
            for (int bi = 0; bi < fileTableBlocks; bi++)
            {
                long blockOff = BlockOffset(fileTableStart + bi);
                for (int k = 0; k < BlockSize / 0x40; k++)
                {
                    int e = (int)blockOff + k * 0x40;
                    if (data[e] == 0)
                    {
                        continue;
                    }

                    int flags = data[e + 0x28];
                    int nameLen = flags & 0x3F;
                    char[] name = new char[nameLen];
                    for (int i = 0; i < nameLen; i++)
                    {
                        name[i] = (char)data[e + i];
                    }

                    list.Add(new FileEntry
                    {
                        Index = bi * (BlockSize / 0x40) + k,
                        Path = new string(name),
                        IsDirectory = (flags & 0x80) != 0,
                        Consecutive = (flags & 0x40) != 0,
                        Blocks = Bin.U24LE(data, e + 0x29),
                        Start = Bin.U24LE(data, e + 0x2F),
                        Parent = (short)Bin.U16BE(data, e + 0x32),
                        Size = (int)Bin.U32BE(data, e + 0x34),
                    });
                }
            }

            Dictionary<int, FileEntry> byIndex = new Dictionary<int, FileEntry>();
            foreach (FileEntry f in list)
            {
                byIndex[f.Index] = f;
            }

            foreach (FileEntry f in list)
            {
                string path = f.Path;
                int p = f.Parent;
                FileEntry parent;
                while (p != -1 && byIndex.TryGetValue(p, out parent))
                {
                    path = parent.Path + "/" + path;
                    p = parent.Parent;
                }

                f.Path = path;
            }

            return list;
        }

        public FileEntry Find(string path)
        {
            foreach (FileEntry f in Files)
            {
                if (!f.IsDirectory && f.Path == path)
                {
                    return f;
                }
            }

            return null;
        }

        public byte[] ReadFile(FileEntry f)
        {
            if (!f.Consecutive)
            {
                throw new InvalidDataException("연속되지 않은 파일은 지원하지 않습니다: " + f.Path);
            }

            byte[] result = new byte[f.Size];
            for (int i = 0, done = 0; done < f.Size; i++)
            {
                int n = Math.Min(BlockSize, f.Size - done);
                Buffer.BlockCopy(data, (int)BlockOffset(f.Start + i), result, done, n);
                done += n;
            }

            return result;
        }

        /// <summary>
        /// Returns the package bytes with one file replaced (same block count)
        /// and all hash tables, the top hash and the content id recomputed.
        /// </summary>
        public byte[] Rebuild(FileEntry f, byte[] newContent)
        {
            if (totalBlocks > 0x70E4)
            {
                throw new InvalidDataException("지원하지 않는 STFS 크기입니다.");
            }

            if (!f.Consecutive)
            {
                throw new InvalidDataException("연속되지 않은 파일은 지원하지 않습니다: " + f.Path);
            }

            int blocks = (newContent.Length + BlockSize - 1) / BlockSize;
            if (blocks != f.Blocks)
            {
                throw new InvalidDataException(String.Format("{0}: 블록 수가 바뀌었습니다 ({1} -> {2}).", f.Path, f.Blocks, blocks));
            }

            byte[] d = (byte[])data.Clone();

            // 1) file content + size in the file table
            for (int i = 0; i < blocks; i++)
            {
                int o = (int)BlockOffset(f.Start + i);
                Array.Clear(d, o, BlockSize);
                Buffer.BlockCopy(newContent, i * BlockSize, d, o, Math.Min(BlockSize, newContent.Length - i * BlockSize));
            }

            int tableBlock = f.Index * 0x40 / BlockSize;
            int entryOffset = (int)BlockOffset(fileTableStart + tableBlock) + (f.Index * 0x40) % BlockSize;
            Bin.PutU32BE(d, entryOffset + 0x34, (uint)newContent.Length);

            using (SHA1 sha = SHA1.Create())
            {
                // 2) level 0 tables
                int groups = (totalBlocks + 0xA9) / 0xAA;
                int[] l0 = new int[groups];
                for (int g = 0; g < groups; g++)
                {
                    l0[g] = g == 0 ? 0 : g * 0xAB + 1;
                }

                for (int b = 0; b < totalBlocks; b++)
                {
                    int g = b / 0xAA;
                    int k = b % 0xAA;
                    byte[] digest = sha.ComputeHash(d, (int)BlockOffset(b), BlockSize);
                    Buffer.BlockCopy(digest, 0, d, (int)PhysicalOffset(l0[g]) + k * 0x18, 20);
                }

                // 3) level 1 table / top hash
                byte[] top;
                if (groups > 1)
                {
                    int l1 = (int)PhysicalOffset(0xAB);
                    for (int g = 0; g < groups; g++)
                    {
                        byte[] digest = sha.ComputeHash(d, (int)PhysicalOffset(l0[g]), BlockSize);
                        Buffer.BlockCopy(digest, 0, d, l1 + g * 0x18, 20);
                    }

                    top = sha.ComputeHash(d, l1, BlockSize);
                }
                else
                {
                    top = sha.ComputeHash(d, (int)PhysicalOffset(0), BlockSize);
                }

                Buffer.BlockCopy(top, 0, d, 0x381, 20);
                byte[] contentId = sha.ComputeHash(d, 0x344, HeaderBase - 0x344);
                Buffer.BlockCopy(contentId, 0, d, 0x32C, 20);
            }

            return d;
        }

        public static string ContentIdOf(byte[] package)
        {
            return ToHex(package, 0x32C, 20);
        }
    }
}
