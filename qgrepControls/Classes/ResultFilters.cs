using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace qgrepControls.Classes
{
    /// <summary>
    /// CK1-CK10 自定义复选框对应的筛选位，位序与 qgrepSearchWindow 里的控件顺序一致（bit0 = CK1）。
    /// </summary>
    public static class CustomFlag
    {
        /// <summary>CK1「含注释」：勾选时保留注释里的匹配；未勾选时按文件扩展名对应的注释语法隐藏注释里的匹配。</summary>
        public const int IncludeComments = 1 << 0;

        /// <summary>CK2「只看当前文件」：只显示当前编辑器活动文件里找到的匹配。</summary>
        public const int CurrentFileOnly = 1 << 1;

        /// <summary>CK3「排除DSN」：不显示 .designer.cs / .designer.vb 里找到的匹配。</summary>
        public const int ExcludeDesigner = 1 << 2;
    }

    /// <summary>路径比较工具：qgrep 输出的路径与 IDE 给出的路径可能在分隔符、大小写上不一致。</summary>
    public static class ResultPath
    {
        public static string NormalizeForCompare(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            string result = path.Trim().Replace('\\', '/');

            while (result.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                result = result.Replace("//", "/");
            }

            return result.TrimEnd('/').ToLowerInvariant();
        }

        public static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            int separator = path.LastIndexOfAny(new[] { '/', '\\' });
            return separator >= 0 ? path.Substring(separator + 1) : path;
        }

        /// <summary>判断两个路径是否指向同一个文件（允许一边是相对路径、另一边是完整路径）。</summary>
        public static bool IsSameFile(string left, string right)
        {
            string normalizedLeft = NormalizeForCompare(left);
            string normalizedRight = NormalizeForCompare(right);

            if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
            {
                return false;
            }

            if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
            {
                return true;
            }

            return normalizedLeft.EndsWith("/" + normalizedRight, StringComparison.Ordinal) ||
                normalizedRight.EndsWith("/" + normalizedLeft, StringComparison.Ordinal);
        }
    }

    /// <summary>VS 设计器自动生成的代码文件（CK3「排除DSN」使用）。</summary>
    public static class DesignerFiles
    {
        private static readonly string[] Suffixes = new[] { ".designer.cs", ".designer.vb" };

        public static bool IsDesignerFile(string path)
        {
            string fileName = ResultPath.GetFileName(path);

            if (fileName.Length == 0)
            {
                return false;
            }

            foreach (string suffix in Suffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>按扩展名归纳出来的注释语法。</summary>
    internal sealed class CommentSyntax
    {
        public static readonly CommentSyntax None = new CommentSyntax(new string[0], new string[0], null, null, "", false);

        public readonly string[] LineMarkers;
        public readonly string[] LineStartOnlyMarkers;
        public readonly string BlockBegin;
        public readonly string BlockEnd;
        /// <summary>该语言里开始 / 结束字符串字面量的字符，字符串内的注释标记不算注释。</summary>
        public readonly string StringQuotes;
        public readonly bool Escapes;

        public CommentSyntax(string[] lineMarkers, string[] lineStartOnlyMarkers, string blockBegin, string blockEnd, string stringQuotes, bool escapes)
        {
            LineMarkers = lineMarkers;
            LineStartOnlyMarkers = lineStartOnlyMarkers;
            BlockBegin = blockBegin;
            BlockEnd = blockEnd;
            StringQuotes = stringQuotes;
            Escapes = escapes;
        }

        public bool IsStringQuote(char value)
        {
            return StringQuotes.IndexOf(value) >= 0;
        }

        public bool IsEmpty
        {
            get { return LineMarkers.Length == 0 && LineStartOnlyMarkers.Length == 0 && BlockBegin == null; }
        }

        private static readonly string[] NoMarkers = new string[0];
        private const string CQuotes = "\"'";

        private static readonly CommentSyntax Cpp = new CommentSyntax(new[] { "//" }, NoMarkers, "/*", "*/", CQuotes, true);
        private static readonly CommentSyntax Dash = new CommentSyntax(new[] { "--" }, NoMarkers, "/*", "*/", CQuotes, true);
        private static readonly CommentSyntax Xml = new CommentSyntax(NoMarkers, NoMarkers, "<!--", "-->", "", false);
        // VB 的 ' 是行注释标记，不是字符串定界符；VB 里只有 " 才是字符串
        private static readonly CommentSyntax Vb = new CommentSyntax(new[] { "'" }, new[] { "REM" }, null, null, "\"", false);
        private static readonly CommentSyntax Hash = new CommentSyntax(new[] { "#" }, NoMarkers, null, null, CQuotes, true);
        private static readonly CommentSyntax Asm = new CommentSyntax(new[] { ";" }, NoMarkers, null, null, CQuotes, false);
        private static readonly CommentSyntax Batch = new CommentSyntax(NoMarkers, new[] { "REM" }, null, null, "\"", false);

        private static readonly Dictionary<string, CommentSyntax> SyntaxByExtension = BuildSyntaxByExtension();

        private static Dictionary<string, CommentSyntax> BuildSyntaxByExtension()
        {
            Dictionary<string, CommentSyntax> map = new Dictionary<string, CommentSyntax>(StringComparer.OrdinalIgnoreCase);

            // C 系：// 与 /* */
            foreach (string extension in new[]
            {
                ".c", ".cc", ".cpp", ".cxx", ".c++", ".h", ".hh", ".hpp", ".hxx", ".inl", ".ipp", ".ixx",
                ".m", ".mm", ".cu", ".cuh", ".ccp", ".tli", ".tlh", ".idl", ".acf", ".rc",
                ".cs", ".java", ".js", ".jsx", ".mjs", ".ts", ".tsx", ".vue",
                ".go", ".rs", ".swift", ".kt", ".kts", ".scala", ".dart", ".groovy",
                ".php", ".php3", ".php4", ".php5", ".phtml",
                ".glsl", ".hlsl", ".cg", ".fx", ".shader",
                ".css", ".less", ".scss", ".sass", ".styl"
            })
            {
                map[extension] = Cpp;
            }

            // 双横线：SQL / Lua / Haskell / Ada
            foreach (string extension in new[] { ".sql", ".lua", ".hs", ".lhs", ".adb", ".ads", ".ada", ".pgsql" })
            {
                map[extension] = Dash;
            }

            // 标记语言：<!-- -->
            foreach (string extension in new[]
            {
                ".xml", ".xsd", ".xsl", ".xslt", ".dtd", ".resx", ".resw", ".xaml", ".svg", ".plist",
                ".htm", ".html", ".xhtml", ".shtml", ".cshtml", ".vbhtml", ".aspx", ".asp", ".ascx",
                ".master", ".config", ".csproj", ".vbproj", ".vcxproj", ".props", ".targets", ".manifest", ".wxs"
            })
            {
                map[extension] = Xml;
            }

            // VB 系：' 与 REM
            foreach (string extension in new[] { ".vb", ".vbs", ".bas", ".cls", ".frm", ".ctl" })
            {
                map[extension] = Vb;
            }

            // 井号：脚本 / 配置
            foreach (string extension in new[]
            {
                ".py", ".pyw", ".ps1", ".psm1", ".psd1", ".sh", ".bash", ".zsh", ".ksh", ".csh",
                ".pl", ".pm", ".rb", ".rake", ".yml", ".yaml", ".r", ".toml", ".tcl", ".cmake", ".mk", ".nsi"
            })
            {
                map[extension] = Hash;
            }

            // 分号：汇编
            map[".asm"] = Asm;
            map[".s"] = Asm;

            // 批处理：REM
            map[".bat"] = Batch;
            map[".cmd"] = Batch;

            return map;
        }

        /// <summary>根据路径判断注释语法；返回 null 表示未知类型，此时不做注释过滤。</summary>
        public static CommentSyntax FromPath(string path)
        {
            string fileName = ResultPath.GetFileName(path);

            int dot = fileName.LastIndexOf('.');
            if (dot < 0 || dot == fileName.Length - 1)
            {
                return null;
            }

            CommentSyntax syntax;
            return SyntaxByExtension.TryGetValue(fileName.Substring(dot), out syntax) ? syntax : null;
        }
    }

    /// <summary>判断某一行里的某个位置是否落在注释中，同时给出该行结束时的块注释状态。</summary>
    internal static class CommentAnalyzer
    {
        public static bool IsInComment(string line, int matchIndex, bool inBlockAtLineStart, CommentSyntax syntax, out bool inBlockAtLineEnd)
        {
            inBlockAtLineEnd = inBlockAtLineStart;

            if (line == null || syntax == null || syntax.IsEmpty)
            {
                inBlockAtLineEnd = false;
                return false;
            }

            bool inComment = false;
            bool open = inBlockAtLineStart;
            int length = line.Length;
            int index = 0;

            // 行首仍处于块注释中：先把跨行的块注释消费掉
            if (open)
            {
                int end = line.IndexOf(syntax.BlockEnd, 0, StringComparison.Ordinal);
                int commentEnd = end < 0 ? length : end + syntax.BlockEnd.Length;

                if (matchIndex >= 0 && matchIndex < commentEnd)
                {
                    inComment = true;
                }

                if (end < 0)
                {
                    inBlockAtLineEnd = true;
                    return inComment;
                }

                open = false;
                index = commentEnd;
            }

            while (index < length)
            {
                char current = line[index];

                if (syntax.IsStringQuote(current))
                {
                    index = SkipString(line, index, syntax.Escapes);
                    continue;
                }

                if (syntax.BlockBegin != null && MatchAt(line, index, syntax.BlockBegin))
                {
                    int end = line.IndexOf(syntax.BlockEnd, index + syntax.BlockBegin.Length, StringComparison.Ordinal);
                    int commentEnd = end < 0 ? length : end + syntax.BlockEnd.Length;

                    if (matchIndex >= index && matchIndex < commentEnd)
                    {
                        inComment = true;
                    }

                    if (end < 0)
                    {
                        open = true;
                        break;
                    }

                    index = commentEnd;
                    continue;
                }

                if (HasLineMarker(line, index, syntax))
                {
                    // 行注释之后本行不再有代码
                    if (matchIndex >= index)
                    {
                        inComment = true;
                    }

                    break;
                }

                index++;
            }

            inBlockAtLineEnd = open;
            return inComment;
        }

        private static int SkipString(string line, int index, bool escapes)
        {
            char quote = line[index];
            index++;

            while (index < line.Length)
            {
                char current = line[index];

                if (escapes && current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current == quote)
                {
                    return index + 1;
                }

                index++;
            }

            return index;
        }

        private static bool HasLineMarker(string line, int index, CommentSyntax syntax)
        {
            return MatchesMarker(line, index, syntax.LineMarkers, false) ||
                MatchesMarker(line, index, syntax.LineStartOnlyMarkers, true);
        }

        private static bool MatchesMarker(string line, int index, string[] markers, bool lineStartOnly)
        {
            if (markers == null || markers.Length == 0)
            {
                return false;
            }

            if (lineStartOnly)
            {
                for (int i = 0; i < index; i++)
                {
                    if (line[i] != ' ' && line[i] != '\t')
                    {
                        return false;
                    }
                }
            }

            foreach (string marker in markers)
            {
                if (!MatchAt(line, index, marker))
                {
                    continue;
                }

                // REM 之类的关键字标记后面不能紧跟标识符字符（REMOVE 不是注释）
                if (lineStartOnly)
                {
                    int after = index + marker.Length;
                    if (after < line.Length && (char.IsLetterOrDigit(line[after]) || line[after] == '_'))
                    {
                        continue;
                    }
                }

                return true;
            }

            return false;
        }

        private static bool MatchAt(string line, int index, string value)
        {
            if (string.IsNullOrEmpty(value) || index + value.Length > line.Length)
            {
                return false;
            }

            return string.Compare(line, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }

    /// <summary>
    /// 按文件增量扫描的注释判断器。qgrep 对同一个文件的结果按行号递增输出，所以只需顺序读一遍文件；
    /// 行号回退或换文件时重新定位。文件句柄用 FileShare.ReadWrite 打开，不会占用用户的文件。
    /// </summary>
    internal sealed class CommentFileScanner : IDisposable
    {
        private string currentPath = null;
        private CommentSyntax currentSyntax = null;
        private StreamReader reader = null;
        private string currentLine = null;
        private int currentLineNumber = 0;
        private bool inBlockAtCurrentLineStart = false;

        /// <summary>matchIndex 为匹配在本行（从 0 开始）的偏移，判断它是否在注释里。</summary>
        public bool IsInComment(string path, int lineNumber, int matchIndex)
        {
            if (!EnsureLine(path, lineNumber))
            {
                return false;
            }

            bool inBlockAtLineEnd;
            return CommentAnalyzer.IsInComment(currentLine, matchIndex, inBlockAtCurrentLineStart, currentSyntax, out inBlockAtLineEnd);
        }

        private bool EnsureLine(string path, int lineNumber)
        {
            if (reader != null && (lineNumber < currentLineNumber || !ResultPath.IsSameFile(path, currentPath)))
            {
                Reset();
            }

            if (reader == null)
            {
                CommentSyntax syntax = CommentSyntax.FromPath(path);

                if (syntax == null || syntax.IsEmpty)
                {
                    return false;
                }

                try
                {
                    FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    reader = new StreamReader(stream, Encoding.UTF8, true);
                }
                catch
                {
                    Reset();
                    return false;
                }

                currentPath = path;
                currentSyntax = syntax;
                currentLine = null;
                currentLineNumber = 0;
                inBlockAtCurrentLineStart = false;
            }

            while (currentLineNumber < lineNumber)
            {
                string next;

                try
                {
                    next = reader.ReadLine();
                }
                catch
                {
                    Reset();
                    return false;
                }

                if (next == null)
                {
                    // 文件比结果行号短，索引已过期，不做判断
                    Reset();
                    return false;
                }

                if (currentLine != null)
                {
                    bool inBlockAtLineEnd;
                    CommentAnalyzer.IsInComment(currentLine, -1, inBlockAtCurrentLineStart, currentSyntax, out inBlockAtLineEnd);
                    inBlockAtCurrentLineStart = inBlockAtLineEnd;
                }

                currentLine = next;
                currentLineNumber++;
            }

            return currentLine != null;
        }

        private void Reset()
        {
            if (reader != null)
            {
                try
                {
                    reader.Dispose();
                }
                catch
                {
                }
            }

            reader = null;
            currentPath = null;
            currentSyntax = null;
            currentLine = null;
            currentLineNumber = 0;
            inBlockAtCurrentLineStart = false;
        }

        public void Dispose()
        {
            Reset();
        }
    }

    /// <summary>CK1「含注释」未勾选时用于隐藏注释里的匹配。每次搜索一个实例，搜索结束后释放。</summary>
    public sealed class CommentFilter
    {
        private CommentFileScanner scanner = null;

        /// <summary>未知扩展名 / 无注释语法的文件一律保留，避免误杀。</summary>
        public bool IsMatchInComment(string file, string lineNumber, int matchIndex)
        {
            if (string.IsNullOrEmpty(file) || matchIndex < 0)
            {
                return false;
            }

            int line;
            if (!int.TryParse(lineNumber, out line) || line <= 0)
            {
                return false;
            }

            if (scanner == null)
            {
                scanner = new CommentFileScanner();
            }

            try
            {
                return scanner.IsInComment(file, line, matchIndex);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>释放扫描器持有的文件句柄。</summary>
        public void Reset()
        {
            if (scanner != null)
            {
                scanner.Dispose();
                scanner = null;
            }
        }
    }
}
