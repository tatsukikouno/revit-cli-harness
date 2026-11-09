#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitHarness.Cli.Installation
{
    /// <summary>
    /// Places the bundled SKILLs into a directory the caller names (a Claude Code skills folder,
    /// on Windows or on WSL via a \\wsl.localhost UNC path). Independent of the Revit install.
    /// A skill folder is replaced or removed only when it carries the marker this wrote.
    /// </summary>
    public sealed class SkillInstaller
    {
        private const string MarkerFile = ".revit-harness";

        private readonly string _bundledSkillsDir;

        public SkillInstaller(string bundledSkillsDir) => _bundledSkillsDir = bundledSkillsDir;

        public SkillResult Install(string skillsDir)
        {
            if (!Directory.Exists(_bundledSkillsDir))
                return Fail($"This CLI build does not bundle the SKILLs (expected {_bundledSkillsDir}).");

            try
            {
                var names = BundledSkillNames().ToList();
                var foreign = names
                    .Select(name => Path.Combine(skillsDir, name))
                    .FirstOrDefault(dest => Directory.Exists(dest) && !IsOwned(dest));
                if (foreign != null)
                    return Fail($"{foreign} already exists and was not installed by revit-harness; move it away or choose another --dir.");

                var files = new List<string>();
                foreach (var name in names)
                {
                    var dest = Path.Combine(skillsDir, name);
                    RevitInstaller.ReplaceDirectory(dest);
                    RevitInstaller.CopyDirectory(Path.Combine(_bundledSkillsDir, name), dest, files);
                    File.WriteAllText(Path.Combine(dest, MarkerFile), "installed by revit-harness install-skills");
                }

                return new SkillResult { Success = true, Message = $"Installed {names.Count} SKILLs into {skillsDir}", Files = files };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail($"Installing SKILLs failed: {ex.Message}");
            }
        }

        public SkillResult Uninstall(string skillsDir)
        {
            try
            {
                var removed = new List<string>();
                foreach (var name in BundledSkillNames())
                {
                    var dest = Path.Combine(skillsDir, name);
                    if (!IsOwned(dest))
                        continue;
                    Directory.Delete(dest, recursive: true);
                    removed.Add(dest);
                }

                return new SkillResult { Success = true, Message = $"Removed {removed.Count} SKILLs from {skillsDir}", Files = removed };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail($"Removing SKILLs failed: {ex.Message}");
            }
        }

        private IEnumerable<string> BundledSkillNames() =>
            Directory.Exists(_bundledSkillsDir)
                ? Directory.GetDirectories(_bundledSkillsDir).Select(d => Path.GetFileName(d)!)
                : Enumerable.Empty<string>();

        private static bool IsOwned(string dir) => File.Exists(Path.Combine(dir, MarkerFile));

        private static SkillResult Fail(string message) => new SkillResult { Success = false, Message = message };
    }

    public sealed class SkillResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
    }
}
