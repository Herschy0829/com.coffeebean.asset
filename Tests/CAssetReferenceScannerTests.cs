using System.Collections.Generic;
using CoffeeBean.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// 反向依赖扫描器测试。
    ///
    /// 来历：这段逻辑原来直接写在 <c>CAssetDependencyWindow.OnGUI</c> 里做全工程扫描 ——
    /// 5000+ 资源实测约 6.8 秒，而 OnGUI 每帧至少跑两次 → 窗口一打开就永久卡在扫描里，
    /// 表现为"点按钮没反应"。现在改成可单测的**分帧增量**扫描器，这里锁住它的行为：
    /// 能真的找到引用者、是分帧的、能取消、同一目标不重扫、非法路径立即结束。
    /// </summary>
    public class CAssetReferenceScannerTests
    {
        private const string Folder = "Assets/__CoffeeBeanScannerTests";
        private const string MaterialPath = Folder + "/Target.mat";
        private const string PrefabPath = Folder + "/Referencer.prefab";
        private const string LonePath = Folder + "/Lone.mat";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "__CoffeeBeanScannerTests");

            Shader shader = Shader.Find("Sprites/Default");
            Assert.IsNotNull(shader, "找不到内置 Shader（Sprites/Default），测试无法构造引用关系");

            var target = new Material(shader) { name = "Target" };
            AssetDatabase.CreateAsset(target, MaterialPath);

            // 无关资源：不该出现在结果里
            var lone = new Material(shader) { name = "Lone" };
            AssetDatabase.CreateAsset(lone, LonePath);

            // 引用 target 的预制体（MeshRenderer.sharedMaterial 会写进 prefab 的序列化数据）
            var go = new GameObject("Referencer");
            go.AddComponent<MeshRenderer>().sharedMaterial = target;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Scan_FindsTheReferencingPrefab()
        {
            var scanner = new CAssetReferenceScanner();
            scanner.Begin(MaterialPath);
            Drive(scanner);

            Assert.IsTrue(scanner.IsDone);
            CollectionAssert.Contains(scanner.Referencers, PrefabPath);
            CollectionAssert.DoesNotContain(scanner.Referencers, LonePath, "无关资源不该出现在反向引用里");
        }

        [Test]
        public void Scan_ReportsForwardDependenciesOfTheTarget()
        {
            var scanner = new CAssetReferenceScanner();
            scanner.Begin(PrefabPath);
            Drive(scanner);

            // 预制体依赖它挂着的材质（正向依赖一次调用即得）
            CollectionAssert.Contains(scanner.Dependencies, MaterialPath);
        }

        /// <summary>分帧的关键：一次 Step 不允许扫完全部候选（否则又变成卡界面）。</summary>
        [Test]
        public void Step_IsIncremental_NotAllAtOnce()
        {
            var scanner = new CAssetReferenceScanner { BudgetMillisecondsPerStep = 1.0 };
            scanner.Begin(MaterialPath);

            Assert.IsTrue(scanner.TotalCount > 0, "应枚举出候选资源");
            scanner.Step();

            Assert.Less(scanner.ScannedCount, scanner.TotalCount,
                "单次 Step 不该把全部候选扫完（否则又会在 OnGUI 里卡死）");
            Assert.Greater(scanner.ScannedCount, 0);
            Assert.IsTrue(scanner.Progress > 0f && scanner.Progress < 1f);
        }

        [Test]
        public void Scan_IsDoneAfterResultArrives_AndReusedForSameTarget()
        {
            var scanner = new CAssetReferenceScanner();
            scanner.Begin(MaterialPath);
            Drive(scanner);

            Assert.IsTrue(scanner.IsResultFor(MaterialPath), "同一目标应复用结果（窗口不再每帧重扫）");
            Assert.IsFalse(scanner.IsResultFor(PrefabPath), "换目标后应判定为需要重扫");
            Assert.IsFalse(scanner.IsResultFor(null));
        }

        [Test]
        public void Cancel_KeepsPartialResult_AndStopsRunning()
        {
            var scanner = new CAssetReferenceScanner { BudgetMillisecondsPerStep = 0.5 };
            scanner.Begin(MaterialPath);

            scanner.Cancel();

            Assert.IsFalse(scanner.IsRunning);
            Assert.IsTrue(scanner.IsDone);
            Assert.LessOrEqual(scanner.ScannedCount, scanner.TotalCount);
        }

        [Test]
        public void Begin_NonAssetsPath_CompletesImmediatelyWithoutScanning()
        {
            var scanner = new CAssetReferenceScanner();
            scanner.Begin("Packages/com.unity.addressables/package.json");

            Assert.IsTrue(scanner.IsDone);
            Assert.IsFalse(scanner.IsRunning);
            Assert.AreEqual(0, scanner.ScannedCount, "Assets 之外的路径不该触发全工程扫描");
        }

        [Test]
        public void Begin_EmptyOrNull_CompletesImmediately()
        {
            var scanner = new CAssetReferenceScanner();
            scanner.Begin(null);
            Assert.IsTrue(scanner.IsDone);
            scanner.Begin(string.Empty);
            Assert.IsTrue(scanner.IsDone);
            Assert.IsEmpty(scanner.Referencers);
        }

        /// <summary>反复 Step 直到结束（带上限，避免测试自己挂死）。</summary>
        private static void Drive(CAssetReferenceScanner scanner)
        {
            int guard = 0;
            while (scanner.IsRunning && guard++ < 100000)
            {
                scanner.Step();
            }
            Assert.IsTrue(scanner.IsDone, "扫描应能结束");
        }

        /// <summary>面板契约：Hub 按"static 类 + 同名 attribute + DrawTool(Action)"发现内嵌面板。</summary>
        [Test]
        public void SettingsPanel_ExposesHubInlineContract()
        {
            var panel = typeof(CAssetSettingsPanel);
            Assert.IsTrue(panel.IsClass && panel.IsAbstract && panel.IsSealed, "内嵌面板必须是 static 类");

            bool hasAttribute = false;
            foreach (object attribute in panel.GetCustomAttributes(false))
            {
                if (attribute.GetType().FullName == "CoffeeBean.EditorTools.CoffeeBeanToolAttribute") hasAttribute = true;
            }
            Assert.IsTrue(hasAttribute, "必须打上 CoffeeBeanTool 标记，否则 Hub 发现不了");

            var draw = panel.GetMethod("DrawTool",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(System.Action) }, null);
            Assert.IsNotNull(draw, "Hub 要求 public static void DrawTool(Action requestRepaint)");
            Assert.AreEqual(typeof(void), draw.ReturnType);
        }
    }
}
