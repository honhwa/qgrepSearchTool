using qgrepControls.Properties;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace qgrepControls.Classes
{
    /// <summary>
    /// 统一管理 qgrep 运行期数据（配置 *.cfg、索引缓存 *.qgd/*.qgf/*.qgc、settings.json）的落盘位置。
    ///
    /// 布局：
    ///   &lt;Root&gt;\&lt;解决方案完整路径（非法字符替换为 '_'）&gt;\   ← 每个 sln 一个独立目录
    ///   &lt;Root&gt;\_global\                                  ← 勾选「使用全局配置」时
    ///   &lt;Root&gt;\_standalone\                              ← 独立版（无解决方案概念）
    ///   &lt;Root&gt;\_crashes\                                 ← 崩溃报告
    ///
    /// Root 由设置项 Settings.ConfigRootPath 指定，留空则用 %APPDATA%\qgrepSearch。
    /// 目的：配置与缓存不再写进项目（解决方案）目录。
    /// </summary>
    public static class ConfigStorage
    {
        public const string GlobalFolderName = "_global";
        public const string StandaloneFolderName = "_standalone";
        public const string CrashesFolderName = "_crashes";

        /// <summary>兼容旧版本使用的漫游目录名。</summary>
        private const string LegacyFolderName = "qgrepSearch";

        /// <summary>旧版把配置放在解决方案目录下的子目录名（带点前缀）。</summary>
        public const string LegacySubFolderName = ".qgrep";

        /// <summary>单个目录名的最大长度，超出则截断并追加哈希。</summary>
        private const int MaxFolderNameLength = 120;

        /// <summary>未显式配置时使用的默认根目录。</summary>
        public static string GetDefaultRootDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyFolderName);
        }

        /// <summary>当前生效的根目录（用户自定义优先，支持 %ENV% 变量）。</summary>
        public static string GetRootDirectory()
        {
            string configured = "";
            try
            {
                configured = Settings.Default.ConfigRootPath;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(configured))
            {
                return GetDefaultRootDirectory();
            }

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return GetDefaultRootDirectory();
            }
        }

        /// <summary>
        /// 由解决方案完整路径生成目录名：":"、"\"、"/"、"*" 等不适合做目录名的字符统一替换为 '_'。
        /// </summary>
        public static string GetSolutionFolderName(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return "";
            }

            string fullPath = solutionPath.Trim();
            try
            {
                fullPath = Path.GetFullPath(fullPath);
            }
            catch { }

            string name = SanitizeFolderName(fullPath);

            // 路径很深时目录名可能超长，截断并追加稳定哈希保证唯一
            if (name.Length > MaxFolderNameLength)
            {
                string hash = ShortHash(fullPath);
                int keep = MaxFolderNameLength - hash.Length - 1;
                name = name.Substring(0, keep > 0 ? keep : 0) + "_" + hash;
            }

            return name;
        }

        /// <summary>替换所有不适合作为目录名的字符，并去掉首尾空白与结尾的点。</summary>
        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                // 显式列出关键字符，避免个别环境下 GetInvalidFileNameChars 集合不完整
                if (c == ':' || c == '\\' || c == '/' || c == '*' || c == '?' || c == '"' ||
                    c == '<' || c == '>' || c == '|' || c < ' ' || invalid.Contains(c))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(c);
                }
            }

            string result = builder.ToString().Trim().TrimEnd('.');
            return result.Length == 0 ? "_" : result;
        }

        /// <summary>FNV-1a 32 位短哈希（十六进制），用于超长目录名去重。</summary>
        private static string ShortHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
                return hash.ToString("x8");
            }
        }

        /// <summary>&lt;Root&gt;\&lt;解决方案目录名&gt;，无解决方案时返回空串。</summary>
        public static string GetSolutionDirectory(string solutionPath)
        {
            string name = GetSolutionFolderName(solutionPath);
            if (name.Length == 0)
            {
                return "";
            }

            return Path.Combine(GetRootDirectory(), name);
        }

        /// <summary>&lt;Root&gt;\_global。</summary>
        public static string GetGlobalDirectory()
        {
            return Path.Combine(GetRootDirectory(), GlobalFolderName);
        }

        /// <summary>&lt;Root&gt;\_standalone。</summary>
        public static string GetStandaloneDirectory()
        {
            return Path.Combine(GetRootDirectory(), StandaloneFolderName);
        }

        /// <summary>&lt;Root&gt;\_crashes。</summary>
        public static string GetCrashesDirectory()
        {
            return Path.Combine(GetRootDirectory(), CrashesFolderName);
        }

        /// <summary>
        /// 解析当前应使用的配置目录。
        /// 全局模式 → &lt;Root&gt;\_global；否则 → &lt;Root&gt;\&lt;sln 目录名&gt;（无解决方案返回空串）。
        /// </summary>
        public static string GetConfigDirectory(bool useGlobalPath, string solutionPath)
        {
            if (useGlobalPath)
            {
                return GetGlobalDirectory();
            }

            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return "";
            }

            return GetSolutionDirectory(solutionPath);
        }

        /// <summary>独立版专用：&lt;Root&gt;\_standalone。</summary>
        public static string GetStandaloneConfigDirectory()
        {
            return GetStandaloneDirectory();
        }

        /// <summary>确保目录存在；成功返回该路径，失败返回空串。</summary>
        public static string EnsureDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return "";
            }

            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return directory;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 把旧版存放在解决方案目录下 .qgrep 里的配置与索引迁移到新的集中目录。
        /// </summary>
        public static void MigrateLegacyConfig(string solutionDirectory, string targetDirectory)
        {
            if (string.IsNullOrWhiteSpace(solutionDirectory))
            {
                return;
            }

            MigrateDirectoryContents(Path.Combine(solutionDirectory.TrimEnd('\\', '/'), LegacySubFolderName), targetDirectory);
        }

        /// <summary>
        /// 把 legacyDirectory 下的文件搬到 targetDirectory：仅当目标目录还没有任何 .cfg 时才迁移，
        /// 避免覆盖用户当前配置；已存在的同名文件不覆盖。
        /// </summary>
        public static void MigrateDirectoryContents(string legacyDirectory, string targetDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(legacyDirectory) || string.IsNullOrWhiteSpace(targetDirectory))
                {
                    return;
                }

                if (!Directory.Exists(legacyDirectory) || !Directory.Exists(targetDirectory))
                {
                    return;
                }

                if (Directory.GetFiles(legacyDirectory, "*.cfg").Length == 0)
                {
                    return;
                }

                if (Directory.GetFiles(targetDirectory, "*.cfg").Length > 0)
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(legacyDirectory))
                {
                    string target = Path.Combine(targetDirectory, Path.GetFileName(file));
                    if (!File.Exists(target))
                    {
                        File.Copy(file, target, false);
                    }
                }
            }
            catch { }
        }
    }
}
