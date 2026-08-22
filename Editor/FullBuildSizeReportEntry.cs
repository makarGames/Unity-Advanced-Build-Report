using System;
using System.Collections.Generic;

namespace DetPanda.Editor.FullBuildSizeReport
{
    [Flags]
    public enum BuildAssetSource
    {
        None = 0,
        Player = 1,
        Addressables = 2,
        Both = Player | Addressables
    }

    /// <summary>
    /// One row in the combined build size report.
    /// Addressables-specific fields are populated when com.unity.addressables is installed.
    /// </summary>
    public sealed class FullBuildSizeReportEntry
    {
        public string AssetPath;
        public string Guid;
        public BuildAssetSource Source;
        public ulong PlayerPackedSizeBytes;
        public ulong AddressablesPackedSizeBytes;
        public string AddressablesGroup;
        public string AddressablesBundle;
        public string AddressableAddress;
        public bool IsImplicitDependency;
        public readonly List<string> AssetLevelReferences = new List<string>();
        public readonly List<string> HierarchyReferences = new List<string>();

        public ulong PackedSizeBytes => PlayerPackedSizeBytes + AddressablesPackedSizeBytes;

        public string SourceLabel
        {
            get
            {
                switch (Source)
                {
                    case BuildAssetSource.Player: return "Player";
                    case BuildAssetSource.Addressables: return "Addressables";
                    case BuildAssetSource.Both: return "Player+AA";
                    default: return "?";
                }
            }
        }

        public string PackedSizeReadable => FormatBytes(PackedSizeBytes);

        public static string FormatBytes(ulong bytes)
        {
            double size = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (size >= 1024d && unit < units.Length - 1)
            {
                size /= 1024d;
                unit++;
            }

            return unit == 0
                ? $"{bytes} {units[unit]}"
                : $"{size:0.##} {units[unit]}";
        }
    }
}
