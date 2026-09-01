using UnityEditor;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// Addressables 设置工具窗口（入口：Window &gt; CoffeeBean 收敛到 CoffeeBean Hub）：
    /// 一键确保 AddressableAssetSettings 存在、切换播放模式为 Use Asset Database（编辑器直读资源）。
    /// </summary>
    [CoffeeBeanTool("Addressables 设置", "确保 AddressableAssetSettings 存在；切换播放模式（Use Asset Database）", "Asset")]
    public sealed class CAssetSettingsWindow : EditorWindow
    {
        // 入口统一收敛到 CoffeeBean Hub（Window > CoffeeBean），不再单独注册菜单项（避免子菜单抢占主入口）
        public static void Open() => GetWindow<CAssetSettingsWindow>("Addressables 设置");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Addressables 设置", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            EditorGUILayout.LabelField("Settings 存在:", settings != null ? "✅ 是" : "❌ 否");

            string playMode = settings?.ActivePlayModeDataBuilder?.Name ?? "（无）";
            EditorGUILayout.LabelField("播放模式:", playMode);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("确保设置存在", GUILayout.Height(30)))
            {
                CAssetSetup.EnsureSettings();
                AssetDatabase.Refresh();
                Repaint();
            }

            if (GUILayout.Button("切换为 Use Asset Database（编辑器直读）", GUILayout.Height(30)))
            {
                CAssetSetup.SetPlayModeUseAssetDatabase();
                AssetDatabase.SaveAssets();
                Repaint();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "设置通常由模块自动创建（首次进入编辑器）；此窗口用于手动管理。\n" +
                "Use Asset Database 播放模式让编辑器/测试直接读 AssetDatabase 资源，无需构建内容。",
                MessageType.Info);
        }
    }
}
