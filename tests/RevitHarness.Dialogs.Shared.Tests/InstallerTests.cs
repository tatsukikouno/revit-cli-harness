using System;
using System.IO;
using System.Linq;
using RevitHarness.Cli.Installation;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public sealed class InstallerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rh-install-" + Guid.NewGuid().ToString("N"));
        private readonly InstallLayout _layout;

        public InstallerTests()
        {
            var bundle = Path.Combine(_root, "bundle");
            Directory.CreateDirectory(Path.Combine(bundle, "watcher", "sub"));
            File.WriteAllText(Path.Combine(bundle, "watcher", InstallLayout.WatcherExeName), "exe-v1");
            File.WriteAllText(Path.Combine(bundle, "watcher", "sub", "lib.dll"), "lib");
            Directory.CreateDirectory(Path.Combine(bundle, "skills", "skill-a"));
            File.WriteAllText(Path.Combine(bundle, "skills", "skill-a", "SKILL.md"), "a");
            Directory.CreateDirectory(Path.Combine(bundle, "skills", "skill-b"));
            File.WriteAllText(Path.Combine(bundle, "skills", "skill-b", "SKILL.md"), "b");

            _layout = new InstallLayout(
                Path.Combine(_root, "appdata"), Path.Combine(_root, "local"), Path.Combine(_root, "skills"), bundle);
            Directory.CreateDirectory(_layout.AddinsFolder(2024));
            Directory.CreateDirectory(_layout.AddinsFolder(2025));
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private RevitInstaller NewInstaller() => new RevitInstaller(_layout);

        [Fact]
        public void Install_places_addin_watcher_and_skills()
        {
            var result = NewInstaller().Install(2024);

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(_layout.InstallationFolder(2024), "RevitHarness.Addin.dll")));
            Assert.True(File.Exists(_layout.ManifestPath(2024)));
            Assert.Equal("exe-v1", File.ReadAllText(_layout.WatcherExe));
            Assert.True(File.Exists(Path.Combine(_layout.WatcherDir, "sub", "lib.dll")));
            Assert.True(File.Exists(Path.Combine(_layout.SkillsDir, "skill-a", "SKILL.md")));
            Assert.True(File.Exists(Path.Combine(_layout.SkillsDir, "skill-b", "SKILL.md")));
        }

        [Fact]
        public void Install_twice_replaces_stale_files()
        {
            NewInstaller().Install(2024);
            File.WriteAllText(Path.Combine(_layout.WatcherDir, "stale.dll"), "old");
            File.WriteAllText(Path.Combine(_layout.InstallationFolder(2024), "stale.dll"), "old");
            File.WriteAllText(Path.Combine(_layout.SkillsDir, "skill-a", "old.md"), "old");
            File.WriteAllText(Path.Combine(_layout.BundledWatcherDir, InstallLayout.WatcherExeName), "exe-v2");

            var result = NewInstaller().Install(2024);

            Assert.True(result.Success, result.Message);
            Assert.Equal("exe-v2", File.ReadAllText(_layout.WatcherExe));
            Assert.False(File.Exists(Path.Combine(_layout.WatcherDir, "stale.dll")));
            Assert.False(File.Exists(Path.Combine(_layout.InstallationFolder(2024), "stale.dll")));
            Assert.False(File.Exists(Path.Combine(_layout.SkillsDir, "skill-a", "old.md")));
        }

        [Fact]
        public void Install_leaves_foreign_skills_alone()
        {
            Directory.CreateDirectory(Path.Combine(_layout.SkillsDir, "someone-elses"));
            File.WriteAllText(Path.Combine(_layout.SkillsDir, "someone-elses", "SKILL.md"), "keep");

            NewInstaller().Install(2024);
            NewInstaller().Uninstall(2024);

            Assert.True(File.Exists(Path.Combine(_layout.SkillsDir, "someone-elses", "SKILL.md")));
        }

        [Fact]
        public void Install_fails_without_bundle()
        {
            Directory.Delete(_layout.BundledWatcherDir, recursive: true);

            var result = NewInstaller().Install(2024);

            Assert.False(result.Success);
            Assert.False(File.Exists(_layout.ManifestPath(2024)));
        }

        [Fact]
        public void Uninstall_last_version_removes_everything_but_dialogs_data()
        {
            NewInstaller().Install(2024);
            Directory.CreateDirectory(_layout.DialogsDir);
            File.WriteAllText(Path.Combine(_layout.DialogsDir, "allowlist.json"), "{}");

            var result = NewInstaller().Uninstall(2024);

            Assert.True(result.Success, result.Message);
            Assert.False(File.Exists(_layout.ManifestPath(2024)));
            Assert.False(Directory.Exists(_layout.InstallationFolder(2024)));
            Assert.False(Directory.Exists(_layout.WatcherDir));
            Assert.False(Directory.Exists(Path.Combine(_layout.SkillsDir, "skill-a")));
            Assert.True(File.Exists(Path.Combine(_layout.DialogsDir, "allowlist.json")));
        }

        [Fact]
        public void Uninstall_keeps_watcher_and_skills_while_another_version_is_installed()
        {
            NewInstaller().Install(2024);
            NewInstaller().Install(2025);

            NewInstaller().Uninstall(2024);

            Assert.False(Directory.Exists(_layout.InstallationFolder(2024)));
            Assert.True(File.Exists(_layout.WatcherExe));
            Assert.True(File.Exists(Path.Combine(_layout.SkillsDir, "skill-a", "SKILL.md")));
        }

        [Fact]
        public void Uninstall_when_nothing_installed_succeeds()
        {
            var result = NewInstaller().Uninstall(2024);

            Assert.True(result.Success, result.Message);
            Assert.Empty(result.RemovedFiles);
        }
    }

    public sealed class WatcherControlTests : IDisposable
    {
        private const string WatcherName = "RevitHarness.DialogWatcher";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rh-watcher-" + Guid.NewGuid().ToString("N"));
        private readonly InstallLayout _layout;

        public WatcherControlTests()
        {
            _layout = new InstallLayout(Path.Combine(_root, "appdata"), Path.Combine(_root, "local"), Path.Combine(_root, "skills"), Path.Combine(_root, "bundle"));
            Directory.CreateDirectory(_layout.WatcherDir);
            File.WriteAllText(_layout.WatcherExe, "exe");
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private void WritePid(int pid)
        {
            Directory.CreateDirectory(_layout.DialogsDir);
            File.WriteAllText(_layout.PidFile, pid.ToString());
        }

        [Fact]
        public void Start_does_not_spawn_a_second_watcher()
        {
            WritePid(4242);
            var spawns = 0;
            var control = new WatcherControl(_layout, pid => pid == 4242 ? WatcherName : null, _ => { spawns++; return 1; });

            var first = control.Start();
            var second = control.Start();

            Assert.True(first.Ok);
            Assert.True(second.Ok);
            Assert.Equal(0, spawns);
            Assert.Equal(4242, second.State.Pid);
            Assert.Contains("already running", second.Message);
        }

        [Fact]
        public void Start_spawns_once_then_is_idempotent()
        {
            var alive = false;
            var spawns = 0;
            var control = new WatcherControl(_layout,
                pid => alive && pid == 777 ? WatcherName : null,
                _ => { spawns++; alive = true; WritePid(777); return 777; });

            var first = control.Start();
            var second = control.Start();

            Assert.True(first.Ok, first.Message);
            Assert.Equal(777, first.State.Pid);
            Assert.True(second.Ok);
            Assert.Equal(1, spawns);
        }

        [Fact]
        public void Start_ignores_a_stale_pid_file()
        {
            WritePid(31337);
            var spawns = 0;
            var control = new WatcherControl(_layout,
                pid => pid == 55 ? WatcherName : (pid == 31337 ? "notepad" : null),
                _ => { spawns++; WritePid(55); return 55; });

            var result = control.Start();

            Assert.True(result.Ok, result.Message);
            Assert.Equal(1, spawns);
            Assert.Equal(55, result.State.Pid);
        }

        [Fact]
        public void Start_reports_a_watcher_that_exits_immediately()
        {
            var control = new WatcherControl(_layout, _ => null, _ => 9);

            var result = control.Start();

            Assert.False(result.Ok);
            Assert.False(result.State.Running);
        }

        [Fact]
        public void Start_fails_clearly_when_not_installed()
        {
            File.Delete(_layout.WatcherExe);
            var control = new WatcherControl(_layout, _ => null, _ => throw new InvalidOperationException("must not spawn"));

            var result = control.Start();

            Assert.False(result.Ok);
            Assert.Contains("install", result.Message);
        }

        [Fact]
        public void Stop_when_not_running_is_ok()
        {
            var result = new WatcherControl(_layout, _ => null, _ => 0).Stop();

            Assert.True(result.Ok);
            Assert.Equal("not running", result.Message);
        }

        [Fact]
        public void Status_reports_pid_and_latest_log()
        {
            WritePid(4242);
            File.WriteAllText(Path.Combine(_layout.DialogsDir, "dialogs_20260101.jsonl"), "");
            File.WriteAllText(Path.Combine(_layout.DialogsDir, "dialogs_20260102.jsonl"), "");
            var control = new WatcherControl(_layout, pid => pid == 4242 ? WatcherName : null, _ => 0);

            var status = control.Status();

            Assert.True(status.Ok);
            Assert.Equal(4242, status.State.Pid);
            Assert.Equal(_layout.DialogsDir, status.State.LogDirectory);
            Assert.Equal("dialogs_20260102.jsonl", Path.GetFileName(status.State.LatestLog));
        }
    }
}
