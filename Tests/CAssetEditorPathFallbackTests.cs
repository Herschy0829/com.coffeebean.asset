using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// 编辑器路径兜底测试。
    ///
    /// 目的：迭代期"新增资源不用手工加进 group" —— 地址在 catalog 里查不到时，
    /// 编辑器按资源路径直读 AssetDatabase。代价是靠兜底才活的地址打包后会失败，
    /// 所以每次兜底都必须被记进清单（这里是回归锁）。
    ///
    /// 注意：FastMode **不是**这个能力 —— FastMode 依然只认 catalog。
    /// 这层兜底是框架自己在编辑器里加的，且整段代码在 `#if UNITY_EDITOR` 内（player 里没有）。
    /// </summary>
    public class CAssetEditorPathFallbackTests
    {
        private const string Folder = "Assets/__CoffeeBeanFallbackTests";
        private const string PrefabPath = Folder + "/Building_1002.prefab";
        private const string Address = "__CoffeeBeanFallbackTests/Building_1002"; // 故意不带扩展名、不带 Assets/ 前缀

        private GameObject _managerGo;

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "__CoffeeBeanFallbackTests");

            var go = new GameObject("Building_1002");
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            CAssetEditorPathFallback.ClearRecorded();
            CAssetEditorPathFallback.InvalidateIndex();
            CAssetEditorPathFallback.Enabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            CAssetEditorPathFallback.ClearRecorded();
            CAssetEditorPathFallback.InvalidateIndex();
            if (_managerGo != null) Object.DestroyImmediate(_managerGo);
            CAssetSystem.ResetInstanceForTest();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        // ========== 路径匹配 ==========

        [Test]
        public void TryLoad_ByPartialPathWithoutExtension_Hits()
        {
            bool ok = CAssetEditorPathFallback.TryLoad(Address, out GameObject asset, out string resolved);

            Assert.IsTrue(ok, $"应按路径后缀命中：{Address}");
            Assert.IsNotNull(asset);
            Assert.AreEqual(PrefabPath, resolved);
        }

        [Test]
        public void TryLoad_ByFullAssetPath_Hits()
        {
            bool ok = CAssetEditorPathFallback.TryLoad(PrefabPath, out GameObject asset, out string resolved);

            Assert.IsTrue(ok);
            Assert.IsNotNull(asset);
            Assert.AreEqual(PrefabPath, resolved);
        }

        [Test]
        public void TryLoad_UnknownAddress_Misses()
        {
            Assert.IsFalse(CAssetEditorPathFallback.TryLoad("No/Such/Thing", out GameObject asset, out _));
            Assert.IsNull(asset);
        }

        [Test]
        public void TryLoad_WrongType_Misses()
        {
            // 路径存在但类型不对（这里要 Sprite，磁盘上是 Prefab）
            Assert.IsFalse(CAssetEditorPathFallback.TryLoad(Address, out Sprite sprite, out _));
            Assert.IsNull(sprite);
        }

        [Test]
        public void TryLoad_NullOrEmpty_Misses()
        {
            Assert.IsFalse(CAssetEditorPathFallback.TryLoad(null, out GameObject a1, out _));
            Assert.IsFalse(CAssetEditorPathFallback.TryLoad(string.Empty, out GameObject a2, out _));
        }

        // ========== 清单 ==========

        [Test]
        public void Report_RecordsOnce_AndPersistsToEditorPrefs()
        {
            CAssetEditorPathFallback.Report(Address, PrefabPath);
            CAssetEditorPathFallback.Report(Address, PrefabPath); // 重复不该产生第二条

            Assert.AreEqual(1, CAssetEditorPathFallback.Recorded.Count);
            Assert.AreEqual(PrefabPath, CAssetEditorPathFallback.Recorded[Address]);

            // 持久化：清单必须活过进出 Play 模式的域重载
            string raw = EditorPrefs.GetString("CoffeeBean.Asset.EditorFallback.Recorded", string.Empty);
            StringAssert.Contains(Address, raw);

            StringAssert.Contains(Address, CAssetEditorPathFallback.DescribeRecorded());

            CAssetEditorPathFallback.ClearRecorded();
            Assert.AreEqual(0, CAssetEditorPathFallback.Recorded.Count);
            Assert.AreEqual(string.Empty, EditorPrefs.GetString("CoffeeBean.Asset.EditorFallback.Recorded", string.Empty));
        }

        // ========== 与 CAssetSystem 的联动 ==========

        /// <summary>
        /// 后端说"没这个地址"（= catalog 里没有），框架应当按路径兜底加载成功，
        /// **并把地址记进清单** —— 这正是"编辑器能跑、打包才炸"的那批地址。
        /// </summary>
        [Test]
        public async Task LoadAssetAsync_CatalogMiss_FallsBackAndRecords()
        {
            var manager = new GameObject("[CoffeeBean] CAssetSystem");
            _managerGo = manager;
            var system = manager.AddComponent<CAssetSystem>();
            CAssetSystem.SetInstanceForTest(system);
            var backend = new MockAssetBackend();      // 空后端：任何地址都"查不到"
            system.Backend = backend;
            system.Options = new CAssetOptions { EditorPathFallback = true, FailSilently = true };

            var loaded = await system.LoadAssetAsync<GameObject>(Address);

            Assert.IsNotNull(loaded, "catalog 里没有，但编辑器应能按路径兜底加载出来");
            Assert.AreEqual(PrefabPath, AssetDatabase.GetAssetPath(loaded));
            Assert.IsTrue(CAssetEditorPathFallback.Recorded.ContainsKey(Address),
                "兜底过的地址必须进清单（否则出包前查不出来）");
        }

        [Test]
        public async Task LoadAssetAsync_FallbackDisabled_ReturnsNull()
        {
            var manager = new GameObject("[CoffeeBean] CAssetSystem");
            _managerGo = manager;
            var system = manager.AddComponent<CAssetSystem>();
            CAssetSystem.SetInstanceForTest(system);
            system.Backend = new MockAssetBackend();
            system.Options = new CAssetOptions { EditorPathFallback = false, FailSilently = true };

            var loaded = await system.LoadAssetAsync<GameObject>(Address);

            Assert.IsNull(loaded, "关掉兜底后，catalog 里没有就该老老实实返回 null");
            Assert.AreEqual(0, CAssetEditorPathFallback.Recorded.Count, "关掉兜底时不该记录");
        }

        /// <summary>同步加载路径也要有兜底（老代码里有地方用同步 API）。</summary>
        [Test]
        public void LoadAsset_WithFallback_ReturnsPrefab()
        {
            var manager = new GameObject("[CoffeeBean] CAssetSystem");
            _managerGo = manager;
            var system = manager.AddComponent<CAssetSystem>();
            CAssetSystem.SetInstanceForTest(system);
            system.Backend = new MockAssetBackend();
            system.Options = new CAssetOptions { EditorPathFallback = true, FailSilently = true };

            var loaded = system.LoadAsset<GameObject>(Address);

            Assert.IsNotNull(loaded);
            Assert.IsTrue(CAssetEditorPathFallback.Recorded.ContainsKey(Address));
        }
    }
}
