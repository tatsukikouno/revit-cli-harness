using SampleTarget.Core;

namespace SampleTarget.Friend
{
    // Reads an internal member of Core through InternalsVisibleTo. Both assemblies are renamed per generation on net48,
    // so this only works while the rename also rewrites the attribute.
    public static class FriendInfo
    {
        public static string CoreTag => BuildInfo.Tag;
    }
}
