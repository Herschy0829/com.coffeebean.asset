using System.Collections.Generic;
using CoffeeBean.EditorTools;
using NUnit.Framework;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// 打包预检测试（纯逻辑，直接喂记录，不碰工程里的 Addressables 设置）。
    ///
    /// 为什么需要这层预检：默认播放模式是 Use Asset Database (fastest)，
    /// 编辑器里读的是 AssetDatabase、**不经过 bundle 打包**，所以"在编辑器里能跑"
    /// 证明不了资源打得进包。预检不构建就能抓出下面这些配置问题。
    /// </summary>
    public class CAssetContentPreflightTests
    {
        private static CAssetEntryRecord Entry(string address, string path = "Assets/x.prefab",
            string group = "G", bool exists = true)
        {
            return new CAssetEntryRecord { Address = address, AssetPath = path, GroupName = group, AssetExists = exists };
        }

        private static CAssetProfileValueRecord Profile(string name, string raw)
        {
            return new CAssetProfileValueRecord { Name = name, RawValue = raw };
        }

        [Test]
        public void HealthyProject_ReportsNoProblems()
        {
            var entries = new List<CAssetEntryRecord>
            {
                Entry("UI/A", "Assets/A.prefab"),
                Entry("UI/B", "Assets/B.prefab"),
            };
            var report = CAssetContentPreflight.Evaluate(entries, new[] { "G" },
                new[] { Profile("BuildTarget", "Android") });

            Assert.IsFalse(report.HasProblems);
            Assert.AreEqual(2, report.EntryCount);
            Assert.AreEqual(1, report.GroupCount);
            StringAssert.Contains("没有发现", report.Details());
        }

        /// <summary>
        /// Addressables 里 <c>[...]</c> 是**正常语法**：既引用变量，也能写内联 C# 表达式；
        /// <c>{...}</c> 是构建期路径替换。这些都不该被当成"未解析变量"报出来
        /// （第一版就是靠"残留方括号"判断的，结果在真实工程上全是假阳性）。
        /// </summary>
        [Test]
        public void LegitProfileSyntax_IsNotFlagged()
        {
            var values = new[]
            {
                Profile("BuildTarget", "[UnityEditor.EditorUserBuildSettings.activeBuildTarget]"), // 内联 C# 表达式
                Profile("Local.BuildPath", "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]"),
                Profile("Local.LoadPath", "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]"),
                Profile("Remote.BuildPath", "ServerData/[BuildTarget]"),
            };
            var report = CAssetContentPreflight.Evaluate(new List<CAssetEntryRecord>(), new[] { "G" }, values);

            Assert.IsEmpty(report.UnresolvedProfileVariables, "正常语法被误报了：" + string.Join(" | ", report.UnresolvedProfileVariables));
            Assert.IsFalse(report.HasProblems);
        }

        /// <summary>
        /// 拼错的变量名会被 Unity **静默吃掉**（实测：<c>[NotDefinedVariable]/x</c> 解析成
        /// <c>NotDefinedVariable/x</c>，方括号都没了）—— 路径是错的，但编辑器里不报错。
        /// 这才是真正要抓的：裸标识符且不是已定义变量。
        /// </summary>
        [Test]
        public void UndefinedVariableName_IsReported()
        {
            var report = CAssetContentPreflight.Evaluate(
                new List<CAssetEntryRecord>(), new[] { "G" },
                new[]
                {
                    Profile("BuildTarget", "Android"),
                    Profile("Remote.BuildPath", "ServerData/[BuildTargt]"), // 少个 e
                });

            Assert.AreEqual(1, report.UnresolvedProfileVariables.Count);
            StringAssert.Contains("BuildTargt", report.UnresolvedProfileVariables[0]);
            StringAssert.Contains("Remote.BuildPath", report.UnresolvedProfileVariables[0]);
            Assert.IsTrue(report.HasProblems);
        }

        [Test]
        public void DuplicateAddress_IsReportedWithBothGroups()
        {
            var entries = new List<CAssetEntryRecord>
            {
                Entry("UI/Same", "Assets/A.prefab", "G1"),
                Entry("UI/Same", "Assets/B.prefab", "G2"),
            };
            var report = CAssetContentPreflight.Evaluate(entries, new[] { "G1", "G2" }, null);

            Assert.AreEqual(1, report.DuplicateAddresses.Count);
            StringAssert.Contains("UI/Same", report.DuplicateAddresses[0]);
            StringAssert.Contains("G1", report.DuplicateAddresses[0]);
            StringAssert.Contains("G2", report.DuplicateAddresses[0]);
            Assert.IsTrue(report.HasProblems);
        }

        [Test]
        public void EmptyAddress_IsReported()
        {
            var report = CAssetContentPreflight.Evaluate(new[] { Entry(null), Entry(string.Empty) }, new[] { "G" }, null);

            Assert.AreEqual(2, report.EmptyAddresses.Count);
            Assert.IsTrue(report.HasProblems);
        }

        [Test]
        public void DeadEntry_IsReported()
        {
            // 资源被删了但条目还留在 group 里 —— 构建会因此报错
            var report = CAssetContentPreflight.Evaluate(
                new[] { Entry("UI/Gone", "Assets/Gone.prefab", "G", exists: false) }, new[] { "G" }, null);

            Assert.AreEqual(1, report.DeadEntries.Count);
            Assert.IsTrue(report.HasProblems);
        }

        [Test]
        public void EmptyGroup_IsReportedButNotAsProblem()
        {
            var report = CAssetContentPreflight.Evaluate(
                new[] { Entry("UI/A", "Assets/A.prefab", "Used") }, new[] { "Used", "Unused" }, null);

            Assert.AreEqual(1, report.EmptyGroups.Count);
            Assert.AreEqual("Unused", report.EmptyGroups[0]);
            Assert.IsFalse(report.HasProblems, "空组是提示，不算会让打包失败的问题");
        }

        [Test]
        public void NullInputs_DoNotThrow()
        {
            Assert.DoesNotThrow(() => CAssetContentPreflight.Evaluate(null, null, null));
            var report = CAssetContentPreflight.Evaluate(null, null, null);
            Assert.AreEqual(0, report.EntryCount);
            Assert.IsFalse(report.HasProblems);
        }

        [Test]
        public void Details_TruncatesLongLists()
        {
            var entries = new List<CAssetEntryRecord>();
            for (int i = 0; i < CAssetPreflightReport.MaxItemsPerCategory + 7; i++)
            {
                entries.Add(Entry("Dup", "Assets/Dup" + i + ".prefab", "G"));
            }
            var report = CAssetContentPreflight.Evaluate(entries, new[] { "G" }, null);

            string details = report.Details();
            StringAssert.Contains("另有", details);
            Assert.Less(details.Length, 4000, "明细要截断，别把面板/Console 刷爆");
        }
    }
}
