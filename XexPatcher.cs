using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// default.xex: decrypts the retail executable, applies the font / system
    /// text changes (Exe/xex_patch.bin) and optionally the intro skip.
    ///
    /// The game's xex uses "basic" compression (data blocks + zero runs), so
    /// after the AES layer is removed the image can be patched in place and the
    /// file is written back unencrypted (encryption flag = 0).
    /// </summary>
    internal static class XexPatcher
    {
        /// <summary>Retail XEX2 key (public; used to unwrap the per-file image key).</summary>
        private static readonly byte[] RetailKey =
        {
            0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3, 0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91,
        };

        private const uint FileFormatInfoKey = 0x3FF;

        public static void Apply(string xexPath, PatchAssets assets, bool introSkip, PatchLog log)
        {
            byte[] xex = File.ReadAllBytes(xexPath);
            Decrypt(xex);

            string hash = Hash.Short(xex);
            if (hash == assets.XexPatchedHash)
            {
                throw new InvalidDataException("이미 한글 패치된 default.xex입니다. 원본 ISO를 사용해 주세요.");
            }

            if (hash != assets.XexDecryptedHash)
            {
                throw new InvalidDataException("default.xex가 지원하는 원본과 다릅니다. 원본 ISO를 사용해 주세요.");
            }

            int runs = ApplyRuns(xex, assets.XexPatchPath);
            log.Info(String.Format("default.xex: 글꼴/시스템 문장 {0:N0}곳 변경", runs));

            if (introSkip)
            {
                int words = ApplyIntroSkip(xex, assets.IntroSkipPath);
                log.Info(String.Format("default.xex: 인트로 스킵 ({0}개 명령어)", words));
            }

            File.WriteAllBytes(xexPath, xex);
        }

        /// <summary>Removes the AES layer in place (no-op for an already decrypted file).</summary>
        private static void Decrypt(byte[] xex)
        {
            if (!Bin.StartsWith(xex, "XEX2"))
            {
                throw new InvalidDataException("default.xex 형식이 아닙니다.");
            }

            int dataOffset = (int)Bin.U32BE(xex, 8);
            int securityOffset = (int)Bin.U32BE(xex, 16);
            int headerCount = (int)Bin.U32BE(xex, 20);
            int formatInfo = -1;
            for (int i = 0; i < headerCount; i++)
            {
                if (Bin.U32BE(xex, 24 + i * 8) == FileFormatInfoKey)
                {
                    formatInfo = (int)Bin.U32BE(xex, 28 + i * 8);
                }
            }

            if (formatInfo < 0)
            {
                throw new InvalidDataException("default.xex 파일 형식 정보가 없습니다.");
            }

            int encryption = Bin.U16BE(xex, formatInfo + 4);
            int compression = Bin.U16BE(xex, formatInfo + 6);
            if (compression != 1)
            {
                throw new InvalidDataException("지원하지 않는 default.xex 압축 형식입니다.");
            }

            if (encryption == 0)
            {
                return;
            }

            if (encryption != 1 || (xex.Length - dataOffset) % 16 != 0)
            {
                throw new InvalidDataException("지원하지 않는 default.xex 암호화 형식입니다.");
            }

            byte[] imageKey;
            using (Aes aes = CreateAes(RetailKey))
            using (ICryptoTransform dec = aes.CreateDecryptor())
            {
                imageKey = dec.TransformFinalBlock(xex, securityOffset + 0x150, 16);
            }

            using (Aes aes = CreateAes(imageKey))
            using (ICryptoTransform dec = aes.CreateDecryptor())
            {
                const int chunk = 1 << 20;
                byte[] plain = new byte[chunk];
                for (int o = dataOffset; o < xex.Length; o += chunk)
                {
                    int n = Math.Min(chunk, xex.Length - o);
                    int done = dec.TransformBlock(xex, o, n, plain, 0);   // CBC state carries over between chunks
                    if (done != n)
                    {
                        throw new InvalidDataException("default.xex 복호화 오류");
                    }

                    Buffer.BlockCopy(plain, 0, xex, o, n);
                }
            }

            xex[formatInfo + 4] = 0;
            xex[formatInfo + 5] = 0;
        }

        private static Aes CreateAes(byte[] key)
        {
            Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = new byte[16];
            return aes;
        }

        /// <summary>xex_patch.bin: "ONYX", u32 count, count * (u32 offset, u32 length, bytes).</summary>
        private static int ApplyRuns(byte[] xex, string patchPath)
        {
            byte[] p = File.ReadAllBytes(patchPath);
            if (!Bin.StartsWith(p, "ONYX"))
            {
                throw new InvalidDataException("xex_patch.bin 형식이 잘못되었습니다.");
            }

            int count = (int)Bin.U32LE(p, 4);
            int pos = 8;
            for (int i = 0; i < count; i++)
            {
                int offset = (int)Bin.U32LE(p, pos);
                int length = (int)Bin.U32LE(p, pos + 4);
                pos += 8;
                if (offset < 0 || length < 0 || offset + length > xex.Length || pos + length > p.Length)
                {
                    throw new InvalidDataException("xex_patch.bin 범위 오류");
                }

                Buffer.BlockCopy(p, pos, xex, offset, length);
                pos += length;
            }

            return count;
        }

        /// <summary>Boot movies end on their own after a few frames (instruction words, verified).</summary>
        private static int ApplyIntroSkip(byte[] xex, string path)
        {
            List<Dictionary<string, object>> rows = JsonLines.Read(path);
            foreach (Dictionary<string, object> row in rows)
            {
                int off = JsonLines.Int(row, "off");
                uint orig = Convert.ToUInt32(JsonLines.Str(row, "orig"), 16);
                if (Bin.U32BE(xex, off) != orig)
                {
                    throw new InvalidDataException(String.Format("인트로 스킵 위치(0x{0:x})의 원본 명령어가 다릅니다.", off));
                }
            }

            foreach (Dictionary<string, object> row in rows)
            {
                Bin.PutU32BE(xex, JsonLines.Int(row, "off"), Convert.ToUInt32(JsonLines.Str(row, "new"), 16));
            }

            return rows.Count;
        }
    }
}
