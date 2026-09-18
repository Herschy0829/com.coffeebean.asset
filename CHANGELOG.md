# Changelog

## [0.4.0] - 2026-09-17

### Changed (BREAKING：异步返回类型 Task → UniTask)
- **资源管理的异步面全部改用 UniTask**（`IAssetBackend` / `CAssetSystem` / `CAssetExtensions` /
  `CCatalogUpdater`）。这是有意为之的取舍，理由写在下面，不是"跟风换库"。

  **调用方基本不用改**：`await CAssetSystem.Instance.LoadAssetAsync<Sprite>(addr)` 写法不变
  （UniTask 有 `GetAwaiter()`，任何 `await` 都能直接接）。要改的只有显式写了
  `Task<T>` 类型、`.ConfigureAwait(false)`、`Task.WhenAll`、`.GetAwaiter().GetResult()` 的地方。

  **为什么换**（`Task` 那条路每次 await 都要额外付，而且付在一个危险的地方）：

  | | `await handle.Task`（旧） | `await handle.ToUniTask()`（新） |
  |---|---|---|
  | 每次操作的分配 | 每个句柄 new `TaskCompletionSource<T>(RunContinuationsAsynchronously)`，**实测 ~104 B** | 等待源**池化**，预热后基本 0 |
  | 恢复调度 | 经调度器/线程池排队；不写 `ConfigureAwait(false)` 还要过 `UnitySynchronizationContext` | `PlayerLoop` 驱动，**直接在 Unity 主线程恢复** |
  | 主线程亲和性 | **不保证** | 保证 |
  | 取消 | 无 | `ToUniTask(cancellationToken:)` |

  旧实现里 `ConfigureAwait(false)` 会把续体丢到线程池，而 `InstantiateAsync` 紧接着调用
  `Object.Instantiate` —— 那是一次**潜在的主线程违规**（mock 后端同步完成所以测试没暴露）。
  现在 `InstantiateAsync` / `LoadSceneAsync` / `LoadTmpFontAsync` 在碰 Unity API 前显式
  `UniTask.SwitchToMainThread()`（已经在主线程时**立即完成**，不额外吃一帧）。

  **代价（说清楚）**：`com.cysharp.unitask` 成为本模块的**硬依赖**；对"一次加载几百个资源"
  的场景收益明显（GC 峰值与主线程排队都降下来），对"偶尔加载一个大资源"基本看不出来 ——
  后者本来就被 I/O 与反序列化支配。

- **一次性真实加载的分配**：`LoadAssetsByLabelAsync` / `PreloadAsync` 不再用 `ConfigureAwait(false)`
  与 `Task.WhenAll`（后者恒定分配数组 + 组合任务）。
- Addressables 句柄一律走 `ToUniTask()`，不再经过 `handle.Task`。

### Added
- `package.json` 声明 `com.cysharp.unitask: 2.5.11`；asmdef 显式引用 `UniTask` / `UniTask.Addressables`
  （不靠 auto-reference，消费工程关掉它也不会断）。
- `Tests/CAssetAsyncSurfaceTests.cs`：**锁住"异步面是 UniTask"这个决定** —— 公开方法返回
  `Task<T>` 会立刻红（含 `IAssetBackend` 契约与扩展方法）。

### Tests
- `MockAssetBackend` 改用 `UniTask.CompletedTask`（同步完成、不切线程、不需要 PlayerLoop，
  所以 EditMode 下不会挂在"没有 PlayerLoop 可泵"上）。
- asset 模块测试 27 → 31（新增 4 条异步面回归锁）。全量 EditMode **690 → 699**
  （698 通过 + 1 跳过；跳过的那条是 tools 里"未集成时才可空操作"的用例 ——
  dev 工程现在被强制依赖装上了 UniRx/UniTask，它的前置条件不再成立，按设计自跳过）。

## [0.3.0] - 2026-09-14

### Removed (BREAKING)
- **`CAssetOptions.AutoInitialize`**（死字段）：该字段在**全代码库中从未被读取**（只有声明），
  是一个"看着能关掉自动初始化、实际什么都不做"的假开关 —— 留着比删掉更有害，因为业务会以为设置它有效。
  真正决定初始化时机的是 Addressables 自身的惰性初始化（首次加载触发）。
  移除后，若你的初始化代码里有 `AutoInitialize = ...` 赋值，删掉该赋值即可，**行为完全不变**。

### Notes
- 同步修正 `docs/design-asset.md` 的 `CAssetOptions` API 清单 —— 该字段是"设计文档里列了、
  实现里从未接线"的残留。

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
