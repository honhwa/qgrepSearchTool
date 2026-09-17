using qgrepControls.ModelViews;
using System;
using System.Collections.Generic;

namespace qgrepControls.Classes
{
    public delegate void MessageCallback(string message);

    public interface IWrapperApp
    {
        string GetConfigPath(bool useGlobalPath);
        /// <summary>当前解决方案所在目录；没有打开的解决方案时返回空串。</summary>
        string GetSolutionPath();
        void GatherAllFoldersAndExtensionsFromSolution(MessageCallback extensionCallback, MessageCallback folderCallback);
        void OpenFile(string path, string line);
        string GetSelectedText();
        System.Drawing.Color GetColor(string resourceKey);
        string GetMonospaceFont();
        string GetNormalFont();
        void RefreshResources(Dictionary<string, object> newResources);
        bool SearchWindowOpened { get; set; }
        bool IsStandalone { get; }
        Dictionary<string, Hotkey> ReadKeyBindings();
        Dictionary<string, string> ReadKeyBindingsReadOnly();
        bool CanEditKeyBindings();
        void ApplyKeyBindings();
        void SaveKeyBindings(Dictionary<string, Hotkey> bindings);
        List<string> GetConflictingCommandsForBinding(Dictionary<string, Hotkey> bindings);
        System.Windows.Window GetMainWindow();
        void GetIcon(uint background, SearchResult searchResult);
        void StartBackgroundTask(string title);
        void UpdateBackgroundTaskPercentage(int progress);
        void UpdateBackgroundTaskMessage(string message);
        void StopBackgroundTask();
        bool LoadConfigAtStartup();
        void IncludeFile(string path);
        bool IsActiveDocumentCpp();
        /// <summary>当前编辑器活动文件的完整路径；取不到时返回空串。</summary>
        string GetActiveDocumentPath();
        void OpenKeyBindingSettings();
    }
}
