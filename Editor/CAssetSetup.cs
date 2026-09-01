using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// Addressables 设置初始化：模块首次进入编辑器时，若工程没有 AddressableAssetSettings，
    /// 自动创建默认设置（AddressableAssetsData 目录），保证运行时 Addressables.InitializeAsync 可用。
    /// 测试 group（CoffeeBeanTestAssets）由测试代码按需创建，避免污染正式工程。
    /// </summary>
    [InitializeOnLoad]
    public static class CAssetSetup
    {
        static CAssetSetup()
        {
            // 延迟到首个编辑器帧执行，避免 InitializeOnLoad 阶段创建设置冲突
            EditorApplication.delayCall += EnsureSettings;
        }

        /// <summary>菜单：手动确保 Addressables 设置存在。</summary>
        [MenuItem("Window/CoffeeBean/Asset/确保 Addressables 设置")]
        public static void EnsureSettingsMenu()
        {
            EnsureSettings();
            AssetDatabase.Refresh();
        }

        /// <summary>确保 AddressableAssetSettings 存在（没有则创建默认）。</summary>
        public static void EnsureSettings()
        {
            if (AddressableAssetSettingsDefaultObject.Settings != null) return;

            const string path = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(path);
            if (settings == null)
            {
                // 创建默认设置（自动生成 AddressableAssetsData 目录与默认 group）
                settings = AddressableAssetSettings.Create(path, "AddressableAssetSettings", true, true);
            }
            AddressableAssetSettingsDefaultObject.Settings = settings;
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 切换 Addressables 播放模式为 Use Asset Database（BuildScriptFastMode）：
        /// EditMode 测试/编辑器预览无需构建内容即可直接加载 AssetDatabase 资源。
        /// </summary>
        public static void SetPlayModeUseAssetDatabase()
        {
            EnsureSettings();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                if (settings.DataBuilders[i] is UnityEditor.AddressableAssets.Build.DataBuilders.BuildScriptFastMode)
                {
                    if (settings.ActivePlayModeDataBuilderIndex != i)
                    {
                        settings.ActivePlayModeDataBuilderIndex = i;
                        AssetDatabase.SaveAssets();
                    }
                    return;
                }
            }
        }

        /// <summary>获取或创建指定名称的 Addressable group。</summary>
        public static AddressableAssetGroup GetOrCreateGroup(string groupName)
        {
            EnsureSettings();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                group = settings.CreateGroup(groupName, false, false, false, null);
            }
            return group;
        }
    }
}
