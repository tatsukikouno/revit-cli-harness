#nullable enable

using System.IO;

namespace RevitHarness.Cli.Installation
{
    public readonly struct ParkResult
    {
        public ParkResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public bool Ok { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Takes a target add-in's own .addin out of Revit's sight so the harness loader is the only active manifest.
    /// Revit reads only files that end in .addin, so parking is a rename in place. Nothing is deleted.
    /// </summary>
    public static class AddinParker
    {
        public const string ParkedSuffix = ".parked";

        public static ParkResult Park(string addinPath)
        {
            if (!addinPath.EndsWith(".addin", System.StringComparison.OrdinalIgnoreCase))
                return new ParkResult(false, $"{addinPath} is not an .addin file.");
            if (string.Equals(Path.GetFileName(addinPath), "RevitHarness.addin", System.StringComparison.OrdinalIgnoreCase))
                return new ParkResult(false, "RevitHarness.addin is the harness loader itself; parking it would disable the harness.");
            var parked = addinPath + ParkedSuffix;

            if (!File.Exists(addinPath))
                return File.Exists(parked)
                    ? new ParkResult(true, $"Already parked: {parked}")
                    : new ParkResult(false, $"No such manifest: {addinPath}");

            // A build that re-emits the manifest wins over the older parked copy.
            File.Move(addinPath, parked, overwrite: true);
            return new ParkResult(true, $"Parked {addinPath} as {parked}. Revit reads manifests at startup, so this takes effect when Revit next starts.");
        }

        public static ParkResult Restore(string addinPath)
        {
            if (!addinPath.EndsWith(".addin", System.StringComparison.OrdinalIgnoreCase))
                return new ParkResult(false, $"{addinPath} is not an .addin file.");
            var parked = addinPath + ParkedSuffix;

            if (File.Exists(addinPath))
                return File.Exists(parked)
                    ? new ParkResult(false, $"Both {addinPath} and {parked} exist; refusing to overwrite. Keep one by hand.")
                    : new ParkResult(true, $"Already active: {addinPath}");
            if (!File.Exists(parked))
                return new ParkResult(false, $"Nothing parked for {addinPath}.");

            File.Move(parked, addinPath);
            return new ParkResult(true, $"Restored {addinPath}. It is active when Revit next starts.");
        }
    }
}
