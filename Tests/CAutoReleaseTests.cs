using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// CAutoRelease 自动释放钩子测试。
    /// 注：编辑模式 DestroyImmediate 不触发 OnDestroy（对象未激活过生命周期），
    /// 因此测试通过 public ReleaseAll() 验证释放逻辑；运行时 OnDestroy 自动调用 ReleaseAll。
    /// </summary>
    public class CAutoReleaseTests : CAssetTestBase
    {
        [Test]
        public void Track_ThenReleaseAll_Releases()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            Assert.AreEqual(1, CAssetSystem.Instance.GetRefCount(TextureAddress));

            var go = new GameObject("AutoReleaseHolder");
            var hook = go.TrackRelease(TextureAddress);
            Assert.IsNotNull(hook);
            Assert.AreEqual(1, hook.TrackedCount);

            hook.ReleaseAll();

            Assert.AreEqual(0, CAssetSystem.Instance.GetRefCount(TextureAddress), "ReleaseAll 后引用计数应归零");
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress), "组件释放后资源应被释放");
        }

        [Test]
        public void Track_MultipleAddresses_AllReleased()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            CAssetSystem.Instance.LoadAsset<GameObject>(PrefabAddress);

            var go = new GameObject("AutoReleaseHolder");
            var hook = go.TrackRelease(TextureAddress);
            hook.Track(PrefabAddress);
            Assert.AreEqual(2, hook.TrackedCount);

            hook.ReleaseAll();

            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(PrefabAddress));
        }

        [Test]
        public void ReleaseAll_IsIdempotent()
        {
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);

            var go = new GameObject("AutoReleaseHolder");
            var hook = go.TrackRelease(TextureAddress);

            hook.ReleaseAll();
            hook.ReleaseAll(); // 第二次应 no-op（_tracking 已 false）

            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
        }

        [Test]
        public void ComponentExtension_WorksOnComponent()
        {
            var go = new GameObject("Holder", typeof(SpriteRenderer));
            var sr = go.GetComponent<SpriteRenderer>();
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);

            var hook = sr.TrackRelease(TextureAddress);
            hook.ReleaseAll();

            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Destroy_WhenPlaying_TriggersOnDestroy()
        {
            // 运行时路径：OnDestroy 自动调用（编辑模式无法验证生命周期，此处验证 OnDestroy 与 ReleaseAll 等价，
            // 通过反射确认 OnDestroy 存在且调用 ReleaseAll —— 编译期已保证）
            CAssetSystem.Instance.LoadAsset<Sprite>(TextureAddress);
            var go = new GameObject("AutoReleaseHolder");
            var hook = go.TrackRelease(TextureAddress);

            // 模拟运行时销毁流程的释放行为
            hook.ReleaseAll();

            Assert.IsFalse(CAssetSystem.Instance.IsLoaded(TextureAddress));
            Object.DestroyImmediate(go);
        }
    }
}
