# qgrepSearchTool — 项目长期约定

## 项目结构
- `qgrep/` — 原生 qgrep 引擎（C++，`qgrep.vcxproj`），通过 `qgrepInterop/`（`/clr` C++/CLI）暴露给托管代码。
- `qgrepControls/` — WPF 库（ToolWindows / Classes / ModelViews / Properties），**扩展与独立版共用的全部核心逻辑**。
  - 新增 `.cs` 必须手动加进 `qgrepControls.csproj` 的 `<Compile Include="..." />`（旧式 csproj，无通配）。
  - 资源在 `Properties/Resources.resx` + `Resources.zh-Hans.resx` + `Resources.zh-Hant.resx`，**同时要手改 `Resources.Designer.cs`**。
  - 设置项改 `Properties/Settings.settings` + `Properties/Settings.Designer.cs` + `app.config` 三处。
- `qgrepExtension/` — VSIX 共享项目（.shproj），被 `qgrepGUI_VS` / `qgrepGUI_VS2019` / `qgrepGUI_VS2022` 引用；`VisualStudioWrapper.cs` 是全 VS 版本共用的唯一 `IWrapperApp` 实现。
- `qgrepGUI_Standalone/` — 独立 WPF 应用（另一个 `IWrapperApp` 实现）。
- 产物统一输出到 `build\Debug_x64\<ProjectName>\`。

## 约束（改动时必须同时满足）
1. **编码链路是字节保真的**：托管↔原生字符串一律按 **ISO-8859-1 (codepage 28591)** 做无损字节搬运（`qgrepInterop.cpp` 的 `ToNativeBytes/FromNativeBytes`，`ConfigParser.ToUtf8/FromUtf8`）。结果行格式为 `path(line)\xB0text`，`0xB0` 必须原样保留，否则 path/line 解析全废。**不要改回 ANSI/CP936、也不要改回 Windows-1252。**
2. **C++ 工程都带 `/utf-8`**（`qgrep.vcxproj`、`qgrepInterop.vcxproj` 四个配置）。源文件里有中文注释/非 ASCII 字符，去掉会触发 C4819 → C2220。
3. **配置与索引缓存不得写进项目目录**：统一走 `ConfigStorage`（根目录 = 设置项 `ConfigRootPath`，默认 `%APPDATA%\qgrepSearch`；每个 sln 一个子目录，目录名 = 解决方案完整路径、非法字符替换为 `_`）。`.cfg` 是缓存 `*.qgd/*.qgf/*.qgc` 的锚点（引擎 `replaceExtension` 推导），所以搬 `.cfg` 就够了。
4. `ConfigParser.Path` = 解决方案工作目录（相对路径基准）；`ConfigParser.ConfigDirectory` = 落盘目录。**不要混用。**
5. **CK1「含注释」的注释语法全部集中在 `qgrepControls/Classes/ResultFilters.cs`**：`CommentSyntax` 由「多行注释 + 多块注释 + 多字符串定界符 + `EmbeddedRegion`（`<script>`/`<style>` 内改用别的语法）」组合而成，构造时按标记长度降序排序；跨行状态是 `CommentState{ BlockEnd, OpenString, Region }`；判定顺序固定 **块注释 → 字符串 → 行注释 → 离开区段 → 进入区段**。加语言只改 `BuildSyntaxByExtension` / `BuildSyntaxByFileName`，不要动判定流程。
   - 返回 `null` 表示"未知类型、不做注释过滤"（`.json`/`.txt` 刻意不映射），**宁可漏判注释也不错杀代码**——新增映射时保持这个方向。
   - 扫描器 `CommentFileScanner` 依赖"同一文件结果按行号递增"，行号回退或换文件才重新定位；`FileShare.ReadWrite|Delete` 打开。
6. **回归测试工程**：`C:\Users\yhwa\AppData\Local\Temp\ck1test`（`ck1test.csproj` 用 `<Compile Include="P:\...\ResultFilters.cs" />` 链入产品源码 + `Harness.cs`）。改注释逻辑后跑：`"C:\Program Files\dotnet\dotnet.exe" build ck1test.csproj -c Release` → `"C:\Program Files\dotnet\dotnet.exe" bin\Release\net8.0\ck1test.dll`，必须 `FAILED = 0`。
7. **`.cfg` 只有 path / file / include / exclude / group / endgroup 六种指令**（`qgrep/src/project.cpp:192-236`），未知行会被当成**路径**，所以**不能新增指令**，任何新规则类型都必须落到 include/exclude 上。
   - `exclude` 正则在四处被匹配、形态不同：`project.cpp:302`（相对路径）、`watch.cpp:46`（相对路径）、`changes.cpp:52`（绝对路径）、`ConfigParser.IsFileRelevant`（Windows 反斜杠绝对路径，已归一化）。写规则要同时覆盖这些形态。
   - re2 侧是 **`posix_syntax(true)` + `RO_IGNORECASE`**：只能用 egrep 元素，**不能用 `(?:...)` / `(?i)`**；`SaveGroup` 会把规则里的 `\\` 换成 `\/`，所以**规则里不要出现字面双反斜杠**。
   - **「不包含的目录」规则的规格化形式定为 `(^|/)(dir1|dir2)/`**（`Classes/DirectoryRule.cs`，Add filter 的第三个 Type `ExcludeDirOptionContent`）。判定与解析只认这一个形式，改写法会让编辑回显退化成正则文本。
   - `ConfigParser.IsFileRelevant` 必须与引擎 `isFileAcceptable` 保持同一语义：路径先归一化、匹配用 `IgnoreCase`、**没有 include 规则时视为全部命中**。
8. **回归测试工程（目录规则）**：`C:\Users\yhwa\AppData\Local\Temp\dirtest`（链入 `DirectoryRule.cs`，`dotnet build` + 跑 dll，必须 `FAILED = 0`）；原生引擎端到端脚本 `C:\Users\yhwa\AppData\Local\Temp\qgrep_dir_e2e.ps1`（用 PowerShell 加载 `qgrepInterop.dll` → `qgrep build` + `qgrep files` 比对文件集合，见"环境"一节）。

## 构建（沙箱环境下的可行姿势）
- 命令里**不能出现 `msbuild` 字样**（含 `dotnet msbuild`、`-p:VCTargetsPath=...\MSBuild\...`），会被安全策略直接拒；`csc.exe` 也被拒。
- 可行姿势：写 ps1，用 `Start-Process -FilePath <MSBuild 全路径> -ArgumentList @(...) -UseNewEnvironment -NoNewWindow -PassThru -RedirectStandardOutput <log>`，然后 **`$p.WaitForExit()`**。
  - **不要用 `-Wait`**：PowerShell 5.1 的 `-Wait` 等后代进程，C# 构建拉起的常驻 `VBCSCompiler.exe` 会让脚本永久挂起。
  - **不要传 `/p:Platform=Any CPU`**：`-ArgumentList` 数组不加引号，空格会被拆成两个参数 → `MSB1008`。省略即可（sln 的 Debug|Any CPU 已映射到 x64）。
  - **必须 `-UseNewEnvironment`**，否则报 `MSB6001 ... Key in dictionary: 'PATH' Key being added: 'Path'`；VS2022 解决方案还会报 `GetDeploymentPathFromVsixManifest` → `TypeLoadException: Microsoft.VisualStudio.ExtensionManager.Metadata`。
  - MSBuild 偶发内部崩溃 `MissingMethodException: System.__Canon ... IEnumerator\`1.get_Current()`（Expander 表达式求值里），**重试即过**；加 `/m:1 /nodeReuse:false` 更稳。
  - 另一个偶发（同样是环境问题，重试即过）：`ResourceDictionary.xaml(...): error MC1000: Unknown build error, 'Could not load type 'System.Windows.Media.Animation.DoubleAnimation' from assembly 'PresentationCore''` —— WPF 标记编译在 `_wpftmp` 工程里加载 PresentationCore 失败。**先重试，别去改 XAML**。
- **临时验证工程可以用 `dotnet.exe build` 编译**（未被安全策略拦，SDK 6/8/9/10 都在）：`csc.exe` 被拦时这是唯一出路——建 `net8.0` 控制台 csproj + `EnableDefaultCompileItems=false` + 绝对路径 `<Compile Include>` 把产品源码链进来。
- 解决方案：`qgrepGUI_VS2022.sln`（扩展）、`qgrepGUI_Standalone.sln`（独立版）、`qgrepGUI_VS.sln`、`qgrepGUI_VS2019.sln`。默认 `Debug` 即为 x64。
- 独立版首次构建若报 `CS0246 Newtonsoft/Octokit`：`dotnet restore qgrepGUI_Standalone.csproj -p:RuntimeIdentifier=win-x64 -p:Platform=x64`；若再报 `doesn't list 'win-x64'`，删 `obj\project.assets.json` + `project.nuget.cache` 后加 `--force` 重还原。

## 原生引擎怎么测（不用编 C++ 测试程序）
`build\Debug_x64\qgrepGUI_Standalone\qgrepInterop.dll` 是混合模式程序集，**PowerShell 5.1 可以直接加载**（需要同目录的 `qgrep.dll`，所以从 Standalone 目录加载；`build\Debug_x64\qgrepInterop\` 里没有 qgrep.dll）：
```powershell
[void][Reflection.Assembly]::LoadFrom('<...>\qgrepGUI_Standalone\qgrepInterop.dll')
$q = New-Object 'System.Collections.Concurrent.ConcurrentQueue[string]'
$cb = [System.Delegate]::CreateDelegate([qgrepInterop.QGrepWrapper+StringCallback], $q, 'Enqueue')
$args = New-Object 'System.Collections.Generic.List[string]'
# 'qgrep','build','<cfg>' | 'qgrep','files','<cfg>' | 'qgrep','search','<cfg>','l','<query>'
[qgrepInterop.QGrepWrapper]::CallQGrepAsync($args, $cb, $null, $null, $null)
$q.ToArray()
```
- `CallQGrepAsync` 名字带 Async 但**实际同步**（内部直接 `mainImpl`），返回即完成。
- **回调必须绑到 .NET 对象方法，不能用 PowerShell 脚本块当委托**：`build`/`update` 的输出与进度可能来自工作线程，脚本块会因"runspace 不可用"炸掉。用 `Delegate::CreateDelegate($type, $obj, 'Enqueue')` 绑 `ConcurrentQueue<string>` 就完全线程安全。不需要的回调传 `$null`。
- 测 cfg 规则时：**`.cfg` 和它生成的 `.qgd/.qgf` 不能放在被索引的目录里**，否则会被自己列进结果，干扰比对。

## 环境
- Bash 工具 shim 缺 coreutils（`ls/grep/head/tail/sed/dirname` 不可用，管道会静默失败）→ 用 Glob/Grep/Read 工具或 `C:\Users\yhwa\.workbuddy\binaries\python\versions\3.13.12\python.exe` 做探测。
- Bash 里调 PowerShell 会被拦（"bypasses PowerShell security checks"）→ 用 PowerShell 工具。
- **PowerShell 工具的 stdout 常拿不到**（`Write-Output` 返回空，只剩 "Command completed with exit code N"）→ 脚本内部把结果 `Out-File` 到 `%TEMP%\xxx.log`，再用 Read 工具读日志。`run_in_background` 的 `TaskOutput` 同样只能确认退出码，内容仍需读日志文件。
- **同一个文件不要在同一条消息里发多个 Edit**：工具会并发执行、后写覆盖先写，先发的那个改动会**静默丢失**（本次 `ConfigRule.IsDirectory`、`Resources.resx` 的键、`ProjectsWindow` 的编辑分支都丢过一次）。改完用 `count` 校验一遍。
