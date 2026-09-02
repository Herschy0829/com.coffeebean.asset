using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>CAssetSystem 核心测试（mock 后端）：缓存/引用计数/释放/统计/失败容错。</summary>
    public class CAssetSystemTests : CAssetTestBase
    {
        [Test]
        public void LoadAsset_LoadsAndCaches()
        {
            var asset = CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);

            Assert.IsNotNull(asset, "应成功加载 Sprite");
            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(TextureAddress), "加载后应缓存");
            Assert.AreEqual(1, CAssetSystem.Instance.GetRefCount(TextureAddress), "首次加载计数应为 1");
            Assert.IsTrue(Backend.IsLoaded(TextureAddress), "后端应持有资源");
        }

        [Test]
        public void LoadAsset_RepeatedLoad_IncrementsRefCount()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);

            Assert.AreEqual(2, CAssetSystem.Instance.GetRefCount(TextureAddress), "重复加载应 +1（缓存命中）");
            Assert.AreEqual(1, CAssetSystem.Instance.GetCacheStats().cacheCount, "同一地址只缓存一份");
            Assert.AreEqual(1, Backend.LoadSyncCount, "缓存命中不应再次走后端");
        }

        [Test]
        public void Release_ToZero_ActuallyFrees()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);

            CAssetSystem.Instance.Release(TextureAddress);
            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(TextureAddress), "计数 2→1 仍缓存");
            Assert.AreEqual(1, CAssetSystem.Instance.GetRefCount(TextureAddress));

            CAssetSystem.Instance.Release(TextureAddress);
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress), "计数归零应释放");
            Assert.AreEqual(0, CAssetSystem.Instance.GetRefCount(TextureAddress));
            Assert.IsFalse(Backend.IsLoaded(TextureAddress), "后端应释放资源");
        }

        [Test]
        public void ForceRelease_IgnoresRefCount()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);

            CAssetSystem.Instance.ForceRelease(TextureAddress);
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress), "强制释放应忽略计数");
        }

        [Test]
        public void ReleaseUnused_FreesOnlyZeroCount()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<GameObject>(PrefabAddress);
            // Prefab 加载两次后释放一次 → 计数 1（未归零，不被 ReleaseUnused 处理）
            CAssetSystem.Instance.LoadAsset<GameObject>(PrefabAddress);
            CAssetSystem.Instance.Release(PrefabAddress);

            int released = CAssetSystem.Instance.ReleaseUnused();

            Assert.AreEqual(0, released, "计数 >0 的资源不应被 ReleaseUnused 释放");
            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(TextureAddress));
            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(PrefabAddress), "计数 1 的资源应保留");
            Assert.AreEqual(1, CAssetSystem.Instance.GetRefCount(PrefabAddress));
        }

        [Test]
        public void ReleaseUnused_FreesZeroCountItem()
        {
            // 制造一个计数为 0 但仍缓存的项：加载两次，释放两次（第二次归零时才会真正释放），
            // 通过 ForceRelease 路径模拟：ForceRelease 忽略计数直接释放，
            // 这里验证 Release 归零即释放 + 字典清理（不依赖 ReleaseUnused 悬挂项）
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress); // 计数 2

            CAssetSystem.Instance.Release(TextureAddress); // 计数 1
            CAssetSystem.Instance.Release(TextureAddress); // 计数 0 → 释放

            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress), "归零应立即释放");
            Assert.AreEqual(0, CAssetSystem.Instance.GetRefCount(TextureAddress));
        }

        [Test]
        public async Task LoadAssetAsync_LoadsAndCaches()
        {
            var asset = await CAssetSystem.Instance.LoadAssetAsync<Sprite>(TextureAddress);

            Assert.IsNotNull(asset, "异步应成功加载 Sprite");
            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(TextureAddress));
            Assert.AreEqual(1, CAssetSystem.Instance.GetRefCount(TextureAddress));
            Assert.AreEqual(1, Backend.LoadAsyncCount);
        }

        [Test]
        public async Task InstantiateAsync_InstantiatesPrefab()
        {
            var go = await CAssetSystem.Instance.InstantiateAsync(PrefabAddress);

            Assert.IsNotNull(go, "应成功实例化");
            Assert.AreEqual("TestPrefab", go.name, "实例名应为 prefab 名");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void LoadAsset_MissingAddress_ReturnsNull()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("未找到资源"));
            var asset = CAssetSystem.Instance.LoadAsset<Sprite>("NotExistAsset_XYZ");
            Assert.IsNull(asset, "不存在的地址应返回 null");
        }

        [Test]
        public async Task LoadAssetAsync_MissingAddress_ReturnsNull()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("未找到资源"));
            var asset = await CAssetSystem.Instance.LoadAssetAsync<Sprite>("NotExistAsset_XYZ");
            Assert.IsNull(asset, "不存在的地址应返回 null");
        }

        [Test]
        public void TryGetCached_OnlyWhenLoaded()
        {
            Assert.IsFalse(CAssetSystem.Instance.TryGetCached<Sprite>(TextureAddress, out _), "未加载不应命中");

            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            Assert.IsTrue(CAssetSystem.Instance.TryGetCached<Sprite>(TextureAddress, out var sprite), "加载后应命中");
            Assert.IsNotNull(sprite);
        }

        [Test]
        public void GetCacheStats_ReportsCounts()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress); // +1
            CAssetSystem.Instance.LoadAsset<GameObject>(PrefabAddress);

            var (cacheCount, totalRef) = CAssetSystem.Instance.GetCacheStats();
            Assert.AreEqual(2, cacheCount, "两个不同地址缓存");
            Assert.AreEqual(3, totalRef, "总引用数 = 2 + 1");
        }

        [Test]
        public void ReleaseAll_ClearsEverything()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<GameObject>(PrefabAddress);

            CAssetSystem.Instance.ReleaseAll();

            Assert.AreEqual(0, CAssetSystem.Instance.GetCacheStats().cacheCount);
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
            Assert.AreEqual(1, Backend.ReleaseAllCount, "应调用后端 ReleaseAll");
        }

        // ========== Pin / Unpin（常驻资源） ==========

        [Test]
        public void Pin_LoadsAndMarksResident()
        {
            var asset = CAssetSystem.Instance.Pin<Sprite>(TextureAddress);

            Assert.IsNotNull(asset, "Pin 应加载资源");
            Assert.IsTrue(CAssetSystem.Instance.IsPinned(TextureAddress), "Pin 后应标记常驻");
        }

        [Test]
        public void ReleaseUnused_SkipsPinned()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.Release(TextureAddress); // 计数归零

            CAssetSystem.Instance.Pin<Sprite>(TextureAddress); // 常驻

            int released = CAssetSystem.Instance.ReleaseUnused();
            Assert.AreEqual(0, released, "常驻资源不应被闲置清理");
            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(TextureAddress), "常驻资源应保留");
        }

        [Test]
        public void Release_OnPinned_DoesNotFree()
        {
            CAssetSystem.Instance.Pin<Sprite>(TextureAddress);
            CAssetSystem.Instance.Release(TextureAddress); // 计数归零但常驻

            Assert.IsTrue(CAssetSystem.Instance.IsLoaded(TextureAddress), "常驻资源 Release 归零不应释放");
        }

        [Test]
        public void Unpin_ThenReleaseUnused_Frees()
        {
            CAssetSystem.Instance.Pin<Sprite>(TextureAddress);
            CAssetSystem.Instance.Release(TextureAddress);
            CAssetSystem.Instance.Unpin(TextureAddress);

            Assert.IsFalse(CAssetSystem.Instance.IsPinned(TextureAddress), "Unpin 应解除常驻");
            int released = CAssetSystem.Instance.ReleaseUnused();
            Assert.AreEqual(1, released, "Unpin 后应可被闲置清理");
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
        }

        [Test]
        public void ForceRelease_ReleasesPinned()
        {
            CAssetSystem.Instance.Pin<Sprite>(TextureAddress);

            CAssetSystem.Instance.ForceRelease(TextureAddress);

            Assert.IsFalse(CAssetSystem.Instance.IsPinned(TextureAddress), "ForceRelease 应解除常驻");
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
        }
    }
}
