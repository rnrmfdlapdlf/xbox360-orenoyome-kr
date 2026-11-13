using System;
using System.Collections.Generic;
using System.IO;

namespace OreNoYomeKoreanPatcher
{
    internal sealed class PatchOptions
    {
        public string IsoPath;
        public string DlcFolder;
        public bool IntroSkip;
        public bool DeveloperMode;

        /// <summary>Keep the extracted work folder (test mode only).</summary>
        public bool KeepWork;
    }

    /// <summary>
    /// The whole patch: ISO (extract -> default.xex, scripts, tables, images ->
    /// rebuild) and DLC folder. The original files are never modified.
    /// </summary>
    internal static class PatchJob
    {
        private const long RequiredFreeBytes = 5L * 1024 * 1024 * 1024;

        public static List<string> Run(PatchOptions options, PatchLog log)
        {
            List<string> outputs = new List<string>();
            log.Stage(2, "패치 데이터 확인 중...");
            PatchAssets assets = PatchAssets.Load();
            TextEncoder encoder = new TextEncoder(assets.CharmapPath);
            log.Info("패치 버전 v" + assets.Version);

            if (!String.IsNullOrEmpty(options.IsoPath))
            {
                outputs.Add(PatchIso(options, assets, encoder, log));
            }

            if (!String.IsNullOrEmpty(options.DlcFolder))
            {
                log.Stage(96, "DLC 패치 중...");
                outputs.Add(DlcPatcher.Apply(options.DlcFolder, assets, encoder, log));
            }

            log.Stage(100, "완료");
            foreach (string path in outputs)
            {
                log.Info("결과: " + path);
            }

            return outputs;
        }

        private static string PatchIso(PatchOptions options, PatchAssets assets, TextEncoder encoder, PatchLog log)
        {
            string iso = Path.GetFullPath(options.IsoPath);
            if (!File.Exists(iso))
            {
                throw new FileNotFoundException("ISO 파일이 없습니다.", iso);
            }

            string isoDir = Path.GetDirectoryName(iso);
            string baseName = Path.GetFileNameWithoutExtension(iso);
            CheckFreeSpace(isoDir);

            string workName = baseName + "_patcher_work";
            string workRoot = PathUtil.AvailableDirectory(Path.Combine(isoDir, workName));
            string suffix = options.DeveloperMode ? "_repacked_debug" : "_repacked";
            string outputIso = PathUtil.AvailableFile(Path.Combine(isoDir, baseName + suffix + ".iso"));
            Directory.CreateDirectory(workRoot);
            bool success = false;
            try
            {
                log.Stage(5, "원본 ISO 해제 중... (몇 분 걸릴 수 있습니다)");
                ExternalToolRunner.Run(assets.ExisoPath,
                    "-x -d " + ExternalToolRunner.Quote(workRoot) + " " + ExternalToolRunner.Quote(iso),
                    workRoot, "exiso");
                string gameRoot = FindGameRoot(workRoot);
                string union = Path.Combine(gameRoot, "rom_root", "UNION");
                string tables = Path.Combine(gameRoot, "rom_root", "Table");

                log.Stage(35, "default.xex 패치 중...");
                XexPatcher.Apply(Path.Combine(gameRoot, "default.xex"), assets, options.IntroSkip, log);

                log.Stage(45, "스크립트 패치 중...");
                ScriptPatcher.Apply(union, assets, encoder, options.DeveloperMode, log);

                log.Stage(60, "미니게임/퀴즈 테이블 패치 중...");
                TablePatcher.Apply(tables, assets, encoder, log);

                log.Stage(65, "이미지 패치 중...");
                ImagePatcher.Apply(union, assets, log);

                log.Stage(70, "ISO 재생성 중... (몇 분 걸릴 수 있습니다)");
                ExternalToolRunner.Run(assets.ExisoPath,
                    "-c " + ExternalToolRunner.Quote(gameRoot) + " " + ExternalToolRunner.Quote(outputIso),
                    workRoot, "exiso");
                FileInfo info = new FileInfo(outputIso);
                if (!info.Exists || info.Length < 100L * 1024 * 1024)
                {
                    throw new InvalidOperationException("ISO 재생성에 실패했습니다.");
                }

                success = true;
            }
            finally
            {
                if (!success && File.Exists(outputIso))
                {
                    File.Delete(outputIso);
                }

                if (!options.KeepWork)
                {
                    log.Stage(95, "임시 작업 폴더 정리 중...");
                    PathUtil.DeleteWorkDirectory(workRoot, isoDir, workName);
                }
            }

            return outputIso;
        }

        /// <summary>exiso puts the files either directly in the folder or in a sub folder named after the ISO.</summary>
        private static string FindGameRoot(string workRoot)
        {
            if (File.Exists(Path.Combine(workRoot, "default.xex")))
            {
                return workRoot;
            }

            foreach (string dir in Directory.GetDirectories(workRoot))
            {
                if (File.Exists(Path.Combine(dir, "default.xex")))
                {
                    return dir;
                }
            }

            throw new InvalidDataException("ISO에서 default.xex를 찾지 못했습니다. 이 게임의 ISO인지 확인해 주세요.");
        }

        private static void CheckFreeSpace(string dir)
        {
            string root = Path.GetPathRoot(dir);
            if (String.IsNullOrEmpty(root) || root.StartsWith(@"\\"))
            {
                return;   // network path: cannot check
            }

            DriveInfo drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < RequiredFreeBytes)
            {
                throw new IOException(String.Format("ISO가 있는 드라이브({0})의 여유 공간이 부족합니다. 최소 5GB가 필요합니다.", root));
            }
        }
    }
}
