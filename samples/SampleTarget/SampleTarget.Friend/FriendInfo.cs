using SampleTarget.Core;

namespace SampleTarget.Friend
{
    // Reads an internal member of Core through InternalsVisibleTo. Both assemblies are renamed per generation on net48,
    // so this only works while the rename also rewrites the attribute.
    // It also references Revit assemblies, like a real helper does: the renamed helper must still bind to Revit's own copies.
    public static class FriendInfo
    {
        public static string CoreTag => BuildInfo.Tag;

        public static string RevitApiLocation => typeof(Autodesk.Revit.DB.Document).Assembly.Location;

        // AdWindows is a Revit assembly that is not part of the harness host set: only the Revit-directory rule keeps it Revit's.
        public static string AdWindowsLocation =>
#if NET10_0_OR_GREATER
            "";
#else
            typeof(Autodesk.Windows.RibbonControl).Assembly.Location;
#endif
    }
}
