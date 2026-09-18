using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// Addressables 设置初始化：模块首次进入编辑器时，若工程没有 AddressableAssetSettings，
    /// 自动创建默认设置（AddressableAssetsData 目录），保证运行时 Addressables.InitializeAsync 可用。
    /// 测试 group（CoffeeBeanTestAssets）由测试代码按需创建，避免污染正式工程。
    ///
    /// **返回值 = "这次到底改了什么"**：这些操作在工程已经配置好时天然是空转，
    /// 以前是静默 return，于是界面上"点了没反应"。现在调用方可以据此给出明确反馈。
    /// </summary>
    [InitializeOnLoad]
    public static class CAssetSetup
    {
        private const string Tag = "CoffeeBean.Asset";
        private const string SettingsPath = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";

        static CAssetSetup()
        {
            // 延迟到首个编辑器帧执行，避免 InitializeOnLoad 阶段创建设置冲突
            // 注意：EnsureSettings 现在有返回值，方法组不能直接转成 CallbackFunction，必须包一层 lambda
            EditorApplication.delayCall += () => EnsureSettings();
        }

        /// <summary>
        /// 确保 AddressableAssetSettings 存在（没有则创建默认）。
        /// </summary>
        /// <returns>true = 这次真的创建了；false = 本来就存在（未做任何改动）。</returns>
        public static bool EnsureSettings()
        {
            if (AddressableAssetSettingsDefaultObject.Settings != null) return false;

            var settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(SettingsPath);
            bool created = settings == null;
            if (created)
            {
                // 创建默认设置（自动生成 AddressableAssetsData 目录与默认 group）
                settings = AddressableAssetSettings.Create(SettingsPath, "AddressableAssetSettings", true, true);
            }
            AddressableAssetSettingsDefaultObject.Settings = settings;
            AssetDatabase.SaveAssets();

            if (created)
            {
                Debug.Log($"[{Tag}] 已创建 Addressable 设置：{SettingsPath}");
            }
            return created;
        }

        /// <summary>
        /// 切换 Addressables 播放模式为 Use Asset Database（BuildScriptFastMode）：
        /// EditMode 测试/编辑器预览无需构建内容即可直接加载 AssetDatabase 资源。
        /// </summary>
        /// <returns>true = 这次真的切换了；false = 本来就是该模式 / 未找到该模式（未做改动）。</returns>
        public static bool SetPlayModeUseAssetDatabase()
        {
            EnsureSettings();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.DataBuilders == null) return false;

            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                if (!(settings.DataBuilders[i] is BuildScriptFastMode)) continue;
                if (settings.ActivePlayModeDataBuilderIndex == i) return false; // 已经是了

                settings.ActivePlayModeDataBuilderIndex = i;
                AssetDatabase.SaveAssets();
                // DataBuilders[i] 是 ScriptableObject（不是 IDataBuilder），名字用 Object.name
                Debug.Log($"[{Tag}] 播放模式已切换为 Use Asset Database（{settings.DataBuilders[i].name}）");
                return true;
            }
            return false;
        }

        /// <summary>当前播放模式名（拿不到时返回 "（无）"）。</summary>
        public static string DescribePlayMode()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.ActivePlayModeDataBuilder == null) return "（无）";
            return settings.ActivePlayModeDataBuilder.Name;
        }

        /// <summary>当前播放模式是不是 Use Asset Database（BuildScriptFastMode）。</summary>
        public static bool IsUsingAssetDatabasePlayMode()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.ActivePlayModeDataBuilder == null) return false;
            return settings.ActivePlayModeDataBuilder is BuildScriptFastMode;
        }

        /// <summary>获取或创建指定名称的 Addressable group。</summary>
        public static AddressableAssetGroup GetOrCreateGroup(string groupName)
        {
            EnsureSettings();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetGroup group = settings.FindGroup(groupName);
            if (group == null)
            {
                group = settings.CreateGroup(groupName, false, false, false, null);
            }
            return group;
        }
    }
}
