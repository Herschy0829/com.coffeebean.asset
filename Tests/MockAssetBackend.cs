using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// 测试用 mock 后端：内存字典管理资源，不依赖 Addressables 初始化。
    /// 验证 CAssetSystem 的缓存/引用计数/释放语义。
    /// </summary>
    public sealed class MockAssetBackend : IAssetBackend
    {
        private readonly Dictionary<string, Object> _assets = new Dictionary<string, Object>();
        private readonly HashSet<string> _loaded = new HashSet<string>();

        public int LoadSyncCount { get; private set; }
        public int LoadAsyncCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public int ReleaseAllCount { get; private set; }

        /// <summary>注册资源（address → asset）。</summary>
        public void Register(string address, Object asset) => _assets[address] = asset;

        /// <summary>移除资源（模拟地址不存在）。</summary>
        public void Unregister(string address) => _assets.Remove(address);

        public bool HasAddress(string address) => _assets.ContainsKey(address);

        public async UniTask<bool> HasAddressAsync(string address)
        {
            await UniTask.CompletedTask;
            return _assets.ContainsKey(address);
        }

        public T LoadAssetSync<T>(string address) where T : Object
        {
            LoadSyncCount++;
            if (!_assets.TryGetValue(address, out var asset) || !(asset is T t)) return null;
            _loaded.Add(address);
            return t;
        }

        public async UniTask<T> LoadAssetAsync<T>(string address) where T : Object
        {
            LoadAsyncCount++;
            // 同步完成（UniTask.CompletedTask 不切线程、不需要 PlayerLoop）——
            // EditMode 下没有 PlayerLoop 可泵，任何真会让帧的等待都会挂住测试
            await UniTask.CompletedTask;
            if (!_assets.TryGetValue(address, out var asset) || !(asset is T t)) return null;
            _loaded.Add(address);
            return t;
        }

        public void Release(string address, Object asset)
        {
            ReleaseCount++;
            _loaded.Remove(address);
        }

        public void ReleaseAll()
        {
            ReleaseAllCount++;
            _loaded.Clear();
        }

        public bool IsLoaded(string address) => _loaded.Contains(address);
        public int LoadedCount => _loaded.Count;
    }
}
