using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CoffeeBean
{
    /// <summary>
    /// 资源加载门面（默认后端 Unity Addressables，可注入替换）：
    /// - **统一缓存**：三字典（address → Object / 后端句柄 / 引用计数），缓存命中零开销
    /// - **引用计数释放**：每次加载成功 +1（含缓存命中）；Release 归零才真正释放（防泄漏）
    /// - **同步 / 异步**：LoadAsset / LoadAssetAsync（异步统一用 **UniTask**）
    /// - **批量 / 标签**：PreloadAsync / LoadAssetsByLabelAsync
    /// - **实例化**：Instantiate / InstantiateAsync
    /// - **统计**：缓存数 / 总引用数
    ///
    /// 为什么是 UniTask 而不是 C# Task：一次真实加载的开销几乎全在 I/O 与反序列化上，
    /// 但 Task 那条路**每次 await 还要额外付**——Addressables 的 <c>handle.Task</c> 每个句柄
    /// new 一个 <c>TaskCompletionSource&lt;T&gt;(RunContinuationsAsynchronously)</c>（实测 ~104 B）
    /// 并经调度器排队；更麻烦的是续体**不保证回到 Unity 主线程**
    /// （旧实现里 <c>ConfigureAwait(false)</c> 会把续体丢到线程池，紧接着 <c>Object.Instantiate</c>
    /// 就是一次潜在的主线程违规）。UniTask 的等待源是池化的、按 PlayerLoop 在主线程恢复，
    /// 顺带还能取消。**调用方写法不变**：<c>await LoadAssetAsync&lt;T&gt;(addr)</c> 照旧。
    ///
    /// 依赖 com.unity.addressables 与 com.cysharp.unitask（声明依赖，来源由消费工程提供）。
    /// </summary>
    public sealed class CAssetSystem : MonoBehaviour
    {
        private const string Tag = "CoffeeBean.Asset";
        private const string ManagerName = "[CoffeeBean] CAssetSystem";

        private static CAssetSystem _instance;

        /// <summary>资源系统单例（自动创建，DontDestroyOnLoad；EditMode 测试不自动创建）。</summary>
        public static CAssetSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<CAssetSystem>();
                }
                if (_instance == null && Application.isPlaying)
                {
                    var go = new GameObject(ManagerName);
                    _instance = go.AddComponent<CAssetSystem>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>测试用：重置单例（EditMode 测试 TearDown 调用）。</summary>
        public static void ResetInstanceForTest()
        {
            if (_instance != null)
            {
                if (Application.isPlaying) Destroy(_instance.gameObject);
                else DestroyImmediate(_instance.gameObject);
                _instance = null;
            }
        }

        /// <summary>测试用：注入单例实例（EditMode 测试 SetUp 调用，跳过自动创建）。</summary>
        internal static void SetInstanceForTest(CAssetSystem instance)
        {
            _instance = instance;
        }

        private readonly Dictionary<string, Object> _cache = new Dictionary<string, Object>();
        private readonly Dictionary<string, int> _refCounts = new Dictionary<string, int>();
        private readonly HashSet<string> _pinned = new HashSet<string>();

        private CAssetOptions _options = new CAssetOptions();
        private IAssetBackend _backend = new AddressablesAssetBackend();

        /// <summary>模块配置（首次访问 Instance 前可设置）。</summary>
        public CAssetOptions Options
        {
            get => _options;
            set => _options = value ?? new CAssetOptions();
        }

        /// <summary>资源加载后端（默认 Addressables；测试注入 mock）。</summary>
        public IAssetBackend Backend
        {
            get => _backend;
            set => _backend = value ?? new AddressablesAssetBackend();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            ReleaseAll();
        }

        // ========== 缓存查询 ==========

        /// <summary>资源是否已加载并缓存。</summary>
        public bool IsLoaded(string address)
            => !string.IsNullOrEmpty(address) && _cache.ContainsKey(address);

        /// <summary>获取资源引用计数。</summary>
        public int GetRefCount(string address)
            => !string.IsNullOrEmpty(address) && _refCounts.TryGetValue(address, out var c) ? c : 0;

        /// <summary>尝试从缓存获取（不增加引用计数，只读访问）。</summary>
        public bool TryGetCached<T>(string address, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrEmpty(address)) return false;
            if (_cache.TryGetValue(address, out var cached) && cached is T result)
            {
                asset = result;
                return true;
            }
            return false;
        }

        /// <summary>统计：缓存数 / 总引用数。</summary>
        public (int cacheCount, int totalRefCount) GetCacheStats()
        {
            int total = 0;
            foreach (var kv in _refCounts) total += kv.Value;
            return (_cache.Count, total);
        }

        // ========== 同步加载 ==========

        /// <summary>同步加载资源（缓存命中零开销）。</summary>
        public T LoadAsset<T>(string address) where T : Object
        {
            address = ResolveAddress(address);
            if (string.IsNullOrEmpty(address))
            {
                LogFail($"地址为空 [{typeof(T).Name}]");
                return null;
            }

            if (TryGetFromCache(address, out T cached)) return cached;

            if (!_backend.HasAddress(address))
            {
                if (TryEditorPathFallback(address, out T fallback, out _))
                {
                    CacheAsset(address, fallback);
                    return fallback;
                }
                LogFail($"未找到资源：{address}");
                return null;
            }

            var asset = _backend.LoadAssetSync<T>(address);
            if (asset == null)
            {
                if (TryEditorPathFallback(address, out T fallback, out _))
                {
                    CacheAsset(address, fallback);
                    return fallback;
                }
                LogFail($"同步加载失败：{address}");
                return null;
            }

            CacheAsset(address, asset);
            return asset;
        }

        // ========== 异步加载 ==========

        /// <summary>异步加载资源（UniTask；地址存在性检查 + 加载 + 缓存 + 引用计数）。</summary>
        public async UniTask<T> LoadAssetAsync<T>(string address) where T : Object
        {
            address = ResolveAddress(address);
            if (string.IsNullOrEmpty(address))
            {
                LogFail($"地址为空 [{typeof(T).Name}]");
                return null;
            }

            if (TryGetFromCache(address, out T cached)) return cached;

            bool exists = await _backend.HasAddressAsync(address);
            if (!exists)
            {
                if (TryEditorPathFallback(address, out T fallback, out _))
                {
                    CacheAsset(address, fallback);
                    return fallback;
                }
                LogFail($"未找到资源：{address}");
                return null;
            }

            var asset = await _backend.LoadAssetAsync<T>(address);
            // 保证调用方（以及后续碰 Unity API 的扩展方法/实例化）一定在主线程 ——
            // 已在主线程时这里是**立即完成**的，不产生额外一帧，也不依赖 PlayerLoop。
            await UniTask.SwitchToMainThread();
            if (asset == null)
            {
                if (TryEditorPathFallback(address, out T fallback, out _))
                {
                    CacheAsset(address, fallback);
                    return fallback;
                }
                LogFail($"异步加载失败：{address}");
                return null;
            }

            CacheAsset(address, asset);
            return asset;
        }

        /// <summary>
        /// 编辑器路径兜底：catalog 里没有这个地址时，按资源路径直接从 AssetDatabase 读。
        ///
        /// **只在编辑器编译**（`#if UNITY_EDITOR`）—— player 构建里根本没有这段代码，
        /// 所以"靠兜底才活"的地址在真机上一定失败；这也是兜底清单必须存在的原因。
        /// </summary>
        private bool TryEditorPathFallback<T>(string address, out T asset, out string resolvedPath) where T : Object
        {
            asset = null;
            resolvedPath = null;
#if UNITY_EDITOR
            if (_options == null || !_options.EditorPathFallback) return false;

            CAssetEditorPathFallback.Enabled = true;
            CAssetEditorPathFallback.Roots = _options.EditorPathFallbackRoots;

            if (!CAssetEditorPathFallback.TryLoad(address, out asset, out resolvedPath)) return false;

            CAssetEditorPathFallback.Report(address, resolvedPath);
            return true;
#else
            return false;
#endif
        }

        // ========== 标签 / 批量 ==========

        /// <summary>按标签加载全部资源（默认后端支持；mock 可简化）。</summary>
        public async UniTask<List<T>> LoadAssetsByLabelAsync<T>(string label) where T : Object
        {
            var result = new List<T>();
            if (string.IsNullOrEmpty(label)) return result;

            // 标签 → 地址集合（由后端解析；Addressables 后端用 LoadResourceLocationsAsync）
            var addresses = await ResolveLabelAddresses(label);
            await UniTask.SwitchToMainThread();
            foreach (var addr in addresses)
            {
                var asset = await LoadAssetAsync<T>(addr);
                if (asset != null) result.Add(asset);
            }
            return result;
        }

        /// <summary>批量预加载（不阻塞，全部完成后返回）。</summary>
        public async UniTask PreloadAsync(IEnumerable<string> addresses)
        {
            if (addresses == null) return;
            // UniTask.WhenAll 只在有多个任务时才付分配（单个直接返回），不像 Task.WhenAll 恒定分配数组+组合任务
            var tasks = addresses.Select(a => LoadAssetAsync<Object>(a)).ToArray();
            if (tasks.Length == 0) return;
            await UniTask.WhenAll(tasks);
        }

        // ========== 实例化 ==========

        /// <summary>同步实例化 GameObject。</summary>
        public GameObject Instantiate(string address, Transform parent = null, bool worldPosStays = false)
        {
            var prefab = LoadAsset<GameObject>(address);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, worldPosStays);
            go.name = prefab.name;
            return go;
        }

        /// <summary>异步实例化 GameObject。</summary>
        public async UniTask<GameObject> InstantiateAsync(string address, Transform parent = null, bool worldPosStays = false)
        {
            var prefab = await LoadAssetAsync<GameObject>(address);
            // Object.Instantiate 是主线程 API —— 显式收口（旧实现 ConfigureAwait(false) 后可能已经在
            // 线程池上，这里就是一次真实的"主线程违规"隐患）
            await UniTask.SwitchToMainThread();
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, worldPosStays);
            go.name = prefab.name;
            return go;
        }

        // ========== 场景 ==========

        /// <summary>异步加载场景（Addressables；mock 返回 default）。</summary>
        public async UniTask<Scene> LoadSceneAsync(string address, LoadSceneMode mode = LoadSceneMode.Single, bool activateOnLoad = true)
        {
            // 场景加载仍直接走 Addressables（后端抽象暂不含场景）
            var handle = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(address, mode, activateOnLoad);
            await handle.ToUniTask();
            await UniTask.SwitchToMainThread();
            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                LogFail($"场景加载失败：{address}");
                return default;
            }
            return handle.Result.Scene;
        }

        // ========== 释放 ==========

        /// <summary>按引用计数释放指定资源（计数 -1，归零释放；常驻资源不释放）。</summary>
        public void Release(string address)
        {
            address = ResolveAddress(address);
            if (!_refCounts.ContainsKey(address)) return;

            _refCounts[address]--;
            if (_refCounts[address] <= 0)
            {
                // 常驻资源（Pin）不被 Release 归零释放，直到 Unpin
                if (!_pinned.Contains(address))
                {
                    ReleaseInternal(address);
                }
            }
        }

        /// <summary>强制释放（忽略引用计数；常驻资源也强制释放并解除常驻）。</summary>
        public void ForceRelease(string address)
        {
            address = ResolveAddress(address);
            _pinned.Remove(address);
            ReleaseInternal(address);
        }

        /// <summary>释放所有引用计数 ≤ 0 的闲置资源，返回释放数量（常驻资源跳过）。</summary>
        public int ReleaseUnused()
        {
            var toRelease = _refCounts
                .Where(kv => kv.Value <= 0 && !_pinned.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var addr in toRelease)
            {
                ReleaseInternal(addr);
            }
            if (toRelease.Count > 0)
            {
                CLog.Info(Tag, $"释放了 {toRelease.Count} 个闲置资源");
            }
            return toRelease.Count;
        }

        /// <summary>资源是否常驻（Pin 中）。</summary>
        public bool IsPinned(string address)
            => !string.IsNullOrEmpty(address) && _pinned.Contains(address);

        /// <summary>
        /// 常驻资源：加载并标记为不被闲置清理/Release 释放（如常驻字体、全局图标）。
        /// 返回资源；加载失败返回 null。
        /// </summary>
        public T Pin<T>(string address) where T : Object
        {
            address = ResolveAddress(address);
            T asset = LoadAsset<T>(address);
            if (asset != null) _pinned.Add(address);
            return asset;
        }

        /// <summary>解除常驻（之后 Release 归零或 ReleaseUnused 可释放）。</summary>
        public void Unpin(string address)
        {
            address = ResolveAddress(address);
            _pinned.Remove(address);
        }

        /// <summary>释放所有资源。</summary>
        public void ReleaseAll()
        {
            foreach (var kv in _cache)
            {
                _backend.Release(kv.Key, kv.Value);
            }
            _cache.Clear();
            _refCounts.Clear();
            _backend.ReleaseAll();
        }

        // ========== 内部 ==========

        private bool TryGetFromCache<T>(string address, out T result) where T : Object
        {
            result = null;
            if (_cache.TryGetValue(address, out var cached) && cached is T res)
            {
                _refCounts[address]++;
                result = res;
                return true;
            }
            return false;
        }

        private void CacheAsset(string address, Object asset)
        {
            if (_cache.ContainsKey(address))
            {
                _refCounts[address]++;
            }
            else
            {
                _cache[address] = asset;
                _refCounts[address] = 1;
            }
        }

        private void ReleaseInternal(string address)
        {
            if (_cache.TryGetValue(address, out var asset))
            {
                _backend.Release(address, asset);
            }
            _cache.Remove(address);
            _refCounts.Remove(address);
        }

        /// <summary>标签 → 地址集合（默认后端用 Addressables 解析）。</summary>
        private async UniTask<List<string>> ResolveLabelAddresses(string label)
        {
            var result = new List<string>();
            var locations = await UnityEngine.AddressableAssets.Addressables
                .LoadResourceLocationsAsync(label).ToUniTask();
            if (locations == null) return result;
            foreach (var loc in locations) result.Add(loc.PrimaryKey);
            return result;
        }

        /// <summary>应用地址前缀。</summary>
        private string ResolveAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return address;
            if (string.IsNullOrEmpty(_options.AddressPrefix)) return address;
            if (address.StartsWith(_options.AddressPrefix, StringComparison.Ordinal)) return address;
            return _options.AddressPrefix + "/" + address;
        }

        private void LogFail(string message)
        {
            if (_options.FailSilently)
            {
                CLog.Warn(Tag, message);
            }
            else
            {
                CLog.Error(Tag, message);
            }
        }
    }
}
