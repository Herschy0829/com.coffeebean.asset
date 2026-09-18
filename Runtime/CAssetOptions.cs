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

        // ---- 编辑器路径兜底（只影响编辑器；打包后这段代码不存在）----

        /// <summary>
        /// **仅编辑器**：catalog 里查不到这个地址时，按资源路径直读 AssetDatabase 兜底（默认 true）。
        ///
        /// 为什么要它：迭代期不想每加一个资源就手工往 Addressables group 里加一条。
        /// 打开后，`Building/Building_1002` 这种"路径形状的地址"即使没登记进 group，
        /// 在编辑器里也能直接加载出来。
        ///
        /// **代价必须清楚**：靠兜底才加载成功的地址，**打包后一定失败**。
        /// 所以每次兜底都会记进 `CAssetEditorPathFallback.Recorded`（并打一次警告），
        /// Hub 的「Addressables 设置」面板里能直接看到这份清单 —— 出包前照着核对。
        ///
        /// 注意：它**不是** FastMode 的功能。FastMode 依然只认 catalog，
        /// 这是框架自己在编辑器里加的一层"按路径直读"。
        /// </summary>
        public bool EditorPathFallback = true;

        /// <summary>兜底搜索的根目录（默认整个 Assets）。地址按"路径后缀"匹配，忽略扩展名。</summary>
        public string[] EditorPathFallbackRoots = { "Assets" };

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
