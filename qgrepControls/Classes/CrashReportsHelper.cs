using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace qgrepControls.Classes
{
    public class CrashReportsHelper
    {
        public static string LastReport = "";
        public static string LastReportPath = "";
        public static readonly object padlock = new object();

        public static void DebugToRoamingLog(string message)
        {
            lock(padlock)
            {
                try
                {
                    string folderPath = GetPrimaryCrashReportDirectory();
                    if (folderPath.Length == 0)
                    {
                        return;
                    }

                    string filePath = System.IO.Path.Combine(folderPath, "LogErrors.txt");
                    using (StreamWriter writer = new StreamWriter(filePath, true))
                    {
                        writer.WriteLine(message);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 崩溃报告存放目录（优先）与旧版本目录（用于兼容读取）。
        /// 主目录创建失败时退回旧位置，保证崩溃报告一定写得下去。
        /// </summary>
        private static List<string> GetCrashReportDirectories()
        {
            List<string> directories = new List<string>();
            string legacyDirectory = "";

            try
            {
                legacyDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "qgrepSearch");
            }
            catch { }

            try
            {
                string directory = ConfigStorage.EnsureDirectory(ConfigStorage.GetCrashesDirectory());
                if (directory.Length > 0)
                {
                    directories.Add(directory);
                }
            }
            catch { }

            try
            {
                if (legacyDirectory.Length > 0 && !directories.Contains(legacyDirectory))
                {
                    // 主目录不可用时创建旧目录兜底；可用时仅在已存在的情况下纳入读取范围
                    if (directories.Count == 0 || Directory.Exists(legacyDirectory))
                    {
                        Directory.CreateDirectory(legacyDirectory);
                        directories.Add(legacyDirectory);
                    }
                }
            }
            catch { }

            return directories;
        }

        /// <summary>取第一个可用的崩溃报告目录；没有则返回空串。</summary>
        private static string GetPrimaryCrashReportDirectory()
        {
            List<string> directories = GetCrashReportDirectories();
            return directories.Count > 0 ? directories[0] : "";
        }

        public static void WriteCrashReport(Exception ex)
        {
            //System.Diagnostics.Debugger.Launch();

            string report = PrintExceptionDetails(ex);
            if(report.Length > 0 && report.Contains("qgrep"))
            {
                string folderPath = GetPrimaryCrashReportDirectory();
                if(folderPath.Length == 0)
                {
                    return;
                }

                string fileName = "CrashReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                string fullPath = Path.Combine(folderPath, fileName);

                try
                {
                    File.WriteAllText(fullPath, report);
                }
                catch { }
            }
        }
        private static string PrintExceptionDetails(Exception ex)
        {
            string report = "";

            while (ex != null)
            {
                report += "An unhandled exception occurred: " + ex.Message + "\n";
                report += "Stack Trace: " + ex.StackTrace + "\n";

                ex = ex.InnerException;
            }

            return report;
        }

        public static void ReadLatestCrashReport()
        {
            LastReport = "";

            try
            {
                foreach (string folderPath in GetCrashReportDirectories())
                {
                    foreach (FileInfo file in new DirectoryInfo(folderPath).GetFiles("CrashReport_*.txt"))
                    {
                        LastReportPath = file.FullName;
                        LastReport = File.ReadAllText(file.FullName);
                    }

                    string errorsLogPath = Path.Combine(folderPath, "LogErrors.txt");
                    if (File.Exists(errorsLogPath))
                    {
                        File.Delete(errorsLogPath);
                    }
                }
            }
            catch { }
        }

        public static void SendCrashReport()
        {
            string formId = "1FAIpQLScwDOYfKjWXdMBDJcEVN6CaJd_Z7L6l7tlXVWkxpys5GoIUlg";
            string fieldId = "entry.1229215598";
            string encodedCrashReport = WebUtility.UrlEncode(LastReport);

            string url = $"https://docs.google.com/forms/d/e/{formId}/viewform?usp=pp_url&{fieldId}={encodedCrashReport}";

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while opening the URL: " + ex.Message);
            }
        }

        public static void MarkReportAsRead()
        {
            try
            {
                string reportDirectory = Path.GetDirectoryName(LastReportPath);
                string reportFileName = Path.GetFileName(LastReportPath);

                string newReportPath = Path.Combine(reportDirectory, "_" + reportFileName);

                File.Move(LastReportPath, newReportPath);
            }
            catch { }
        }
    }
}
