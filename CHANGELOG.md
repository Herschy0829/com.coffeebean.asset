# Changelog

## [0.2.3] - 2026-09-14

### Fixed
- **`Runtime/Bridge/Bridge.cs` 声明的模块版本与 `package.json` 不一致**：Bridge 里写的是 `0.1.0`，
  而 `package.json` 已经到 `0.2.2`（落后两个版本）。`CoffeeBeanModule` 特性的 Version 是 Core 运行期
  做 `MinCoreVersion` 兼容校验、Hub 显示与模块清单的唯一来源，因此消费工程看到的 asset 版本一直是错的
  `0.1.0` —— 这既违反模板契约（`templates/module/PLACEHOLDERS.md` §2 要求两处必须一致），
  也让"模块版本"这一列失去可信度。现已对齐为 `0.2.3`。
- README 安装示例的 tag 由严重过期的 `v0.1.0` 修正为 `v0.2.3`。

## [0.2.2] - 2026-08-28


### Added
- **资源依赖分析工具**（CAssetDependencyWindow）：选择资源查看正向依赖与被引用（反向依赖），注册进 CoffeeBean Hub（Asset 分类）

# Changelog

## [0.2.1] - 2026-08-28


### Added
- **CAutoRelease 组件自动释放钩子**：登记已加载资源地址，组件销毁时自动 CAssetSystem.Release（防泄漏）；TrackRelease 便捷扩展

# Changelog

## [0.2.0] - 2026-08-28


### Added
- **Pin / Unpin 常驻资源**：CAssetSystem.Pin 加载并标记常驻（ReleaseUnused/Release 归零不清理），Unpin 解除；ForceRelease 强制释放并解除

# Changelog

## [0.1.2] - 2026-08-28


### Changed
- **移除 Window/CoffeeBean 子菜单项**（避免子菜单抢占 Hub 主入口）：Addressables 设置改为独立窗口 + CoffeeBeanToolAttribute 注册进 Hub 导航

# Changelog

## [0.1.1] - 2026-08-28


### Changed
- InternalsVisibleTo 增加 CoffeeBean.UI.Tests（ui 模块 CAssetPanelLoader 测试复用 asset 测试注入）

# Changelog

## [0.1.0] - 2026-08-28

### Added
- **`CAssetSystem` 资源加载门面**（封装 Unity Addressables）：
  同步/异步加载（`LoadAsset` / `LoadAssetAsync`，C# Task）、实例化（`Instantiate` / `InstantiateAsync`）、
  预加载（`PreloadAsync`）、标签加载（`LoadAssetsByLabelAsync`）、场景加载（`LoadSceneAsync`）
- **引用计数释放**：三字典缓存（address → Object / handle / refCount），加载成功 +1（含缓存命中），
  `Release` 归零才真正释放；`ForceRelease` / `ReleaseUnused` / `ReleaseAll`；`GetCacheStats` 统计
- **`CAssetOptions`**：地址前缀、失败策略（静默/抛错）、TMP 动态字体参数（对齐 TmpData 收敛）
- **`CAssetExtensions` 组件绑定**：Image/Button/SpriteRenderer 加载 Sprite、
  TMP_Text/Text 动态字体（缓存复用）、AudioSource 加载 AudioClip —— 统一走 CAssetSystem 共享缓存
- **`CCatalogUpdater` 更新下载服务**：catalog 检测更新 → 下载（进度回调）→ 失败重试（MaxRetry）
- **`CAssetSetup`（Editor）**：自动创建 AddressableAssetSettings（无则生成），菜单入口
- **AssetDemo 示例**：加载/释放/统计演示
- EditMode 测试：缓存语义 / 引用计数 / 释放归零 / 强制释放 / 闲置释放 / 异步加载 / 实例化 / 失败容错 / 统计 / 组件绑定

### Notes
- 依赖 `com.coffeebean.tools`（单例/日志）+ `com.unity.addressables`（**声明依赖，来源消费工程定**）
- Core 可选集成：Bridge 条件编译（模块标记 + 生命周期）
