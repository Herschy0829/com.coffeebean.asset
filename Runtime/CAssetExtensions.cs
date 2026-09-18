using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeBean
{
    /// <summary>
    /// 组件资源绑定扩展（统一走 <see cref="CAssetSystem"/>，共享缓存与引用计数）：
    /// Image/Button/SpriteRenderer 加载 Sprite；TMP_Text/Text 动态字体；AudioSource 加载 AudioClip。
    /// 替代散落的私有缓存字典，避免资源常驻泄漏。
    /// </summary>
    public static class CAssetExtensions
    {
        // ---- Image / Button / SpriteRenderer ----

        /// <summary>同步加载 Sprite 并赋值（Image）。</summary>
        public static void LoadSprite(this Image image, string address)
        {
            if (image == null) return;
            var sprite = CAssetSystem.Instance.LoadAsset<Sprite>(address);
            if (sprite != null) image.sprite = sprite;
        }

        /// <summary>异步加载 Sprite 并赋值（Image）。</summary>
        public static async UniTask LoadSpriteAsync(this Image image, string address)
        {
            if (image == null) return;
            var sprite = await CAssetSystem.Instance.LoadAssetAsync<Sprite>(address);
            if (sprite != null && image != null) image.sprite = sprite;
        }

        /// <summary>同步加载 Sprite 并赋值（Button 内 Image）。</summary>
        public static void LoadSprite(this Button button, string address)
        {
            if (button == null) return;
            button.GetComponent<Image>()?.LoadSprite(address);
        }

        /// <summary>异步加载 Sprite 并赋值（Button 内 Image）。</summary>
        public static async UniTask LoadSpriteAsync(this Button button, string address)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image != null) await image.LoadSpriteAsync(address);
        }

        /// <summary>同步加载 Sprite 并赋值（SpriteRenderer）。</summary>
        public static void LoadSprite(this SpriteRenderer renderer, string address)
        {
            if (renderer == null) return;
            var sprite = CAssetSystem.Instance.LoadAsset<Sprite>(address);
            if (sprite != null) renderer.sprite = sprite;
        }

        // ---- TMP_Text 动态字体 ----

        private static readonly Dictionary<string, TMP_FontAsset> FontCache = new Dictionary<string, TMP_FontAsset>();

        /// <summary>同步加载 Font 并生成 TMP_FontAsset（动态字体，参数来自 CAssetOptions，结果缓存）。</summary>
        public static void LoadFont(this TMP_Text tmpText, string address)
        {
            if (tmpText == null) return;
            tmpText.font = LoadTmpFont(address);
        }

        /// <summary>异步加载 Font 并生成 TMP_FontAsset。</summary>
        public static async UniTask LoadFontAsync(this TMP_Text tmpText, string address)
        {
            if (tmpText == null) return;
            var fontAsset = await LoadTmpFontAsync(address);
            if (fontAsset != null && tmpText != null) tmpText.font = fontAsset;
        }

        /// <summary>同步生成 TMP 字体（缓存复用）。</summary>
        public static TMP_FontAsset LoadTmpFont(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (FontCache.TryGetValue(address, out var cached)) return cached;

            var font = CAssetSystem.Instance.LoadAsset<Font>(address);
            if (font == null) return null;

            var opt = CAssetSystem.Instance.Options;
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font, opt.FontSamplingPointSize, opt.FontPadding, opt.FontRenderMode,
                opt.FontAtlasWidth, opt.FontAtlasHeight, opt.FontAtlasMode, opt.FontMultiAtlas);
            fontAsset.name = address;
            FontCache[address] = fontAsset;
            return fontAsset;
        }

        /// <summary>异步生成 TMP 字体（缓存复用）。</summary>
        public static async UniTask<TMP_FontAsset> LoadTmpFontAsync(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (FontCache.TryGetValue(address, out var cached)) return cached;

            var font = await CAssetSystem.Instance.LoadAssetAsync<Font>(address);
            if (font == null) return null;

            // TMP_FontAsset.CreateFontAsset 会建 Unity 对象 —— 显式收口到主线程
            await UniTask.SwitchToMainThread();
            var opt = CAssetSystem.Instance.Options;
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font, opt.FontSamplingPointSize, opt.FontPadding, opt.FontRenderMode,
                opt.FontAtlasWidth, opt.FontAtlasHeight, opt.FontAtlasMode, opt.FontMultiAtlas);
            fontAsset.name = address;
            FontCache[address] = fontAsset;
            return fontAsset;
        }

        /// <summary>清理 TMP 字体缓存（如切语言/释放内存时）。</summary>
        public static void ClearTmpFontCache()
        {
            foreach (var font in FontCache.Values)
            {
                if (font != null) Object.Destroy(font);
            }
            FontCache.Clear();
        }

        // ---- Text（原生 uGUI）----

        /// <summary>同步加载 Font 并赋值（原生 Text）。</summary>
        public static void LoadFont(this Text text, string address)
        {
            if (text == null) return;
            var font = CAssetSystem.Instance.LoadAsset<Font>(address);
            if (font != null) text.font = font;
        }

        /// <summary>异步加载 Font 并赋值（原生 Text）。</summary>
        public static async UniTask LoadFontAsync(this Text text, string address)
        {
            if (text == null) return;
            var font = await CAssetSystem.Instance.LoadAssetAsync<Font>(address);
            if (font != null && text != null) text.font = font;
        }

        // ---- AudioSource ----

        /// <summary>同步加载 AudioClip 并赋值（AudioSource）。</summary>
        public static void LoadClip(this AudioSource source, string address)
        {
            if (source == null) return;
            var clip = CAssetSystem.Instance.LoadAsset<AudioClip>(address);
            if (clip != null) source.clip = clip;
        }

        /// <summary>异步加载 AudioClip 并赋值（AudioSource）。</summary>
        public static async UniTask LoadClipAsync(this AudioSource source, string address)
        {
            if (source == null) return;
            var clip = await CAssetSystem.Instance.LoadAssetAsync<AudioClip>(address);
            if (clip != null && source != null) source.clip = clip;
        }
    }
}
