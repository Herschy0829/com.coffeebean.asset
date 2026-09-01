using NUnit.Framework;
using UnityEngine;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// 测试基类：用 mock 后端注入 CAssetSystem（不依赖 Addressables 初始化），
    /// 提供测试 Sprite / GameObject 资源。验证缓存/引用计数/释放等核心语义。
    /// </summary>
    public abstract class CAssetTestBase
    {
        protected MockAssetBackend Backend;
        protected string TextureAddress = "test/tex";
        protected string PrefabAddress = "test/prefab";

        private Sprite _sprite;
        private GameObject _prefab;

        [SetUp]
        public void BaseSetUp()
        {
            // EditMode 下单例不自动创建，手动创建实例并注入 mock 后端
            var go = new GameObject("[CoffeeBean] CAssetSystem");
            var inst = go.AddComponent<CAssetSystem>();
            CAssetSystem.SetInstanceForTest(inst);

            Backend = new MockAssetBackend();
            inst.Backend = Backend;

            // 注册测试资源
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            _sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _sprite.name = "TestSprite";
            Backend.Register(TextureAddress, _sprite);

            _prefab = new GameObject("TestPrefab");
            Backend.Register(PrefabAddress, _prefab);
        }

        [TearDown]
        public void BaseTearDown()
        {
            CAssetSystem.ResetInstanceForTest();
            if (_sprite != null)
            {
                Object.DestroyImmediate(_sprite.texture);
                Object.DestroyImmediate(_sprite);
            }
            if (_prefab != null) Object.DestroyImmediate(_prefab);
        }
    }
}
