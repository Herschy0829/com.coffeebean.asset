using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 「Addressables 设置」——**CoffeeBean Hub 内嵌面板**（不再是独立窗口）。
    ///
    /// 三段内容，对应"编辑器里怎么确认资源真能打进包"这件事：
    /// 1. **状态**：设置是否存在、当前播放模式；
    /// 2. **播放模式切换**：`Use Asset Database (fastest)` 读 AssetDatabase（快，但**不经过打包**）
    ///    ⇄ `Use Existing Build` 读真实构建产物（与真机一致）；
    /// 3. **打包预检 + 真实构建**：预检不构建、秒级抓"编辑器能跑、打包会炸"的配置问题；
    ///    Build Content 出真产物，之后切 `Use Existing Build` 就能在编辑器里按真机方式验证。
    ///
    /// 为什么要有第 2、3 段：默认播放模式是 FastMode，编辑器里根本不经过 bundle 打包，
    /// 所以"在编辑器里跑一遍"**不能**证明资源打得进包 —— 这是最容易踩的坑。
    /// </summary>
    [CoffeeBeanTool("Addressables 设置", "播放模式切换（FastMode / 真实构建产物）、打包预检、一键构建内容", "Asset")]
    public static class CAssetSettingsPanel
    {
        private static string _lastResult = string.Empty;
        private static bool _lastOk = true;
        private static CAssetPreflightReport _lastReport;
        private static string _lastBuildSummary = string.Empty;

        /// <summary>Hub 调用它画面板；<paramref name="requestRepaint"/> 用于动作结束后刷新。</summary>
        public static void DrawTool(Action requestRepaint)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            bool exists = settings != null;

            DrawStateSection(settings, exists);
            EditorGUILayout.Space(10);
            DrawPlayModeSection(settings, exists, requestRepaint);
            EditorGUILayout.Space(10);
            DrawPreflightSection(settings, exists, requestRepaint);
            EditorGUILayout.Space(10);
            DrawBuildSection(settings, exists, requestRepaint);

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_lastResult, _lastOk ? MessageType.Info : MessageType.Warning);
            }
        }

        // ========== 状态 ==========

        private static void DrawStateSection(AddressableAssetSettings settings, bool exists)
        {
            EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
            bool fast = CAssetSetup.IsUsingAssetDatabasePlayMode();
            DrawStatusRow("AddressableAssetSettings", exists ? "已存在" : "不存在", exists);
            DrawStatusRow("播放模式",
                exists ? CAssetSetup.DescribePlayMode() + (fast ? "（编辑器直读）" : "（读真实构建产物）") : "（无）",
                exists);

            EditorGUI.BeginDisabledGroup(exists);
            string createLabel = exists
                ? "创建 Addressable 设置（已存在，无需创建）"
                : "创建 Addressable 设置";
            if (GUILayout.Button(createLabel, GUILayout.Height(26)))
            {
                bool created = CAssetSetup.EnsureSettings();
                SetResult(created, created ? "已创建 AddressableAssetSettings" : "设置已存在，未做改动");
            }
            EditorGUI.EndDisabledGroup();
        }

        // ========== 播放模式 ==========

        private static void DrawPlayModeSection(AddressableAssetSettings settings, bool exists, Action requestRepaint)
        {
            EditorGUILayout.LabelField("播放模式（决定编辑器里跑的是哪一套）", EditorStyles.boldLabel);

            if (!exists)
            {
                EditorGUILayout.LabelField("（先创建设置）", EditorStyles.miniLabel);
                return;
            }

            bool fast = CAssetSetup.IsUsingAssetDatabasePlayMode();
            EditorGUILayout.LabelField(fast
                    ? "· Use Asset Database (fastest)：读 AssetDatabase，最快，但**不经过 bundle 打包** —— 不能证明资源打得进包。"
                    : "· Use Existing Build：读真实构建产物，与真机一致 —— 验证打包结果的正解（需先构建内容）。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(fast);
            if (GUILayout.Button(fast ? "已是 FastMode" : "切到 FastMode（日常开发）", GUILayout.Height(24)))
            {
                SetResult(CAssetSetup.SetPlayModeFast(), "已切到 Use Asset Database (fastest)");
                requestRepaint?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!fast);
            if (GUILayout.Button(fast ? "切到 Use Existing Build（验证打包）" : "已是 Use Existing Build", GUILayout.Height(24)))
            {
                SetResult(CAssetSetup.SetPlayModeExistingBuild(),
                    "已切到 Use Existing Build —— 如果还没构建过内容，请先在下方的「构建内容」里构建");
                requestRepaint?.Invoke();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        // ========== 打包预检 ==========

        private static void DrawPreflightSection(AddressableAssetSettings settings, bool exists, Action requestRepaint)
        {
            EditorGUILayout.LabelField("打包预检（不构建，秒级）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "抓那些「编辑器里能跑、打包后才炸」的配置问题：重复/空地址、资源被删但条目还在、Profile 变量没解析。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!exists);
            if (GUILayout.Button("运行预检", GUILayout.Width(90)))
            {
                _lastReport = CAssetContentPreflight.Run(settings);
                Debug.Log("[CoffeeBean.Asset] 打包预检\n" + _lastReport.Details());
                _lastResult = _lastReport.Details();
                _lastOk = !_lastReport.HasProblems;
                requestRepaint?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            // Unity 自带的深度静态分析（Bundle Layout / 重复依赖），不重复造轮子
            if (GUILayout.Button("打开 Addressables Analyze", GUILayout.Width(190)))
            {
                EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Analyze");
            }
            if (GUILayout.Button("打开 Groups", GUILayout.Width(110)))
            {
                EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Groups");
            }
            EditorGUILayout.EndHorizontal();

            if (_lastReport != null)
            {
                EditorGUILayout.LabelField(_lastReport.Summary, EditorStyles.miniLabel);
            }

            EditorGUILayout.HelpBox(
                "更细的检查在 Analyze 窗口里（Unity 自带规则）：\n" +
                "· Check Bundle Dupe Dependencies —— 同一个依赖被打进多个 bundle（内存翻倍）；\n" +
                "· Check Resources to Addressable Duplicate Dependencies —— Resources 与 Addressables 重复；\n" +
                "· Bundle Layout Preview —— 预览「哪些资源会进哪个 bundle」（不构建也能看）。",
                MessageType.None);
        }

        // ========== 真实构建 ==========

        private static void DrawBuildSection(AddressableAssetSettings settings, bool exists, Action requestRepaint)
        {
            EditorGUILayout.LabelField("构建内容（真实打包）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "出包前必做。构建后切到 Use Existing Build，就能在编辑器里按真机方式跑一遍" +
                "（地址解析、bundle 加载、着色器全走构建产物）。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginDisabledGroup(!exists);
            if (GUILayout.Button("Build Content（New Build）", GUILayout.Height(28)))
            {
                if (!EditorUtility.DisplayDialog("构建 Addressables 内容",
                        "构建期间编辑器会阻塞（Unity 的 Addressables 构建是同步的，官方菜单也一样）。\n\n继续？",
                        "开始构建", "取消"))
                {
                    return;
                }

                bool ok = CAssetSetup.BuildContent(out string summary);
                _lastBuildSummary = summary;
                SetResult(ok, summary);
                requestRepaint?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_lastBuildSummary))
            {
                EditorGUILayout.LabelField(_lastBuildSummary, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.HelpBox(
                "等价的官方入口（结果一样）：Window > Asset Management > Addressables > Groups → Build > New Build。\n" +
                "构建后想验证真机行为：切到 Use Existing Build 再运行；远程内容还要记得 catalog 与 bundle 都上传到位。",
                MessageType.None);
        }

        // ========== 小工具 ==========

        private static void SetResult(bool ok, string message)
        {
            _lastOk = ok;
            _lastResult = (ok ? "刚刚：" : "无需操作：") + message;
            Debug.Log($"[CoffeeBean.Asset] {_lastResult}");
        }

        private static void DrawStatusRow(string label, string value, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(170));
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = ok ? new Color(0.35f, 0.8f, 0.4f) : new Color(0.9f, 0.6f, 0.2f);
            EditorGUILayout.LabelField(value, style);
            EditorGUILayout.EndHorizontal();
        }
    }
}
