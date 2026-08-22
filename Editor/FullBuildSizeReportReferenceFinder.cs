using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DetPanda.Editor.FullBuildSizeReport
{
    /// <summary>
    /// Finds where an asset is used: asset-level refs plus hierarchy chains in scenes/prefabs
    /// like "Main1 -> Canvas -> Progress bar -> Image (m_Sprite)".
    /// </summary>
    public static class FullBuildSizeReferenceFinder
    {
        const int MaxHierarchyResults = 40;
        const int MaxContainersToScan = 80;

        public static void FindHierarchyReferences(
            FullBuildSizeReportEntry entry,
            IReadOnlyList<FullBuildSizeReportEntry> allEntries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.AssetPath))
                return;

            entry.HierarchyReferences.Clear();

            var target = AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
            if (target == null)
            {
                entry.HierarchyReferences.Add("(asset not found in project)");
                return;
            }

            var targetGuid = string.IsNullOrEmpty(entry.Guid)
                ? AssetDatabase.AssetPathToGUID(entry.AssetPath)
                : entry.Guid;

            var containers = new List<string>();
            foreach (var path in entry.AssetLevelReferences)
                AddContainerCandidate(containers, path);

            if (allEntries != null)
            {
                foreach (var other in allEntries)
                {
                    if (other == null || string.IsNullOrEmpty(other.AssetPath))
                        continue;
                    AddContainerCandidate(containers, other.AssetPath);
                }
            }

            var filtered = new List<string>();
            foreach (var container in containers)
            {
                if (filtered.Count >= MaxContainersToScan)
                    break;

                var deps = AssetDatabase.GetDependencies(container, true);
                var depends = deps.Any(d =>
                    string.Equals(d, entry.AssetPath, StringComparison.OrdinalIgnoreCase));
                if (depends)
                    filtered.Add(container);
            }

            if (filtered.Count == 0)
            {
                foreach (var path in entry.AssetLevelReferences.Take(MaxHierarchyResults))
                    entry.HierarchyReferences.Add($"Asset: {Path.GetFileNameWithoutExtension(path)} ({path})");

                if (entry.HierarchyReferences.Count == 0)
                    entry.HierarchyReferences.Add("(no direct scene/prefab refs found in report)");
                return;
            }

            EditorUtility.DisplayProgressBar(
                "Full Build Size Report",
                $"Searching refs for {Path.GetFileName(entry.AssetPath)}...",
                0f);

            try
            {
                for (var i = 0; i < filtered.Count; i++)
                {
                    if (entry.HierarchyReferences.Count >= MaxHierarchyResults)
                        break;

                    var containerPath = filtered[i];
                    EditorUtility.DisplayProgressBar(
                        "Full Build Size Report",
                        Path.GetFileName(containerPath),
                        (float)i / filtered.Count);

                    try
                    {
                        if (containerPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                            ScanScene(containerPath, target, targetGuid, entry.HierarchyReferences);
                        else if (containerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                            ScanPrefab(containerPath, target, targetGuid, entry.HierarchyReferences);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"[BuildSizeExplorer] Failed to scan {containerPath}: {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (entry.HierarchyReferences.Count == 0)
            {
                foreach (var path in entry.AssetLevelReferences.Take(10))
                    entry.HierarchyReferences.Add($"Asset: {Path.GetFileNameWithoutExtension(path)} ({path})");

                if (entry.HierarchyReferences.Count == 0)
                    entry.HierarchyReferences.Add("(no hierarchy refs found — may be an internal bundle dependency)");
            }
        }

        static void AddContainerCandidate(List<string> containers, string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return;
            if (containers.Exists(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                return;
            containers.Add(path);
        }

        static void ScanScene(
            string scenePath,
            UnityEngine.Object target,
            string targetGuid,
            List<string> results)
        {
            Scene scene;
            var openedHere = false;

            var existing = EditorSceneManager.GetSceneByPath(scenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                scene = existing;
            }
            else
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                if (!scene.IsValid())
                    return;

                var rootName = Path.GetFileNameWithoutExtension(scenePath);
                foreach (var root in scene.GetRootGameObjects())
                    ScanGameObject(root, rootName, target, targetGuid, results);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ScanPrefab(
            string prefabPath,
            UnityEngine.Object target,
            string targetGuid,
            List<string> results)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return;

                var rootName = Path.GetFileNameWithoutExtension(prefabPath);
                ScanGameObject(root, rootName, target, targetGuid, results);
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void ScanGameObject(
            GameObject go,
            string containerRootName,
            UnityEngine.Object target,
            string targetGuid,
            List<string> results)
        {
            if (results.Count >= MaxHierarchyResults || go == null)
                return;

            var components = go.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null)
                    continue;

                FindReferencesOnObject(component, containerRootName, target, targetGuid, results);
                if (results.Count >= MaxHierarchyResults)
                    return;
            }

            var transform = go.transform;
            for (var i = 0; i < transform.childCount; i++)
                ScanGameObject(transform.GetChild(i).gameObject, containerRootName, target, targetGuid, results);
        }

        static void FindReferencesOnObject(
            Component component,
            string containerRootName,
            UnityEngine.Object target,
            string targetGuid,
            List<string> results)
        {
            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                if (iterator.objectReferenceValue == null)
                    continue;

                if (!IsSameAsset(iterator.objectReferenceValue, target, targetGuid))
                    continue;

                var hierarchy = BuildHierarchyPath(component.transform);
                var niceProperty = SimplifyPropertyPath(iterator.propertyPath);
                var line =
                    $"{containerRootName} -> {hierarchy} -> {component.GetType().Name} ({niceProperty})";

                if (!results.Exists(x => string.Equals(x, line, StringComparison.Ordinal)))
                    results.Add(line);

                if (results.Count >= MaxHierarchyResults)
                    return;
            }
        }

        static bool IsSameAsset(UnityEngine.Object candidate, UnityEngine.Object target, string targetGuid)
        {
            if (candidate == target)
                return true;

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long _))
                return false;

            return string.Equals(guid, targetGuid, StringComparison.OrdinalIgnoreCase);
        }

        static string BuildHierarchyPath(Transform transform)
        {
            var parts = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return string.Join(" -> ", parts);
        }

        static string SimplifyPropertyPath(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return "reference";

            var parts = propertyPath.Split('.');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("Array", StringComparison.Ordinal))
                    return i > 0 ? parts[i - 1] : parts[i];
            }

            return parts[parts.Length - 1];
        }

        public static string BuildReferenceSummary(FullBuildSizeReportEntry entry)
        {
            if (entry == null)
                return string.Empty;

            var sb = new StringBuilder();

            if (entry.HierarchyReferences.Count > 0)
            {
                sb.AppendLine("Hierarchy:");
                foreach (var line in entry.HierarchyReferences)
                    sb.AppendLine("  • " + line);
            }

            if (entry.AssetLevelReferences.Count > 0)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine("Asset references:");
                foreach (var path in entry.AssetLevelReferences.Take(20))
                    sb.AppendLine("  • " + path);
            }

            return sb.ToString().TrimEnd();
        }
    }
}
