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
        private readonly ExternalEvent _externalEvent;
        private readonly AddinManagerEventHandler _eventHandler;
        private readonly AssemblyScanner _scanner;
        private readonly List<string> _logLines = new List<string>();

        public AddinManagerWindow(ExternalEvent externalEvent, AddinManagerEventHandler eventHandler)
        {
            InitializeComponent();
            _externalEvent = externalEvent;
            _eventHandler = eventHandler;

            Action<string> log = msg =>
            {
                _logLines.Add(msg);
            };

            var shadowCopier = new ShadowCopier(log);
            _scanner = new AssemblyScanner(shadowCopier, log);
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

            _logLines.Clear();
            AppendResult($"Executing: {selected.FullName} ...");

            _eventHandler.Enqueue(new AddinManagerRequest
            {
                AssemblyPath = path,
                CommandType = selected.FullName,
                Callback = result => Dispatcher.Invoke(() => ShowResult(result))
            });
            _externalEvent.Raise();
        }

        private void ShowResult(CommandExecutionResult result)
        {
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

        private void AppendResult(string text)
        {
            if (!string.IsNullOrEmpty(ResultTextBox.Text))
                ResultTextBox.AppendText(Environment.NewLine);
            ResultTextBox.AppendText(text);
            ResultTextBox.ScrollToEnd();
        }
    }
}
