#nullable enable

using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitHarness.Addin.Commands;
using RevitHarness.Addin.Ipc;
using System;
using System.Collections.Generic;
using System.Windows;

namespace RevitHarness.Addin.UI
{
    public partial class AddinManagerWindow : Window
    {
        private readonly UIApplication _uiapp;
        private readonly AssemblyScanner _scanner;
        private readonly CommandExecutor _executor;
        private readonly List<string> _logLines = new List<string>();

        public AddinManagerWindow(UIApplication uiapp)
        {
            InitializeComponent();
            _uiapp = uiapp;

            Action<string> log = msg =>
            {
                _logLines.Add(msg);
            };

            var shadowCopier = new ShadowCopier(log);
            _scanner = new AssemblyScanner(shadowCopier, log);
            _executor = new CommandExecutor(new ShadowCopier(log), log);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
                Title = "Select Assembly"
            };

            if (dialog.ShowDialog() == true)
            {
                DllPathTextBox.Text = dialog.FileName;
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            var path = DllPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                AppendResult("Please specify a DLL path.");
                return;
            }

            try
            {
                _logLines.Clear();
                var types = _scanner.Scan(path);
                CommandGrid.ItemsSource = types;

                if (types.Count == 0)
                    AppendResult("No command types found in the assembly.");
                else
                    AppendResult($"Found {types.Count} command type(s).");
            }
            catch (Exception ex)
            {
                AppendResult($"Scan failed: {ex.Message}");
            }
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = CommandGrid.SelectedItem as DiscoveredType;
            if (selected == null)
            {
                AppendResult("Please select a command to execute.");
                return;
            }

            var path = DllPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                AppendResult("DLL path is empty.");
                return;
            }

            try
            {
                _logLines.Clear();
                AppendResult($"Executing: {selected.FullName} ...");

                var result = _executor.ExecuteCommand(_uiapp, path, selected.FullName);

                if (result.Success)
                {
                    AppendResult($"[OK] {result.ResultText}");
                }
                else
                {
                    AppendResult($"[FAILED] {result.ErrorMessage}");
                    if (result.StackTrace != null)
                        AppendResult(result.StackTrace);
                }
            }
            catch (Exception ex)
            {
                AppendResult($"Execution error: {ex.Message}");
            }
        }

        private void AppendResult(string text)
        {
            if (!string.IsNullOrEmpty(ResultTextBox.Text))
                ResultTextBox.AppendText(Environment.NewLine);
            ResultTextBox.AppendText(text);
            ResultTextBox.ScrollToEnd();
        }
    }
}
