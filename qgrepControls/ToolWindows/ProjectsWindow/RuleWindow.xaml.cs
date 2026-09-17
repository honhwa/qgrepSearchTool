using qgrepControls.Classes;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace qgrepControls.SearchWindow
{
    public partial class RuleWindow : System.Windows.Controls.UserControl
    {
        public const int TypeInclude = 0;
        public const int TypeExclude = 1;
        public const int TypeExcludeDirectory = 2;

        public MainWindow Dialog = null;
        public delegate void Callback(bool accepted);
        public bool IsOK = false;

        public RuleWindow(IWrapperApp WrapperApp)
        {
            InitializeComponent();
            ThemeHelper.UpdateColorsFromSettings(this, WrapperApp);
        }

        /// <summary>当前是否为「不包含的目录」类型。</summary>
        public bool IsDirectoryRule
        {
            get
            {
                return RuleType.SelectedIndex == TypeExcludeDirectory;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsOK = false;
            if (Dialog != null)
            {
                Dialog.Close();
            }
        }

        private void UserControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        }

        private void Accept()
        {
            // 目录类型下输入的是目录名（可用 ; , | 或空白分隔多个），
            // 落盘前统一整理成引擎能识别的正则形式。
            if (IsDirectoryRule)
            {
                string regex = DirectoryRule.BuildRegex(RegExTextBox.Text);

                if (regex.Length > 0)
                {
                    RegExTextBox.Text = regex;
                }
            }

            IsOK = true;
            if (Dialog != null)
            {
                Dialog.Close();
            }
        }

        private void PredefinedButton_Click(object sender, RoutedEventArgs e)
        {
            PredefinedPopup.IsOpen = !PredefinedPopup.IsOpen;
            RegExTextBox.SelectAll();
        }

        private void ComboBoxItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            PredefinedPopup.IsOpen = false;
            ComboBoxItem comboBox = sender as ComboBoxItem;
            if (comboBox != null)
            {
                string value = comboBox.Tag as String ?? "";

                if (DirectoryRule.IsDirectoryRegex(value))
                {
                    // 目录类预设：自动切到「不包含的目录」，并回显成便于阅读的目录名
                    RuleType.SelectedIndex = TypeExcludeDirectory;
                    RegExTextBox.Text = DirectoryRule.ExtractInput(value);
                }
                else
                {
                    RegExTextBox.Text = value;
                }
            }
        }
    }
}
