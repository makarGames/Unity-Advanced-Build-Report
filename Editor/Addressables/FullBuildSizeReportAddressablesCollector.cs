using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build.Layout;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DetPanda.Editor.FullBuildSizeReport
{
#if USE_ADDRESSABLES
    [InitializeOnLoad]
    internal static class FullBuildSizeReportAddressablesBootstrap
    {
        static FullBuildSizeReportAddressablesBootstrap()
        {
            FullBuildSizeReportAddressablesHook.Collect = FullBuildSizeReportAddressablesCollector.Collect;
        }
    }

    /// <summary>
    /// Addressables Build Layout collection (compiled only when com.unity.addressables is installed).
    /// </summary>
    internal static class FullBuildSizeReportAddressablesCollector
    {
        public static void Collect(
            FullBuildSizeReportResult result,
            Dictionary<string, FullBuildSizeReportEntry> byPath)
        {
            if (!ProjectConfigData.GenerateBuildLayout)
            {
                ProjectConfigData.GenerateBuildLayout = true;
                Debug.Log("[BuildSizeExplorer] Enabled Generate Build Layout for Addressables (Preferences).");
            }

            var layoutPath = FindLatestAddressablesLayoutPath();
            if (string.IsNullOrEmpty(layoutPath))
            {
                result.AddressablesReportStatus =
                    "not found. Build Addressables (or run a Player Build if AA is part of your pipeline). " +
                    "Check Edit → Preferences → Addressables → Build Layout Report.";
                return;
            }

            BuildLayout layout = null;
            try
            {
                layout = BuildLayout.Open(layoutPath, readHeader: true, readFullFile: true);
            }
            catch (Exception ex)
            {
                result.AddressablesReportStatus = $"read error: {ex.Message}";
                return;
            }

            if (layout == null)
            {
                result.AddressablesReportStatus = "could not open Build Layout.";
                return;
            }

            try
            {
                result.AddressablesReportPath = layoutPath;
                result.AddressablesReportStatus =
                    $"OK · {layout.BuildTarget} · {Path.GetFileName(layoutPath)} · {layout.BuildStart:yyyy-MM-dd HH:mm}";

                var groupNameByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (layout.Groups != null)
                {
                    foreach (var group in layout.Groups)
                    {
                        if (group != null && !string.IsNullOrEmpty(group.Guid))
                            groupNameByGuid[group.Guid] = group.Name;
                    }
                }

                ulong aaSum = 0;

                foreach (var bundle in BuildLayoutHelpers.EnumerateBundles(layout))
                {
                    if (bundle?.Files == null)
                        continue;

                    var bundleUncompressed = bundle.UncompressedFileSize;
                    var bundleCompressed = bundle.FileSize;

                    foreach (var file in bundle.Files)
                    {
                        if (file == null)
                            continue;

                        if (file.Assets != null)
                        {
                            foreach (var asset in file.Assets)
                            {
                                if (asset == null || string.IsNullOrEmpty(asset.AssetPath))
                                    continue;
                                if (!FullBuildSizeReportCollector.IsProjectAssetPath(asset.AssetPath))
                                    continue;

                                var size = EstimateOnDiskShare(
                                    asset.SerializedSize + asset.StreamedSize,
                                    bundleUncompressed,
                                    bundleCompressed);
                                aaSum += size;

                                var entry = FullBuildSizeReportCollector.GetOrCreate(byPath, asset.AssetPath);
                                entry.Source |= BuildAssetSource.Addressables;
                                entry.AddressablesPackedSizeBytes += size;
                                entry.Guid = asset.Guid;
                                entry.AddressableAddress = asset.AddressableName;
                                entry.AddressablesBundle = bundle.Name;
                                entry.IsImplicitDependency = false;

                                if (!string.IsNullOrEmpty(asset.GroupGuid) &&
                                    groupNameByGuid.TryGetValue(asset.GroupGuid, out var groupName))
                                    entry.AddressablesGroup = groupName;

                                CollectAssetLevelReferences(entry, asset);
                            }
                        }

                        if (file.OtherAssets != null)
                        {
                            foreach (var other in file.OtherAssets)
                            {
                                if (other == null || string.IsNullOrEmpty(other.AssetPath))
                                    continue;
                                if (!FullBuildSizeReportCollector.IsProjectAssetPath(other.AssetPath))
                                    continue;

                                var size = EstimateOnDiskShare(
                                    other.SerializedSize + other.StreamedSize,
                                    bundleUncompressed,
                                    bundleCompressed);
                                aaSum += size;

                                var entry = FullBuildSizeReportCollector.GetOrCreate(byPath, other.AssetPath);
                                entry.Source |= BuildAssetSource.Addressables;
                                entry.AddressablesPackedSizeBytes += size;
                                entry.Guid = other.AssetGuid;
                                entry.AddressablesBundle = bundle.Name;
                                entry.IsImplicitDependency = true;

                                if (other.ReferencingAssets != null)
                                {
                                    foreach (var referencer in other.ReferencingAssets)
                                    {
                                        if (referencer == null || string.IsNullOrEmpty(referencer.AssetPath))
                                            continue;
                                        FullBuildSizeReportCollector.AddUnique(entry.AssetLevelReferences, referencer.AssetPath);
                                    }
                                }
                            }
                        }
                    }
                }

                result.AddressablesTotalBytes = aaSum;
            }
            finally
            {
                layout.Close();
            }
        }

        static void CollectAssetLevelReferences(FullBuildSizeReportEntry entry, BuildLayout.ExplicitAsset asset)
        {
            if (asset.ReferencingAssets == null)
                return;

            foreach (var referencer in asset.ReferencingAssets)
            {
                if (referencer == null || string.IsNullOrEmpty(referencer.AssetPath))
                    continue;
                FullBuildSizeReportCollector.AddUnique(entry.AssetLevelReferences, referencer.AssetPath);
            }
        }

        static ulong EstimateOnDiskShare(ulong assetUncompressed, ulong bundleUncompressed, ulong bundleCompressed)
        {
            if (assetUncompressed == 0)
                return 0;

            if (bundleUncompressed == 0 || bundleCompressed == 0)
                return assetUncompressed;

            return (ulong)Math.Round(assetUncompressed / (double)bundleUncompressed * bundleCompressed);
        }

        static string FindLatestAddressablesLayoutPath()
        {
            var folder = Addressables.BuildReportPath;
            if (!Directory.Exists(folder))
                return null;

            return Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }
#endif
}
