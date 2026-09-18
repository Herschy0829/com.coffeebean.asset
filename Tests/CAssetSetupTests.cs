using System.Collections.Generic;
using CoffeeBean.EditorTools;
using NUnit.Framework;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// Addressables 设置助手测试。
    ///
    /// 来历：设置窗口那两个按钮在**工程已经配置好时是静默空转**
    /// （<c>EnsureSettings()</c> 与 <c>SetPlayModeUseAssetDatabase()</c> 直接 return），
    /// 用户点了界面和日志都没有任何变化 —— 报"点了没反应"。
    /// 现在两个方法都返回"这次到底改了什么"，界面据此显示结果 / 置灰按钮，这里锁住这个契约。
    /// </summary>
    public class CAssetSetupTests
    {
        [Test]
        public void EnsureSettings_IsIdempotent_AndSecondCallReportsNoChange()
        {
            bool first = CAssetSetup.EnsureSettings();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.IsNotNull(settings, "调用后必须有 AddressableAssetSettings");

            // 第一次可能创建成功（false 表示工程本来就有），第二次**必须**是"无改动"
            if (first) Assert.IsTrue(CAssetSetup.IsUsingAssetDatabasePlayMode() || !CAssetSetup.IsUsingAssetDatabasePlayMode());

            Assert.IsFalse(CAssetSetup.EnsureSettings(),
                "设置已存在时再调一次应返回 false —— 界面据此显示「无需操作」，而不是静默什么都不做");
        }

        [Test]
        public void DescribePlayMode_ReturnsSomethingUsable()
        {
            CAssetSetup.EnsureSettings();
            Assert.IsNotEmpty(CAssetSetup.DescribePlayMode());

            // 描述与判定必须自洽
            bool fast = CAssetSetup.IsUsingAssetDatabasePlayMode();
            string name = CAssetSetup.DescribePlayMode();
            if (fast) StringAssert.Contains("Database", name);
        }

        [Test]
        public void SetPlayModeUseAssetDatabase_ReportsChangeThenNoChange()
        {
            CAssetSetup.EnsureSettings();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.IsNotNull(settings);

            int original = settings.ActivePlayModeDataBuilderIndex;
            int otherIndex = FindNonFastModeIndex(settings);
            if (otherIndex < 0) Assert.Ignore("只有 FastMode 一个 builder，无法验证「切换」分支");

            try
            {
                // 先切到"非编辑器直读"的模式，构造出"需要切换"的状态
                settings.ActivePlayModeDataBuilderIndex = otherIndex;
                Assert.IsFalse(CAssetSetup.IsUsingAssetDatabasePlayMode());

                Assert.IsTrue(CAssetSetup.SetPlayModeUseAssetDatabase(), "从非 FastMode 切过去应返回 true（=发生了改动）");
                Assert.IsTrue(CAssetSetup.IsUsingAssetDatabasePlayMode());

                Assert.IsFalse(CAssetSetup.SetPlayModeUseAssetDatabase(), "已经是该模式时再切应返回 false");
            }
            finally
            {
                settings.ActivePlayModeDataBuilderIndex = original;
            }
        }

        private static int FindNonFastModeIndex(AddressableAssetSettings settings)
        {
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                if (!(settings.DataBuilders[i] is BuildScriptFastMode)) return i;
            }
            return -1;
        }
    }
}
