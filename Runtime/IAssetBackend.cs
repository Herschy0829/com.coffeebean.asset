using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CoffeeBean
{
    /// <summary>
    /// 资源加载后端抽象（默认 Addressables；测试可注入 mock，未来可接其他方案）。
    /// CAssetSystem 的缓存/引用计数/释放语义与后端解耦。
    ///
    /// 异步统一用 <see cref="UniTask"/>（不是 C# Task）：
    /// · 等待源池化 —— 预热后每次操作**不再分配** TaskCompletionSource/Task；
    /// · 按 PlayerLoop 在**主线程**恢复 —— 不经过 SynchronizationContext，
    ///   也就没有"ConfigureAwait(false) 把续体丢到线程池、之后再碰 Unity API 就炸"的老问题；
    /// · 顺带拿到取消能力（<c>ToUniTask(cancellationToken:)</c>）。
    /// </summary>
    public interface IAssetBackend
    {
        /// <summary>同步加载资源。</summary>
        T LoadAssetSync<T>(string address) where T : Object;

        /// <summary>异步加载资源。</summary>
        UniTask<T> LoadAssetAsync<T>(string address) where T : Object;

        /// <summary>地址是否存在（同步）。</summary>
        bool HasAddress(string address);

        /// <summary>地址是否存在（异步）。</summary>
        UniTask<bool> HasAddressAsync(string address);

        /// <summary>释放资源句柄。</summary>
        void Release(string address, Object asset);

        /// <summary>释放全部。</summary>
        void ReleaseAll();
    }

    /// <summary>默认后端：Unity Addressables 实现。</summary>
    public sealed class AddressablesAssetBackend : IAssetBackend
    {
        private readonly System.Collections.Generic.Dictionary<string, AsyncOperationHandle> _handles =
            new System.Collections.Generic.Dictionary<string, AsyncOperationHandle>();

        public T LoadAssetSync<T>(string address) where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(address);
            handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Addressables.Release(handle);
                return null;
            }
            _handles[address] = handle;
            return handle.Result;
        }

        public async UniTask<T> LoadAssetAsync<T>(string address) where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(address);
            // 直接等句柄（ToUniTask = 池化等待源 + PlayerLoop 主线程恢复），
            // **不要**走 handle.Task：那条路每个句柄都会 new TaskCompletionSource<T>(RunContinuationsAsynchronously)
            // 并经过调度器排队，且续体不保证在 Unity 主线程上（旧实现用 ConfigureAwait(false) 把续体丢到线程池）。
            await handle.ToUniTask();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Addressables.Release(handle);
                return null;
            }
            _handles[address] = handle;
            return handle.Result;
        }

        public bool HasAddress(string address)
        {
            var handle = Addressables.LoadResourceLocationsAsync(address, typeof(Object));
            handle.WaitForCompletion();
            bool exists = handle.Status == AsyncOperationStatus.Succeeded
                          && handle.Result != null && handle.Result.Count > 0;
            Addressables.Release(handle);
            return exists;
        }

        public async UniTask<bool> HasAddressAsync(string address)
        {
            var handle = Addressables.LoadResourceLocationsAsync(address, typeof(Object));
            await handle.ToUniTask();
            bool exists = handle.Status == AsyncOperationStatus.Succeeded
                          && handle.Result != null && handle.Result.Count > 0;
            Addressables.Release(handle);
            return exists;
        }

        public void Release(string address, Object asset)
        {
            if (_handles.TryGetValue(address, out var handle))
            {
                if (handle.IsValid()) Addressables.Release(handle);
                _handles.Remove(address);
            }
        }

        public void ReleaseAll()
        {
            foreach (var kv in _handles)
            {
                if (kv.Value.IsValid()) Addressables.Release(kv.Value);
            }
            _handles.Clear();
        }
    }
}
