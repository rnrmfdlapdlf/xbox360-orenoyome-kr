using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OreNoYomeKoreanPatcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                return RunCommandLine(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        /// <summary>
        /// Test mode without the window:
        ///   OreNoYomeKoreanPatcher.exe --iso &lt;iso&gt; [--dlc &lt;folder&gt;] [--skip-intro] [--debug] [--keep-work] [--log &lt;file&gt;]
        /// </summary>
        private static int RunCommandLine(string[] args)
        {
            PatchOptions options = new PatchOptions();
            string logPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--iso": options.IsoPath = args[++i]; break;
                    case "--dlc": options.DlcFolder = args[++i]; break;
                    case "--skip-intro": options.IntroSkip = true; break;
                    case "--debug": options.DeveloperMode = true; break;
                    case "--keep-work": options.KeepWork = true; break;
                    case "--log": logPath = args[++i]; break;
                    default:
                        Console.Error.WriteLine("unknown argument: " + args[i]);
                        return 2;
                }
            }

            StreamWriter writer = logPath != null ? new StreamWriter(logPath, false, new UTF8Encoding(false)) : null;
            try
            {
                PatchLog log = new PatchLog(delegate(int percent, string message, bool append)
                {
                    string line = (percent >= 0 ? "[" + percent.ToString().PadLeft(3) + "%] " : "       ") + message;
                    Console.WriteLine(line);
                    if (writer != null)
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                });
                PatchJob.Run(options, log);
                return 0;
            }
            catch (Exception ex)
            {
                string line = "오류: " + ex;
                Console.Error.WriteLine(line);
                if (writer != null)
                {
                    writer.WriteLine(line);
                }

                return 1;
            }
            finally
            {
                if (writer != null)
                {
                    writer.Dispose();
                }
            }
        }
    }
}
