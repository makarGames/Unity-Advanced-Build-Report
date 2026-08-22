using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DetPanda.Editor.FullBuildSizeReport
{
    /// <summary>
    /// Combined build size report: Player BuildReport + optional Addressables Build Layout.
    /// Menu: Window → Analysis → Full Build Size Report
    /// </summary>
    public sealed class FullBuildSizeReportWindow : EditorWindow
    {
        enum SourceFilter
        {
            All = 0,
            Player = 1,
            Addressables = 2,
            Both = 3
        }

        static readonly int[] PageSizeOptions = { 10, 20, 50 };
        static readonly string[] PageSizeLabels = { "10", "20", "50" };

        FullBuildSizeReportResult _result;
        List<FullBuildSizeReportEntry> _filtered = new List<FullBuildSizeReportEntry>();
        Vector2 _listScroll;
        Vector2 _detailsScroll;
        string _search = string.Empty;
        SourceFilter _sourceFilter = SourceFilter.All;
        int _selectedIndex = -1;
        string _status = "Нажмите Refresh после Player Build.";
        bool _showOnlyLarge;
        ulong _largeThresholdBytes = 100 * 1024;
        int _pageSizeIndex = 1;
        int _pageIndex;

        int PageSize => PageSizeOptions[Mathf.Clamp(_pageSizeIndex, 0, PageSizeOptions.Length - 1)];

        int PageCount
        {
            get
            {
                if (_filtered.Count == 0)
                    return 1;
                return Mathf.Max(1, Mathf.CeilToInt(_filtered.Count / (float)PageSize));
            }
        }

        [MenuItem("Window/Analysis/Full Build Size Report")]
        public static void Open()
        {
            var window = GetWindow<FullBuildSizeReportWindow>("Full Build Size");
            window.minSize = new Vector2(960, 520);
            window.Show();
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawSummary();
            DrawFilters();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawAssetList();
                DrawDetailsPanel();
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    RefreshReport();

                if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    ExportCsv();

#if USE_ADDRESSABLES
                if (GUILayout.Button("Open AA Build Report", EditorStyles.toolbarButton, GUILayout.Width(140)))
                    EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Build Report");
#endif

                GUILayout.FlexibleSpace();
                GUILayout.Label(_status, EditorStyles.miniLabel);
            }
        }

        void DrawSummary()
        {
            if (_result == null)
            {
                EditorGUILayout.HelpBox(
                    "Этот отчёт объединяет:\n" +
                    "• Player BuildReport — ассеты обычного билда\n" +
#if USE_ADDRESSABLES
                    "• Addressables Build Layout — ассеты внутри бандлов (если установлен пакет Addressables)\n\n" +
                    "Сделайте Player Build, затем нажмите Refresh.",
#else
                    "• Addressables Build Layout — доступен после установки com.unity.addressables\n\n" +
                    "Сделайте Player Build, затем нажмите Refresh.",
#endif
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"Player: {_result.PlayerReportStatus}",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                $"Addressables: {_result.AddressablesReportStatus}",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStatBox("Player assets", _result.PlayerTotalBytes);
                DrawStatBox("Addressables assets", _result.AddressablesTotalBytes);
                DrawStatBox("Unique combined", _result.CombinedUniqueBytes);
                DrawStatBox("Rows", (ulong)_result.Entries.Count, isBytes: false);
            }
        }

        void DrawStatBox(string title, ulong value, bool isBytes = true)
        {
            using (new EditorGUILayout.VerticalScope("box", GUILayout.MinWidth(140)))
            {
                GUILayout.Label(title, EditorStyles.miniBoldLabel);
                GUILayout.Label(
                    isBytes ? FullBuildSizeReportEntry.FormatBytes(value) : value.ToString("N0"),
                    EditorStyles.boldLabel);
            }
        }

        void DrawFilters()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _search = EditorGUILayout.TextField("Search", _search);
                _sourceFilter = (SourceFilter)EditorGUILayout.EnumPopup("Source", _sourceFilter, GUILayout.Width(220));
                _showOnlyLarge = EditorGUILayout.ToggleLeft($"Only > {FullBuildSizeReportEntry.FormatBytes(_largeThresholdBytes)}", _showOnlyLarge, GUILayout.Width(180));
                if (EditorGUI.EndChangeCheck())
                    ApplyFilter();
            }

            DrawPaginationBar();
        }

        void DrawPaginationBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("На странице:", GUILayout.Width(85));
                _pageSizeIndex = GUILayout.Toolbar(_pageSizeIndex, PageSizeLabels, GUILayout.Width(140));
                if (EditorGUI.EndChangeCheck())
                {
                    _pageIndex = 0;
                    _listScroll = Vector2.zero;
                    ClampPageIndex();
                }

                GUILayout.Space(12);

                EditorGUI.BeginDisabledGroup(_pageIndex <= 0);
                if (GUILayout.Button("◀ Prev", GUILayout.Width(70)))
                {
                    _pageIndex--;
                    _listScroll = Vector2.zero;
                }
                EditorGUI.EndDisabledGroup();

                ClampPageIndex();
                var pageStart = _filtered.Count == 0 ? 0 : _pageIndex * PageSize + 1;
                var pageEnd = Mathf.Min((_pageIndex + 1) * PageSize, _filtered.Count);
                var totalAll = _result != null ? _result.Entries.Count : 0;

                GUILayout.Label(
                    $"Стр. {_pageIndex + 1} / {PageCount}  ·  строки {pageStart}-{pageEnd} из {_filtered.Count}  (всего {totalAll})",
                    EditorStyles.miniLabel);

                EditorGUI.BeginDisabledGroup(_pageIndex >= PageCount - 1);
                if (GUILayout.Button("Next ▶", GUILayout.Width(70)))
                {
                    _pageIndex++;
                    _listScroll = Vector2.zero;
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        void DrawAssetList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.62f)))
            {
                DrawListHeader();

                ClampPageIndex();
                var start = _pageIndex * PageSize;
                var end = Mathf.Min(start + PageSize, _filtered.Count);

                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                for (var i = start; i < end; i++)
                {
                    var entry = _filtered[i];
                    var selected = i == _selectedIndex;
                    var rowInPage = i - start;
                    var bg = selected ? new Color(0.24f, 0.37f, 0.58f, 0.45f)
                        : (rowInPage % 2 == 0 ? new Color(0f, 0f, 0f, 0.08f) : Color.clear);

                    var rect = EditorGUILayout.BeginHorizontal();
                    if (Event.current.type == EventType.Repaint && bg.a > 0f)
                        EditorGUI.DrawRect(rect, bg);

                    if (GUILayout.Button(selected ? "●" : " ", GUILayout.Width(22)))
                        SelectRow(i);

                    GUILayout.Label(entry.PackedSizeReadable, GUILayout.Width(78));
                    GUILayout.Label(entry.SourceLabel, GUILayout.Width(88));
                    var pathStyle = selected ? EditorStyles.whiteLabel : EditorStyles.label;
                    var pathContent = new GUIContent(
                        entry.AssetPath,
                        BuildTooltip(entry));
                    GUILayout.Label(pathContent, pathStyle);

                    if (GUILayout.Button(new GUIContent("Select", "Выделить в окне Project"), GUILayout.Width(56)))
                    {
                        SelectRow(i);
                        PingAsset(entry.AssetPath);
                    }

                    EditorGUILayout.EndHorizontal();

                    if (Event.current.type == EventType.MouseDown
                        && rect.Contains(Event.current.mousePosition)
                        && Event.current.button == 0)
                    {
                        SelectRow(i);
                        Event.current.Use();
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        void ClampPageIndex()
        {
            _pageIndex = Mathf.Clamp(_pageIndex, 0, PageCount - 1);
        }

        void DrawListHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(" ", GUILayout.Width(22));
                GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(78));
                GUILayout.Label("Source", EditorStyles.miniBoldLabel, GUILayout.Width(88));
                GUILayout.Label("Asset Path", EditorStyles.miniBoldLabel);
                GUILayout.Label(" ", GUILayout.Width(56));
            }
        }

        void DrawDetailsPanel()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUILayout.Label("Details", EditorStyles.boldLabel);

                if (_selectedIndex < 0 || _selectedIndex >= _filtered.Count)
                {
                    EditorGUILayout.HelpBox("Выберите ассет слева.", MessageType.None);
                    return;
                }

                var entry = _filtered[_selectedIndex];
                _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);

                EditorGUILayout.LabelField("Path", entry.AssetPath);
                EditorGUILayout.LabelField("Total packed size", entry.PackedSizeReadable);
                if (entry.PlayerPackedSizeBytes > 0)
                    EditorGUILayout.LabelField("  Player part", FullBuildSizeReportEntry.FormatBytes(entry.PlayerPackedSizeBytes));
                if (entry.AddressablesPackedSizeBytes > 0)
                    EditorGUILayout.LabelField("  Addressables part", FullBuildSizeReportEntry.FormatBytes(entry.AddressablesPackedSizeBytes));
                EditorGUILayout.LabelField("Source", entry.SourceLabel);
                EditorGUILayout.LabelField("GUID", string.IsNullOrEmpty(entry.Guid) ? "—" : entry.Guid);

#if USE_ADDRESSABLES
                if ((entry.Source & BuildAssetSource.Addressables) != 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Addressables", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Group", string.IsNullOrEmpty(entry.AddressablesGroup) ? "—" : entry.AddressablesGroup);
                    EditorGUILayout.LabelField("Bundle", string.IsNullOrEmpty(entry.AddressablesBundle) ? "—" : entry.AddressablesBundle);
                    EditorGUILayout.LabelField("Address", string.IsNullOrEmpty(entry.AddressableAddress) ? "—" : entry.AddressableAddress);
                    EditorGUILayout.LabelField("Implicit dep", entry.IsImplicitDependency ? "yes" : "no");
                }
#endif

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select in Project", GUILayout.Height(28)))
                        PingAsset(entry.AssetPath);

                    if (GUILayout.Button("Find Hierarchy Refs", GUILayout.Height(28)))
                    {
                        FullBuildSizeReferenceFinder.FindHierarchyReferences(entry, _result?.Entries);
                        Repaint();
                    }
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
                var summary = FullBuildSizeReferenceFinder.BuildReferenceSummary(entry);
                if (string.IsNullOrEmpty(summary))
                {
                    EditorGUILayout.HelpBox(
                        "Нажмите «Find Hierarchy Refs», чтобы найти цепочки вроде:\n" +
                        "Main1 -> Canvas -> Progress bar -> Image (m_Sprite)",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.TextArea(summary, GUILayout.ExpandHeight(true));
                }

#if USE_ADDRESSABLES
                if (entry.AssetLevelReferences.Count > 0 && entry.HierarchyReferences.Count == 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Quick asset-level refs (from Addressables layout):", EditorStyles.miniBoldLabel);
                    foreach (var path in entry.AssetLevelReferences.Take(12))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.SelectableLabel(path, GUILayout.Height(16));
                            if (GUILayout.Button("Select", GUILayout.Width(56)))
                                PingAsset(path);
                        }
                    }
                }
#endif

                EditorGUILayout.EndScrollView();
            }
        }

        void SelectRow(int index)
        {
            _selectedIndex = index;
            Repaint();
        }

        void RefreshReport()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Full Build Size Report", "Собираю данные...", 0.3f);
                _result = FullBuildSizeReportCollector.Collect();
                ApplyFilter();
                _selectedIndex = -1;
                _pageIndex = 0;
                _status = $"Обновлено {DateTime.Now:HH:mm:ss} · {_result.Entries.Count} ассетов";
            }
            catch (Exception ex)
            {
                _status = "Ошибка: " + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        void ApplyFilter()
        {
            _filtered.Clear();
            if (_result == null)
                return;

            IEnumerable<FullBuildSizeReportEntry> query = _result.Entries;

            switch (_sourceFilter)
            {
                case SourceFilter.Player:
                    query = query.Where(e => (e.Source & BuildAssetSource.Player) != 0);
                    break;
                case SourceFilter.Addressables:
                    query = query.Where(e => (e.Source & BuildAssetSource.Addressables) != 0);
                    break;
                case SourceFilter.Both:
                    query = query.Where(e => e.Source == BuildAssetSource.Both);
                    break;
            }

            if (!string.IsNullOrEmpty(_search))
            {
                var s = _search.Trim();
                query = query.Where(e =>
                    (!string.IsNullOrEmpty(e.AssetPath) && e.AssetPath.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(e.AddressablesGroup) && e.AddressablesGroup.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(e.AddressablesBundle) && e.AddressablesBundle.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(e.AddressableAddress) && e.AddressableAddress.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (_showOnlyLarge)
                query = query.Where(e => e.PackedSizeBytes >= _largeThresholdBytes);

            _filtered.AddRange(query);

            _pageIndex = 0;
            _listScroll = Vector2.zero;
            ClampPageIndex();

            if (_selectedIndex >= _filtered.Count)
                _selectedIndex = -1;
        }

        void ExportCsv()
        {
            if (_result == null || _result.Entries.Count == 0)
            {
                EditorUtility.DisplayDialog("Full Build Size Report", "Сначала нажмите Refresh.", "OK");
                return;
            }

            var path = EditorUtility.SaveFilePanel(
                "Export Full Build Size Report",
                "",
                $"FullBuildSize_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("PackedSizeBytes;PlayerBytes;AddressablesBytes;PackedSize;Source;Path;Group;Bundle;Address;Implicit;AssetRefs");
            foreach (var e in _result.Entries)
            {
                sb.Append(e.PackedSizeBytes).Append(';')
                    .Append(e.PlayerPackedSizeBytes).Append(';')
                    .Append(e.AddressablesPackedSizeBytes).Append(';')
                    .Append(e.PackedSizeReadable).Append(';')
                    .Append(e.SourceLabel).Append(';')
                    .Append(Csv(e.AssetPath)).Append(';')
                    .Append(Csv(e.AddressablesGroup)).Append(';')
                    .Append(Csv(e.AddressablesBundle)).Append(';')
                    .Append(Csv(e.AddressableAddress)).Append(';')
                    .Append(e.IsImplicitDependency ? "1" : "0").Append(';')
                    .Append(Csv(string.Join(" | ", e.AssetLevelReferences)))
                    .AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }

        static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        static string BuildTooltip(FullBuildSizeReportEntry entry)
        {
            if (entry == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine(entry.AssetPath);
            sb.AppendLine($"Size: {entry.PackedSizeReadable}");
            sb.AppendLine($"Source: {entry.SourceLabel}");
            if (!string.IsNullOrEmpty(entry.AddressablesGroup))
                sb.AppendLine($"Group: {entry.AddressablesGroup}");
            if (entry.AssetLevelReferences.Count > 0)
            {
                sb.AppendLine("Referenced by:");
                foreach (var path in entry.AssetLevelReferences.Take(8))
                    sb.AppendLine(" - " + path);
            }

            return sb.ToString().TrimEnd();
        }

        static void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return;

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[BuildSizeExplorer] Не удалось загрузить: {assetPath}");
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            EditorUtility.FocusProjectWindow();
        }
    }
}
