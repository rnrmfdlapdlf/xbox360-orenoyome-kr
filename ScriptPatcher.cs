using System;
using System.Collections.Generic;
using System.IO;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// Replaces the script text in rom_root/UNION/SCRIPT.UNI and script2.uni
    /// with the translations of Text/script_ko.jsonl and, for the developer
    /// mode, unlocks the leftover debug menu (DebugMenuPatcher).
    /// </summary>
    internal static class ScriptPatcher
    {
        public static readonly string[] Archives = { "SCRIPT.UNI", "script2.uni" };

        private sealed class Line
        {
            public string Id;
            public string Hash;
            public string Korean;
        }

        public static void Apply(string unionDir, PatchAssets assets, TextEncoder encoder, bool unlockDebugMenu, PatchLog log)
        {
            // arc -> fid -> lines
            Dictionary<string, Dictionary<uint, List<Line>>> byArchive = new Dictionary<string, Dictionary<uint, List<Line>>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> row in JsonLines.Read(assets.ScriptPath))
            {
                string arc = JsonLines.Str(row, "arc");
                uint fid = (uint)JsonLines.Int(row, "fid");
                Dictionary<uint, List<Line>> files;
                if (!byArchive.TryGetValue(arc, out files))
                {
                    files = new Dictionary<uint, List<Line>>();
                    byArchive[arc] = files;
                }

                List<Line> lines;
                if (!files.TryGetValue(fid, out lines))
                {
                    lines = new List<Line>();
                    files[fid] = lines;
                }

                lines.Add(new Line { Id = JsonLines.Str(row, "id"), Hash = JsonLines.Str(row, "h"), Korean = JsonLines.Str(row, "ko") });
            }

            foreach (string arc in Archives)
            {
                string path = Path.Combine(unionDir, arc);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("스크립트 아카이브를 찾을 수 없습니다: " + arc, path);
                }

                Uni2Archive archive = Uni2Archive.Load(path);
                Dictionary<uint, List<Line>> files;
                byArchive.TryGetValue(arc, out files);
                int scripts = 0;
                int lineCount = 0;
                foreach (Uni2Archive.Entry entry in archive.Entries)
                {
                    List<Line> lines;
                    if (files == null || !files.TryGetValue(entry.Id, out lines))
                    {
                        continue;
                    }

                    if (!Bin.StartsWith(entry.Data, "STCM2B"))
                    {
                        throw new InvalidDataException(String.Format("{0} {1}: 스크립트가 아닙니다.", arc, entry.Id));
                    }

                    Stcm2Script script = new Stcm2Script(entry.Data);
                    Dictionary<string, Stcm2Script.DataObject> strings = script.Strings();
                    foreach (Line line in lines)
                    {
                        Stcm2Script.DataObject ob;
                        if (!strings.TryGetValue(line.Id, out ob) || Hash.Short(Stcm2Script.TextOf(ob)) != line.Hash)
                        {
                            throw new InvalidDataException(String.Format(
                                "{0} {1:00000} {2}: 원본 문장이 다릅니다. 지원하는 원본 ISO인지 확인해 주세요.", arc, entry.Id, line.Id));
                        }

                        Stcm2Script.SetText(ob, encoder.Encode(line.Korean));
                    }

                    entry.Data = script.Build();
                    scripts++;
                    lineCount += lines.Count;
                }

                log.Info(String.Format("{0}: 스크립트 {1}개, 문장 {2:N0}개", arc, scripts, lineCount));

                if (unlockDebugMenu)
                {
                    int unlocked = DebugMenuPatcher.Apply(archive);
                    if (unlocked == 0)
                    {
                        throw new InvalidDataException(arc + ": 개발자 메뉴 위치를 찾지 못했습니다.");
                    }

                    log.Info(String.Format("{0}: 개발자 메뉴 활성화 {1}개", arc, unlocked));
                }

                archive.Save(path);
            }
        }
    }
}
