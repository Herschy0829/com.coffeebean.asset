using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// Addressables 设置初始化。
    ///
    /// **返回值 = "这次到底改了什么"**：这些操作在工程已经配置好时天然是空转，
    /// 以前是静默 return，于是界面上"点了没反应"。现在调用方可以据此给出明确反馈。
    ///
    /// **2026-09-18 加硬防线**：`EnsureSettings()` 曾经是"`Settings == null` 就创建"，
    /// 而 `AddressableAssetSettings.Create()` 会**覆写同名资源** —— 在"包更新 → 全量重导入"
    /// 那种窗口期里，`Settings` 与 `LoadAssetAtPath` 都可能瞬时为 null，于是它有把工程里
    /// 已有的 Addressables 设置（连同 77 个 group 的引用）覆盖掉的风险。
    /// 现在三条防线：
    /// 1. **磁盘上文件存在就绝不创建**（`File.Exists`，不看加载结果）；
    /// 2. **正在导入/编译时直接跳过**，不猜；
    /// 3. 只有文件**真的不存在**时才创建。
    /// </summary>
    [InitializeOnLoad]
    public static class CAssetSetup
    {
        private const string Tag = "CoffeeBean.Asset";
        private const string SettingsPath = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
        private static bool _warnedSettingsNotLoaded;

        static CAssetSetup()
        {
            // 延迟到首个编辑器帧执行，避免 InitializeOnLoad 阶段创建设置冲突
            // 注意：EnsureSettings 现在有返回值，方法组不能直接转成 CallbackFunction，必须包一层 lambda
            EditorApplication.delayCall += () => EnsureSettings();
        }

        /// <summary>
        /// 确保 AddressableAssetSettings 存在。
        ///
        /// **只在设置资源真的不存在时才创建**；磁盘上已有文件时一律不动它
        /// （哪怕这一刻没能加载出来 —— 那通常是导入中，创建只会把用户已有配置覆盖掉）。
        /// </summary>
        /// <returns>true = 这次真的创建了；false = 未做任何改动。</returns>
        public static bool EnsureSettings()
        {
            if (AddressableAssetSettingsDefaultObject.Settings != null) return false;

            // 防线 2：导入/编译期间不判断、不创建（这段窗口里 Settings 可能瞬时为 null）
            if (EditorApplication.isUpdating || EditorApplication.isCompiling) return false;

            // 防线 1：文件在磁盘上存在 → 绝不创建（可能只是还没加载出来）
            if (File.Exists(SettingsPath))
            {
                if (!_warnedSettingsNotLoaded)
                {
                    _warnedSettingsNotLoaded = true;
                    Debug.LogWarning($"[{Tag}] {SettingsPath} 存在但当前没能加载出来（可能正在导入/编译），" +
                                     "已跳过创建 —— 框架绝不能覆盖工程里已有的 Addressables 设置。");
                }
                return false;
            }

            // 防线 3：确实没有 → 创建默认设置（自动生成 AddressableAssetsData 目录与默认 group）
            AddressableAssetSettings settings =
                AddressableAssetSettings.Create(SettingsPath, "AddressableAssetSettings", true, true);
            AddressableAssetSettingsDefaultObject.Settings = settings;
            AssetDatabase.SaveAssets();

            Debug.Log($"[{Tag}] 已创建 Addressable 设置：{SettingsPath}");
            return true;
        }

        /// <summary>
        /// 切换 Addressables 播放模式为 Use Asset Database（BuildScriptFastMode）：
        /// EditMode 测试/编辑器预览无需构建内容即可直接加载 AssetDatabase 资源。
        /// </summary>
        /// <returns>true = 这次真的切换了；false = 本来就是该模式 / 未找到该模式（未做改动）。</returns>
        public static bool SetPlayModeUseAssetDatabase()
        {
            return SetPlayMode<BuildScriptFastMode>();
        }

        /// <summary>
        /// 切到 **Use Existing Build**（BuildScriptPackedPlayMode）：读**真实构建产物**，
        /// 与真机行为一致 —— 这是"验证资源是否真的能打进包"的正解（需先构建过内容）。
        /// </summary>
        /// <returns>true = 这次真的切换了。</returns>
        public static bool SetPlayModeExistingBuild()
        {
            return SetPlayMode<BuildScriptPackedPlayMode>();
        }

        /// <summary>切到 FastMode（等价于 <see cref="SetPlayModeUseAssetDatabase"/>，名字更直白）。</summary>
        public static bool SetPlayModeFast()
        {
            return SetPlayMode<BuildScriptFastMode>();
        }

        private static bool SetPlayMode<TBuilder>() where TBuilder : ScriptableObject
        {
            EnsureSettings();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.DataBuilders == null) return false;

            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                if (!(settings.DataBuilders[i] is TBuilder)) continue;
                if (settings.ActivePlayModeDataBuilderIndex == i) return false; // 已经是了

                settings.ActivePlayModeDataBuilderIndex = i;
                AssetDatabase.SaveAssets();
                // DataBuilders[i] 是 ScriptableObject（不是 IDataBuilder），名字用 Object.name
                Debug.Log($"[{Tag}] 播放模式已切换为 {settings.DataBuilders[i].name}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 构建 Addressables 内容（等价于官方菜单 <c>Groups → Build → New Build</c>）。
        ///
        /// **同步阻塞** —— Unity 的 Addressables 构建本身就是同步的，官方菜单也一样会卡住编辑器。
        /// 产出 catalog + bundle；构建完成后切到 Use Existing Build 即可在编辑器里按真机方式验证。
        /// </summary>
        /// <param name="summary">给界面/日志看的一句话结果。</param>
        /// <returns>true = 构建成功（无 Error）。</returns>
        public static bool BuildContent(out string summary)
        {
            EnsureSettings();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                summary = "没有 AddressableAssetSettings，无法构建。";
                return false;
            }

            try
            {
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                bool ok = result != null && string.IsNullOrEmpty(result.Error);
                summary = ok
                    ? $"构建成功：{result.LocationCount} 个 location，耗时 {result.Duration:F1} 秒；输出 {result.OutputPath}"
                    : $"构建失败：{(result == null ? "构建未返回结果" : result.Error)}";

                if (ok) Debug.Log($"[{Tag}] {summary}");
                else Debug.LogError($"[{Tag}] {summary}");
                return ok;
            }
            catch (Exception e)
            {
                summary = "构建抛出异常：" + e.Message;
                Debug.LogError($"[{Tag}] {summary}\n{e}");
                return false;
            }
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
