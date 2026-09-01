using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>CAssetExtensions 组件绑定测试（mock 后端）：Image LoadSprite / 计数联动。</summary>
    public class CAssetExtensionsTests : CAssetTestBase
    {
        [Test]
        public void Image_LoadSprite_SetsSpriteAndRefCount()
        {
            var go = new GameObject("TestImage", typeof(RectTransform), typeof(Image));
            var image = go.GetComponent<Image>();
            int refBefore = CAssetSystem.Instance.GetRefCount(TextureAddress);

            image.LoadSprite(TextureAddress);

            Assert.IsNotNull(image.sprite, "Image.sprite 应被赋值");
            Assert.AreEqual(refBefore + 1, CAssetSystem.Instance.GetRefCount(TextureAddress), "加载后引用计数 +1");

            Object.DestroyImmediate(go);
        }

        [Test]
        public async Task Image_LoadSpriteAsync_SetsSprite()
        {
            var go = new GameObject("TestImageAsync", typeof(RectTransform), typeof(Image));
            var image = go.GetComponent<Image>();

            await image.LoadSpriteAsync(TextureAddress);

            Assert.IsNotNull(image.sprite, "异步加载后 Image.sprite 应被赋值");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Button_LoadSprite_SetsInnerImage()
        {
            var go = new GameObject("TestButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var button = go.GetComponent<Button>();

            button.LoadSprite(TextureAddress);

            Assert.IsNotNull(go.GetComponent<Image>().sprite, "Button 内 Image.sprite 应被赋值");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SpriteRenderer_LoadSprite_SetsSprite()
        {
            var go = new GameObject("TestSR");
            var sr = go.AddComponent<SpriteRenderer>();

            sr.LoadSprite(TextureAddress);

            Assert.IsNotNull(sr.sprite, "SpriteRenderer.sprite 应被赋值");

            Object.DestroyImmediate(go);
        }
    }
}
