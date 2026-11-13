using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OreNoYomeKoreanPatcher
{
    internal static class PathUtil
    {
        public static string AvailableDirectory(string desired)
        {
            if (!Directory.Exists(desired) && !File.Exists(desired))
            {
                return desired;
            }

            for (int i = 1; i < 1000; i++)
            {
                string candidate = desired + "_" + i;
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("사용 가능한 폴더 이름을 찾을 수 없습니다: " + desired);
        }

        public static string AvailableFile(string desired)
        {
            if (!File.Exists(desired) && !Directory.Exists(desired))
            {
                return desired;
            }

            string dir = Path.GetDirectoryName(desired);
            string name = Path.GetFileNameWithoutExtension(desired);
            string ext = Path.GetExtension(desired);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, name + "_" + i + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("사용 가능한 파일 이름을 찾을 수 없습니다: " + desired);
        }

        /// <summary>
        /// Deletes a temporary work folder created by the patcher. Refuses
        /// anything that is not a direct child of <paramref name="parent"/>
        /// with the expected name, or that contains links.
        /// </summary>
        public static void DeleteWorkDirectory(string workDir, string parent, string expectedName)
        {
            DirectoryInfo dir = new DirectoryInfo(Path.GetFullPath(workDir));
            if (!dir.Exists)
            {
                return;
            }

            string parentFull = new DirectoryInfo(Path.GetFullPath(parent)).FullName.TrimEnd('\\');
            if (dir.Parent == null || !String.Equals(dir.Parent.FullName.TrimEnd('\\'), parentFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("임시 작업 폴더 위치가 예상과 다릅니다: " + dir.FullName);
            }

            if (!dir.Name.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("임시 작업 폴더 이름이 예상과 다릅니다: " + dir.FullName);
            }

            EnsureNoReparsePoints(dir.FullName);
            Directory.Delete(dir.FullName, true);
        }

        private static void EnsureNoReparsePoints(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("임시 작업 폴더에 링크가 있어 안전하게 삭제할 수 없습니다: " + path);
            }

            foreach (string entry in Directory.GetFileSystemEntries(path))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("임시 작업 폴더에 링크가 있어 안전하게 삭제할 수 없습니다: " + entry);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    EnsureNoReparsePoints(entry);
                }
            }
        }
    }

    internal static class ExternalToolRunner
    {
        public static string Run(string fileName, string arguments, string workingDirectory, string displayName)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.WorkingDirectory = workingDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            StringBuilder output = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) { lock (output) { output.AppendLine(e.Data); } } };
                process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) { lock (output) { output.AppendLine(e.Data); } } };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string message = output.ToString().Trim();
                    if (message.Length > 600)
                    {
                        message = message.Substring(message.Length - 600);
                    }

                    if (message.Length == 0)
                    {
                        message = "exit code " + process.ExitCode;
                    }

                    throw new InvalidOperationException(displayName + " 실행 실패: " + message);
                }
            }

            return output.ToString();
        }

        public static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
