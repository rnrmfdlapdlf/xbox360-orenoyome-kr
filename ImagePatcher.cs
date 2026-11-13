using System;
using System.Collections.Generic;
using System.IO;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// Replaces the Japanese UI textures in rom_root/UNION/*.UNI with the
    /// Korean ones (Images/*.bin are complete, already compressed UNI entries;
    /// the original entry is checked by hash before it is replaced).
    /// </summary>
    internal static class ImagePatcher
    {
        public static int Apply(string unionDir, PatchAssets assets, PatchLog log)
        {
            Dictionary<string, List<Dictionary<string, object>>> byArchive = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();
            foreach (Dictionary<string, object> row in JsonLines.Read(assets.ImageListPath))
            {
                string arc = JsonLines.Str(row, "arc");
                if (!byArchive.ContainsKey(arc))
                {
                    byArchive[arc] = new List<Dictionary<string, object>>();
                    order.Add(arc);
                }

                byArchive[arc].Add(row);
            }

            int total = 0;
            foreach (string arc in order)
            {
                string path = Path.Combine(unionDir, arc);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("이미지 아카이브를 찾을 수 없습니다: " + arc, path);
                }

                Uni2Archive archive = Uni2Archive.Load(path);
                foreach (Dictionary<string, object> row in byArchive[arc])
                {
                    uint fid = (uint)JsonLines.Int(row, "fid");
                    Uni2Archive.Entry entry = archive.Find(fid);
                    if (entry == null || Hash.Short(entry.Data) != JsonLines.Str(row, "h"))
                    {
                        throw new InvalidDataException(String.Format("{0} {1}: 원본 이미지가 다릅니다. 지원하는 원본 ISO인지 확인해 주세요.", arc, fid));
                    }

                    entry.Data = File.ReadAllBytes(Path.Combine(assets.ImagesDir, JsonLines.Str(row, "file")));
                }

                archive.Save(path);
                total += byArchive[arc].Count;
                log.Info(String.Format("{0}: 이미지 {1}개", arc, byArchive[arc].Count));
            }

            return total;
        }
    }
}
