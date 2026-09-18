# Changelog

## [0.6.0] - 2026-09-18

### Added
- **「Addressables 设置」面板补上"打包验证"三段**：默认播放模式是
  `Use Asset Database (fastest)`，编辑器里读的是 AssetDatabase、**不经过 bundle 打包** ——
  所以"在编辑器里跑一遍"**证明不了**资源打得进包。现在面板按三层递进给齐手段：

  1. **播放模式一键切换**（`FastMode` ⇄ `Use Existing Build`）：
     前者日常开发最快、不校验打包；后者读**真实构建产物**，与真机一致。切过去时提示"需先构建内容"。
     **注意：Addressables 2.x 已经没有 "Simulate Groups (advanced)" 这个播放模式脚本了**
     （只有 `FastMode` / `PackedPlayMode` / `PackedMode`），网上老教程里的那一档在你版本里不存在。
  2. **打包预检**（`CAssetContentPreflight`，**不构建、秒级**）抓"编辑器能跑、打包才炸"的配置问题：
     - 重复 address（catalog 里后者覆盖前者，只在运行期 LogError）；
     - address 为空的条目；
     - **失效条目**（资源已删但条目还留在 group 里 —— 构建直接报错）；
     - **未解析的 Profile 变量**（变量值 / bundle 的 BuildPath·LoadPath 里还残留 `[Var]`）；
     - 空 group（提示级，通常是漏配地址的信号）。
     结果按类目列出（每类最多 20 条明细），一键打 Console。
  3. **一键 Build Content（New Build）** + 结果报告（`LocationCount` / `Duration` / `OutputPath` / `Error`）。
     构建是**同步阻塞**的（Unity 官方菜单也一样），所以先弹确认框说明；构建完切到
     `Use Existing Build` 就能在编辑器里按真机方式验证。
  4. 快捷入口：一键打开 Unity 自带的 **Analyze 窗口**（Bundle Layout Preview、Check Bundle Dupe
     Dependencies 等规则，不重复造轮子）与 **Groups 窗口**。

### Notes
- Addressables 2.9.1 的 `AddressableAssetBuildResult` **没有 `Warnings` 字段**（只有
  `Error` / `LocationCount` / `Duration` / `OutputPath` / `FileRegistry`），报告按实际字段写。
- 2.x 里 group 本身没有 `BuildPath`/`LoadPath`（1.x 有），路径挂在 `BundledAssetGroupSchema`
  的 `ProfileValueReference` 上 —— 预检按 2.x 的 API 取。

### Tests
- 新增 `CAssetContentPreflightTests`（8 条，纯逻辑喂记录、不碰工程设置）：
  健康工程无问题、重复地址带两组名、空地址、失效条目、未解析变量、空组只算提示、
  null 输入不抛、明细截断（不刷爆 Console/面板）。
- asset 测试 42 → 50；全量 EditMode **710 → 718**（717 通过 + 1 有意跳过）。

## [0.5.0] - 2026-09-18

### Fixed（都是"点了没反应"的真实成因）

- **「资源依赖分析」窗口自己把自己卡死** —— 用户实测反馈"点按钮没反应"。

  根因：反向引用扫描写在 `OnGUI` 里，对全工程资源逐个 `AssetDatabase.GetDependencies`。
  实测该工程（5206 个资源、约 1.3 ms/项）**一次全量约 6.8 秒**，而 `OnGUI` 每帧至少跑
  Layout + Repaint **两次**，改目标 / 敲过滤词还会重跑 —— 于是窗口一打开就永久停在扫描里，
  连一帧都画不完，"定位"按钮自然永远不会响应。

  改法：把扫描抽成新的 **`CAssetReferenceScanner`**（与 GUI 解耦、可单测），**分帧增量**执行：
  - 每帧只花一段预算（默认 10 ms，挂在 `EditorApplication.update` 上，**不再走 OnGUI**）；
  - 有进度条、可**取消**、结果**边扫边出**；
  - 同一目标结果复用（用 `GetAssetDependencyHash` 作失效键），不再每帧重扫；
  - 正向依赖 = 一次 `GetDependencies`，即时出；只有反向引用才需要扫描。

- **「Addressables 设置」两个按钮在工程已配置好时静默空转**：`EnsureSettings()` 与
  `SetPlayModeUseAssetDatabase()` 遇到"已经是我要的状态"就直接 `return`，界面和 Console 都没有任何变化
  —— 实测该工程正好处于这个状态（设置已存在、播放模式已是 Use Asset Database），两个按钮点了必然"没反应"。
  现在两个方法**返回"这次到底改了什么"**（`bool`）并打日志，界面据此显示结果。

### Changed
- **「Addressables 设置」从独立窗口改为 Hub 内嵌面板**（`CAssetSettingsPanel`，用框架的内嵌面板机制）：
  原来在 Hub 里点导航只是"选中"，还要在右侧卡片上再点一次「打开」才弹窗口 —— 两步，
  而且第一步看起来像没反应。现在一次点击就在 Hub 内容区看到状态与按钮。
  删除 `CAssetSettingsWindow`（功能全部搬进面板，保留独立窗口没有意义）。
- 面板里**无事可做时按钮直接置灰并写明原因**："创建 Addressable 设置（已存在，无需创建）"、
  "切换为 Use Asset Database（已是该模式，无需切换）" —— 置灰 + 原因，比"能点但没反应"清楚得多。
- 状态行常显：设置是否存在 / 当前播放模式名 / 是否已是编辑器直读（绿=正常，橙=需注意）。
- **「资源依赖分析」自动跟随 Project 选中**（可关掉，选择记进 EditorPrefs）：
  打开窗口时若已选中资源就直接出结果，不用再手动拖进去。

### Tests
- 新增 `CAssetReferenceScannerTests`（8 条）：真的能扫出引用者、正向依赖正确、
  **单次 `Step` 不会扫完**（分帧的保证）、同目标复用结果、取消保留部分结果、
  Assets 之外的路径立即结束、内嵌面板契约。
- 新增 `CAssetSetupTests`（3 条）：`EnsureSettings` 幂等且第二次报"无改动"、
  播放模式描述与判定自洽、切换后第二次报"无改动"（并**恢复原值**，不污染工程）。
- asset 测试 31 → 42；全量 EditMode **699 → 710**。

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
