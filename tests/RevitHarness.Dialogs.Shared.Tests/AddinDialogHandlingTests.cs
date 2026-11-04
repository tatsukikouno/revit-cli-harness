using System;
using System.IO;
using RevitHarness.Addin.DialogHandling;
using RevitHarness.Dialogs;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public class AddinDialogHandlingTests
    {
        [Fact]
        public void MessageBox_YesNo_GetsSyntheticButtonsWithResultCodes()
        {
            var info = DialogInfoBuilder.ForMessageBox("Dialog_X", "Save?", 4);
            Assert.Equal(new[] { "Yes", "No" }, info.Buttons);
            Assert.Equal("6", info.ButtonDetails[0].AutomationId);
            Assert.Equal(DialogSource.Api, info.Source);
        }

        [Fact]
        public void TaskDialog_HasNoButtons()
        {
            var info = DialogInfoBuilder.ForTaskDialog("TaskDialog_Foo", "hello");
            Assert.Empty(info.ButtonDetails);
            Assert.Equal("hello", info.Text);
        }

        [Theory]
        [InlineData("Ok", 1)] [InlineData("yes", 6)] [InlineData("CommandLink2", 1002)] [InlineData("8", 8)]
        public void ResultCodeParsing(string text, int expected)
        {
            Assert.True(DialogResultCodes.TryParse(text, out var code));
            Assert.Equal(expected, code);
        }

        [Fact]
        public void Decision_PrefersButtonCode_ThenRuleResultCode()
        {
            var withButton = new DialogDecision { Button = new DialogButton { Name = "No", AutomationId = "7" } };
            Assert.True(DialogInfoBuilder.TryGetResultCode(withButton, out var a));
            Assert.Equal(7, a);
            var withCode = new DialogDecision { ResultCode = "Cancel" };
            Assert.True(DialogInfoBuilder.TryGetResultCode(withCode, out var b));
            Assert.Equal(2, b);
            Assert.False(DialogInfoBuilder.TryGetResultCode(new DialogDecision(), out _));
        }

        [Fact]
        public void Failure_IdCarriesPrefixAndGuid()
        {
            var g = Guid.NewGuid();
            Assert.Equal("failure:" + g.ToString("D"), DialogInfoBuilder.ForFailure(g, "d").DialogId);
        }

        [Fact]
        public void LogReader_ReturnsNewestRecordsAcrossFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "rh-" + Guid.NewGuid().ToString("N"));
            try
            {
                var dir = DialogPaths.GetDirectory(root);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "dialogs_20260101.jsonl"), "{\"n\":1}\n{\"n\":2}\n");
                File.WriteAllText(Path.Combine(dir, "dialogs_20260102.jsonl"), "{\"n\":3}\n");
                Assert.Equal(new[] { "{\"n\":2}", "{\"n\":3}" }, DialogLogReader.ReadRecent(2, root));
                Assert.Empty(DialogLogReader.ReadRecent(5, Path.Combine(root, "none")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void ResultCodeIsOnlyOfferedWhenTheDialogReportsThatButton()
        {
            var box = DialogInfoBuilder.ForMessageBox("id", "text", 1);
            Assert.True(DialogInfoBuilder.IsOffered(box, 1));
            Assert.True(DialogInfoBuilder.IsOffered(box, 2));
            Assert.False(DialogInfoBuilder.IsOffered(box, 6));
            Assert.True(DialogInfoBuilder.IsOffered(DialogInfoBuilder.ForTaskDialog("id", "text"), 1001));
        }
    }
}
