using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 「Addressables 设置」——**CoffeeBean Hub 内嵌面板**（不再是独立窗口）。
    ///
    /// 为什么改成内嵌：
    /// 1. 原来在 Hub 里点导航项只是"选中"，还得在右侧卡片上再点一次「打开」才弹窗口 ——
    ///    两步、而且第一步看起来像没反应。内嵌后面板直接画在 Hub 内容区，一次点击就到位；
    /// 2. 原来窗口里的两个按钮在**工程已经配置好时是静默空转**
    ///    （`EnsureSettings()` 和 `SetPlayModeUseAssetDatabase()` 直接 return），
    ///    用户点了没有任何变化，自然觉得"按钮坏了"。现在：
    ///    · 状态行常显（设置是否存在 / 当前播放模式 / 是否已是编辑器直读）；
    ///    · **无事可做时按钮直接置灰并写明原因**（"已存在，无需创建"）；
    ///    · 每次动作都有结果提示 + Console 日志。
    /// </summary>
    [CoffeeBeanTool("Addressables 设置", "查看/创建 AddressableAssetSettings，一键切到 Use Asset Database（编辑器直读）", "Asset")]
    public static class CAssetSettingsPanel
    {
        private static string _lastResult = string.Empty;
        private static bool _lastOk = true;

        /// <summary>Hub 调用它画面板；<paramref name="requestRepaint"/> 用于动作结束后刷新。</summary>
        public static void DrawTool(Action requestRepaint)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            bool exists = settings != null;
            bool alreadyFast = CAssetSetup.IsUsingAssetDatabasePlayMode();

            EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
            DrawStatusRow("AddressableAssetSettings", exists ? "已存在" : "不存在", exists);
            DrawStatusRow("播放模式",
                exists ? CAssetSetup.DescribePlayMode() + (alreadyFast ? "（编辑器直读 ✅）" : "（非编辑器直读 ⚠）") : "（无）",
                exists && alreadyFast);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

            // 没有可做的事就置灰 —— 置灰的按钮配一句原因，比"能点但没反应"清楚得多
            EditorGUI.BeginDisabledGroup(exists);
            string createLabel = exists
                ? "创建 Addressable 设置（已存在，无需创建）"
                : "创建 Addressable 设置";
            if (GUILayout.Button(createLabel, GUILayout.Height(26)))
            {
                bool created = CAssetSetup.EnsureSettings();
                SetResult(created, created ? "已创建 AddressableAssetSettings" : "设置已存在，未做改动");
                requestRepaint?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!exists || alreadyFast);
            string switchLabel = !exists
                ? "切换为 Use Asset Database（需先创建设置）"
                : alreadyFast
                    ? "切换为 Use Asset Database（已是该模式，无需切换）"
                    : "切换为 Use Asset Database（编辑器直读）";
            if (GUILayout.Button(switchLabel, GUILayout.Height(26)))
            {
                bool changed = CAssetSetup.SetPlayModeUseAssetDatabase();
                SetResult(changed, changed ? "已切换为 Use Asset Database" : "已是 Use Asset Database，未做改动");
                requestRepaint?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(_lastResult, _lastOk ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Use Asset Database 播放模式让编辑器 / EditMode 测试直接读 AssetDatabase 资源，不需要先 Build Content。\n" +
                "打包出的播放器不受这里影响；正式出包仍按 Addressables 的 Build 流程走。",
                MessageType.None);
        }

        private static void SetResult(bool ok, string message)
        {
            _lastOk = ok;
            _lastResult = (ok ? "刚刚：" : "无需操作：") + message;
            // Console 里也留一条，便于回溯
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
