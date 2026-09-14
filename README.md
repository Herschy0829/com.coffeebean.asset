# CoffeeBean Asset（com.coffeebean.asset）

CoffeeBean 框架的资源管理模块：**统一封装 Unity Addressables**。

- **统一加载门面 `CAssetSystem`**：同步/异步加载、实例化、预加载、标签加载、场景加载、引用计数释放、统计
- **可插拔式设计**：基于 Addressables（官方标准），声明依赖来源消费方定
- **组件绑定 `CAssetExtensions`**：Image/Button/SpriteRenderer 加载 Sprite、TMP_Text/Text 动态字体、AudioSource 加载音频（统一走 CAssetSystem 共享缓存，防泄漏）
- **更新下载 `CCatalogUpdater`**：检测 catalog 更新 → 下载（进度回调）→ 失败重试
- **零额外依赖**：仅依赖 `com.coffeebean.tools` + `com.unity.addressables`（声明依赖）

> 设计文档：`docs/design-asset.md`

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.asset": "https://github.com/Herschy0829/com.coffeebean.asset.git#v0.2.3",
    "com.coffeebean.tools": "https://github.com/Herschy0829/com.coffeebean.tools.git#v0.5.0",
    "com.unity.addressables": "2.9.1"  // 或本地副本；来源消费方定
  }
}
```

> `com.unity.addressables` 仅**声明依赖**，来源由消费工程提供（Package Manager 经 Unity Registry 解析，或本地副本）。

## 快速使用

```csharp
using CoffeeBean;

// 同步加载（缓存命中零开销；引用计数 +1）
var sprite = CAssetSystem.Instance.LoadAsset<Sprite>("Assets/UI/icon_gold.png");

// 异步加载（C# Task）
Sprite sprite2 = await CAssetSystem.Instance.LoadAssetAsync<Sprite>("Assets/UI/icon_gem.png");

// 实例化
GameObject go = await CAssetSystem.Instance.InstantiateAsync("Assets/Prefabs/Enemy.prefab", parent);

// 预加载 / 标签
await CAssetSystem.Instance.PreloadAsync(new[] { "a.png", "b.png" });
var list = await CAssetSystem.Instance.LoadAssetsByLabelAsync<Sprite>("icons");

// 释放（引用计数归零才真正释放）
CAssetSystem.Instance.Release("Assets/UI/icon_gold.png");
int freed = CAssetSystem.Instance.ReleaseUnused(); // 释放闲置
var (cache, totalRef) = CAssetSystem.Instance.GetCacheStats();

// 组件绑定
image.LoadSprite("Assets/UI/icon_gold.png");          // Image
tmpText.LoadFont("Assets/Fonts/MyFont.ttf");          // TMP 动态字体
audioSource.LoadClip("Assets/Audio/click.wav");       // AudioClip

// 更新下载
var updater = new CCatalogUpdater { MaxRetry = 3 };
bool ok = await updater.UpdateAsync(progress: p => Debug.Log($"下载 {p:P0}"));
```

## 引用计数语义

- 每次 `LoadAsset/Async` 成功 → 计数 +1（**含缓存命中**）
- `Release` → 计数 -1；归零 → 真正释放（`Addressables.Release`）
- `ForceRelease` 忽略计数强制释放；`ReleaseUnused` 释放计数 ≤ 0 的闲置资源
- 组件扩展（`Image.LoadSprite` 等）自动走同一缓存与计数

## 目录结构

```
Runtime/
├── CAssetSystem.cs         资源门面（加载/实例化/预加载/标签/释放/统计）
├── CAssetOptions.cs        配置（地址前缀、失败策略、TMP 字体参数）
├── CAssetExtensions.cs     组件绑定（Image/TMP_Text/Text/SpriteRenderer/AudioClip）
├── CCatalogUpdater.cs      更新下载服务
└── Bridge/                 Core 可选集成
Editor/
└── CAssetSetup.cs          AddressableAssetSettings 初始化
```

## 测试

EditMode 测试（临时创建测试 Addressable 资源与 group）：缓存语义 / 引用计数 / 释放归零 / 强制释放 / 闲置释放 / 异步加载 / 实例化 / 失败容错 / 统计 / 组件绑定。

## 版本约定

- SemVer + git tag `vX.Y.Z`；每个版本对应 GitHub Release（CHANGELOG 派生说明）
