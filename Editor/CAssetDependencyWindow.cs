using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 资源依赖分析（入口：Window &gt; CoffeeBean → Asset · 资源依赖分析）：
    /// 选中一个资源，看它**依赖谁**（正向）与**谁引用了它**（反向）。
    ///
    /// 三个曾经让人"点了没反应"的点，这里都改掉了：
    /// 1. **扫描不再阻塞界面**：反向引用原来在 OnGUI 里全工程扫描（5000 资源 ≈ 6.8 s，
    ///    而 OnGUI 每帧跑两次）→ 现在交给 <see cref="CAssetReferenceScanner"/> 分帧增量执行，
    ///    有进度、可取消、结果边扫边出；
    /// 2. **自动用当前选中**：打开窗口时若 Project 里选了资源就直接分析（可关掉跟随），
    ///    不用再手动拖；
    /// 3. **每个动作都有反馈**：状态行写清"在扫什么 / 扫了多少 / 花了多久 / 命中几条"。
    /// </summary>
    [CoffeeBeanTool("资源依赖分析", "查看选中资源依赖谁、谁引用了它（自动跟随 Project 选中，可取消扫描）", "Asset")]
    public sealed class CAssetDependencyWindow : EditorWindow
    {
        private const string FollowPrefKey = "CoffeeBean.Asset.FollowSelection";

        private Object _target;
        private Vector2 _scroll;
        private string _search = string.Empty;
        private bool _followSelection = true;
        private bool _showDependencies = true;
        private bool _showReferencers = true;

        private readonly CAssetReferenceScanner _scanner = new CAssetReferenceScanner();
        private string _status = "选一个资源开始分析。";
        private bool _hooked;

        private void OnEnable()
        {
            _followSelection = EditorPrefs.GetBool(FollowPrefKey, true);
            SyncTargetFromSelection();
        }

        private void OnDisable()
        {
            Unhook();
            _scanner.Cancel();
        }

        private void OnSelectionChange()
        {
            if (!_followSelection) return;
            if (SyncTargetFromSelection()) Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("资源依赖分析", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "看一个资源依赖谁（正向）、谁引用了它（反向）。反向引用要扫全工程，分帧进行、可随时取消。",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            DrawTargetRow();
            EditorGUILayout.Space(4);

            string path = GetTargetPath();
            if (string.IsNullOrEmpty(path))
            {
                EditorGUILayout.HelpBox(
                    "请在 Project 里选中一个 Assets 下的资源（Prefab / Material / Texture / ScriptableObject…），" +
                    "或把资源拖到上面的字段里。",
                    MessageType.Info);
                return;
            }

            DrawScanControls(path);
            EditorGUILayout.Space(6);

            _search = EditorGUILayout.TextField("过滤:", _search);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            _showReferencers = EditorGUILayout.Foldout(_showReferencers,
                $"被引用（{CountMatching(_scanner.Referencers)}）", true);
            if (_showReferencers)
            {
                DrawList(_scanner.Referencers, "（没有资源引用它 —— 可能只在场景实例里、或运行时按地址加载）");
            }

            EditorGUILayout.Space(8);

            _showDependencies = EditorGUILayout.Foldout(_showDependencies,
                $"正向依赖（{CountMatching(_scanner.Dependencies)}）", true);
            if (_showDependencies)
            {
                DrawList(_scanner.Dependencies, "（它不依赖任何 Assets 下的资源）");
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(_status, EditorStyles.helpBox);
        }

        // ========== 目标选择 ==========

        private void DrawTargetRow()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _target = EditorGUILayout.ObjectField("目标资源", _target, typeof(Object), false);
            if (EditorGUI.EndChangeCheck())
            {
                // 手动改目标 = 明确指定，取消跟随，免得下一次点选又被覆盖
                if (_target != null)
                {
                    _followSelection = false;
                    EditorPrefs.SetBool(FollowPrefKey, false);
                }
            }

            var followStyle = new GUIStyle(EditorStyles.miniButton) { fixedWidth = 110 };
            bool newFollow = GUILayout.Toggle(_followSelection, "跟随 Project 选中", followStyle);
            if (newFollow != _followSelection)
            {
                _followSelection = newFollow;
                EditorPrefs.SetBool(FollowPrefKey, newFollow);
                if (newFollow) SyncTargetFromSelection();
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool SyncTargetFromSelection()
        {
            Object active = Selection.activeObject;
            if (active == null) return false;
            if (!(active is GameObject) && !(active is Component) && !AssetDatabase.Contains(active))
            {
                // 场景里的对象也允许（AssetDatabase.Contains == false 但仍是合法目标）
            }
            if (ReferenceEquals(active, _target)) return false;
            _target = active;
            return true;
        }

        private string GetTargetPath()
        {
            if (_target == null) return null;
            string path = AssetDatabase.GetAssetPath(_target);
            if (string.IsNullOrEmpty(path)) return null;
            if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox("请选 Assets 目录下的资源（Package / 场景对象没有可分析的反向引用）。",
                    MessageType.Warning);
                return null;
            }
            return path;
        }

        // ========== 扫描控制 ==========

        private void DrawScanControls(string path)
        {
            EditorGUILayout.LabelField("目标: " + path, EditorStyles.miniLabel);

            if (_scanner.IsResultFor(path))
            {
                // 结果已就绪：只显示，不重扫
            }
            else if (!_scanner.IsRunning)
            {
                // 目标变了或还没扫过 → 启动（这一步不会阻塞：真正的活在 update 里分帧做）
                _scanner.Begin(path);
                Hook();
            }

            if (_scanner.IsRunning)
            {
                EditorGUILayout.Space(2);
                Rect rect = GUILayoutUtility.GetRect(18, 18, "TextField");
                EditorGUI.ProgressBar(rect, _scanner.Progress,
                    $"扫描中 {_scanner.ScannedCount}/{_scanner.TotalCount}");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("取消扫描", GUILayout.Width(90)))
                {
                    _scanner.Cancel();
                    Unhook();
                    _status = $"已取消（扫到 {_scanner.ScannedCount}/{_scanner.TotalCount}，结果不完整）。";
                    RepaintAllWindows();
                }
                EditorGUILayout.LabelField("扫描期间界面保持可交互；结果会边扫边出。", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("重新分析", GUILayout.Width(90)))
                {
                    _scanner.Begin(path);
                    Hook();
                    _status = "重新扫描中…";
                }
                EditorGUILayout.LabelField(
                    $"共 {_scanner.TotalCount} 个候选资源，耗时 {_scanner.ElapsedMilliseconds:F0} ms", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>把"每帧推进一小段"挂到编辑器更新循环上（**不是** OnGUI）。</summary>
        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            EditorApplication.update += Tick;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            EditorApplication.update -= Tick;
        }

        private void Tick()
        {
            if (!_scanner.IsRunning)
            {
                Unhook();
                if (_scanner.IsDone)
                {
                    _status = $"扫描完成：{_scanner.Referencers.Count} 个资源引用了它，耗时 {_scanner.ElapsedMilliseconds:F0} ms。";
                }
                RepaintAllWindows();
                return;
            }

            _scanner.Step();
            RepaintAllWindows();

            if (!_scanner.IsRunning && _scanner.IsDone)
            {
                _status = $"扫描完成：{_scanner.Referencers.Count} 个资源引用了它，耗时 {_scanner.ElapsedMilliseconds:F0} ms。";
                Unhook();
            }
        }

        private static void RepaintAllWindows()
        {
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<CAssetDependencyWindow>())
            {
                window.Repaint();
            }
        }

        // ========== 列表 ==========

        private int CountMatching(List<string> items)
        {
            if (items == null) return 0;
            if (string.IsNullOrEmpty(_search)) return items.Count;
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (Matches(items[i])) n++;
            }
            return n;
        }

        private bool Matches(string path)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return path.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawList(List<string> items, string emptyHint)
        {
            if (items == null || items.Count == 0)
            {
                EditorGUILayout.LabelField(emptyHint, EditorStyles.miniLabel);
                return;
            }

            int shown = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!Matches(items[i])) continue;
                shown++;
                string path = items[i];

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("选中", GUILayout.Width(48))) SelectAsset(path);
                EditorGUILayout.LabelField(path);
                EditorGUILayout.EndHorizontal();
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField($"（{items.Count} 条，但都被过滤词排除了）", EditorStyles.miniLabel);
            }
        }

        private static void SelectAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[CoffeeBean.Asset] 资源已不存在或无法加载：{path}");
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
