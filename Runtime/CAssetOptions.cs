using TMPro;
using UnityEngine.TextCore.LowLevel;

namespace CoffeeBean
{
    /// <summary>
    /// 资源模块配置：地址前缀规则、失败策略、TMP 动态字体参数（对齐 TmpData 收敛）。
    /// </summary>
    public sealed class CAssetOptions
    {
        /// <summary>加载失败仅告警不抛错（默认 true）。</summary>
        public bool FailSilently = true;

        /// <summary>可选统一地址前缀（默认 ""，对齐 AblesNaming 由业务决定）。</summary>
        public string AddressPrefix = string.Empty;

        // ---- TMP 动态字体参数（对齐 Idle TmpData，消除逻辑重复）----

        /// <summary>采样字号 SamplingPointSize。</summary>
        public int FontSamplingPointSize = 68;

        /// <summary>字符边距 Padding。</summary>
        public int FontPadding = 12;

        /// <summary>渲染模式。</summary>
        public GlyphRenderMode FontRenderMode = GlyphRenderMode.SDFAA;

        /// <summary>图集宽度。</summary>
        public int FontAtlasWidth = 4096;

        /// <summary>图集高度。</summary>
        public int FontAtlasHeight = 4096;

        /// <summary>图集填充模式（动态）。</summary>
        public AtlasPopulationMode FontAtlasMode = AtlasPopulationMode.Dynamic;

        /// <summary>是否启用多图集。</summary>
        public bool FontMultiAtlas = true;
    }
}
