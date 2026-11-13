using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// The Assets folder next to the exe (built by tools/export_patcher_assets.py).
    /// It contains only translations, redrawn images and changed bytes; original
    /// game data is referenced by SHA1 prefixes only.
    /// </summary>
    internal sealed class PatchAssets
    {
        public string Root { get; private set; }
        public string Version { get; private set; }
        public string XexDecryptedHash { get; private set; }
        public string XexPatchedHash { get; private set; }

        public string ExisoPath { get { return Path.Combine(Root, "Tools", "exiso.exe"); } }
        public string CharmapPath { get { return Path.Combine(Root, "Text", "charmap.tsv"); } }
        public string ScriptPath { get { return Path.Combine(Root, "Text", "script_ko.jsonl"); } }
        public string TablePath { get { return Path.Combine(Root, "Text", "table_ko.jsonl"); } }
        public string DlcPath { get { return Path.Combine(Root, "Text", "dlc_ko.jsonl"); } }
        public string XexPatchPath { get { return Path.Combine(Root, "Exe", "xex_patch.bin"); } }
        public string IntroSkipPath { get { return Path.Combine(Root, "Exe", "intro_skip.jsonl"); } }
        public string ImagesDir { get { return Path.Combine(Root, "Images"); } }
        public string ImageListPath { get { return Path.Combine(ImagesDir, "images.jsonl"); } }

        public static PatchAssets Load()
        {
            string root = FindRoot();
            string manifestPath = Path.Combine(root, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("Assets 폴더를 찾을 수 없습니다. 프로그램 폴더의 Assets 폴더를 확인해 주세요.", manifestPath);
            }

            Dictionary<string, object> m = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath, Encoding.UTF8));

            PatchAssets a = new PatchAssets();
            a.Root = root;
            a.Version = JsonLines.Str(m, "version");
            a.XexDecryptedHash = JsonLines.Str(m, "xex_decrypted_h");
            a.XexPatchedHash = JsonLines.Str(m, "xex_patched_h");

            string[] required =
            {
                a.ExisoPath, a.CharmapPath, a.ScriptPath, a.TablePath, a.DlcPath,
                a.XexPatchPath, a.IntroSkipPath, a.ImageListPath,
            };
            foreach (string path in required)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("패치 데이터가 없습니다: " + path.Substring(root.Length + 1), path);
                }
            }

            return a;
        }

        private static string FindRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Assets");
                if (File.Exists(Path.Combine(candidate, "manifest.json")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
        }
    }
}
