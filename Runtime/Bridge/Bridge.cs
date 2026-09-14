#if COFFEEBEAN_CORE
using CoffeeBean;

[assembly: CoffeeBeanModule(
    "com.coffeebean.asset",
    "0.2.3",
    DisplayName = "Asset",
    Description = "Asset management: Addressables facade (CAssetSystem), UI binding extensions, catalog updater.",
    Dependencies = new[] { "com.coffeebean.core", "com.coffeebean.tools" }
)]

namespace CoffeeBean
{
    /// <summary>
    /// Core 集成：CAssetSystem 由业务按需访问（首次访问自动创建单例并初始化 Addressables），
    /// 本模块标记使 asset 可被 Core 发现、启停与版本检查。
    /// </summary>
    public sealed class AssetModule : ICoffeeBeanModule
    {
        public void OnLoad(CoffeeBeanContext context)
        {
            context.Log("CoffeeBean.Asset integrated (CAssetSystem auto-creates on first access).");
        }

        public void OnStart()
        {
        }

        public void OnShutdown()
        {
        }
    }
}
#endif
