using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DetPanda.Editor.FullBuildSizeReport
{
    public sealed class FullBuildSizeReportResult
    {
        public readonly List<FullBuildSizeReportEntry> Entries = new List<FullBuildSizeReportEntry>();
        public string PlayerReportStatus = "not found";
        public string AddressablesReportStatus = "not found";
        public string PlayerReportPath;
        public string AddressablesReportPath;
        public ulong PlayerTotalBytes;
        public ulong AddressablesTotalBytes;
        public ulong CombinedUniqueBytes;
    }

    /// <summary>
    /// Collects asset sizes from Unity BuildReport (Player) and, when available,
    /// Addressables Build Layout into a single list.
    /// </summary>
    public static class FullBuildSizeReportCollector
    {
        public static FullBuildSizeReportResult Collect()
        {
            var result = new FullBuildSizeReportResult();
            var byPath = new Dictionary<string, FullBuildSizeReportEntry>(StringComparer.OrdinalIgnoreCase);

            CollectPlayerPackedAssets(result, byPath);

            if (FullBuildSizeReportAddressablesHook.Collect != null)
            {
                FullBuildSizeReportAddressablesHook.Collect(result, byPath);
            }
            else
            {
                result.AddressablesReportStatus = "Addressables package not installed";
            }

            result.Entries.AddRange(byPath.Values);
            result.Entries.Sort((a, b) => b.PackedSizeBytes.CompareTo(a.PackedSizeBytes));

            result.CombinedUniqueBytes = 0;
            foreach (var entry in result.Entries)
                result.CombinedUniqueBytes += entry.PackedSizeBytes;

            return result;
        }

        public static FullBuildSizeReportEntry GetOrCreate(
            Dictionary<string, FullBuildSizeReportEntry> byPath,
            string assetPath)
        {
            if (byPath.TryGetValue(assetPath, out var existing))
                return existing;

            var created = new FullBuildSizeReportEntry
            {
                AssetPath = assetPath,
                Guid = AssetDatabase.AssetPathToGUID(assetPath)
            };
            byPath[assetPath] = created;
            return created;
        }

        public static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            if (list.Exists(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                return;
            list.Add(value);
        }

        public static bool IsProjectAssetPath(string path)
        {
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        static void CollectPlayerPackedAssets(
            FullBuildSizeReportResult result,
            Dictionary<string, FullBuildSizeReportEntry> byPath)
        {
            BuildReport report = null;

            try
            {
                report = BuildReport.GetLatestReport();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildSizeExplorer] GetLatestReport failed: {ex.Message}");
            }

            if (report == null)
            {
                result.PlayerReportStatus =
                    "not found. Run a Player Build first (File → Build Settings / Build Profiles).";
                return;
            }

            result.PlayerReportPath = "BuildReport.GetLatestReport()";
            result.PlayerReportStatus =
                $"OK · {report.summary.platform} · {FullBuildSizeReportEntry.FormatBytes(report.summary.totalSize)} · {report.summary.buildStartedAt:yyyy-MM-dd HH:mm}";

            var packedByPath = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            var guidByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var packedAssets = report.packedAssets;
            if (packedAssets != null)
            {
                foreach (var packed in packedAssets)
                {
                    if (packed == null)
                        continue;

                    var shortPath = packed.shortPath ?? string.Empty;
                    if (IsAddressablesStreamingBlob(shortPath))
                        continue;

                    var contents = packed.contents;
                    if (contents == null)
                        continue;

                    foreach (var info in contents)
                    {
                        var assetPath = info.sourceAssetPath;
                        if (string.IsNullOrEmpty(assetPath))
                            continue;
                        if (!IsProjectAssetPath(assetPath))
                            continue;

                        if (!packedByPath.TryGetValue(assetPath, out var size))
                            size = 0;
                        packedByPath[assetPath] = size + info.packedSize;

                        var guidString = info.sourceAssetGUID.ToString();
                        if (!string.IsNullOrEmpty(guidString) && guidString != "0" &&
                            guidString != "00000000000000000000000000000000")
                            guidByPath[assetPath] = guidString;
                    }
                }
            }

            ulong playerSum = 0;
            foreach (var kv in packedByPath)
            {
                playerSum += kv.Value;
                var entry = GetOrCreate(byPath, kv.Key);
                entry.Source |= BuildAssetSource.Player;
                entry.PlayerPackedSizeBytes += kv.Value;
                if (guidByPath.TryGetValue(kv.Key, out var guid))
                    entry.Guid = guid;
            }

            result.PlayerTotalBytes = playerSum;
        }

        static bool IsAddressablesStreamingBlob(string shortPath)
        {
            if (string.IsNullOrEmpty(shortPath))
                return false;

            var normalized = shortPath.Replace('\\', '/');
            return normalized.IndexOf("/aa/", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalized.StartsWith("aa/", StringComparison.OrdinalIgnoreCase)
                   || normalized.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase);
        }
    }
}
