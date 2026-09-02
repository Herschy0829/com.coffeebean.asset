using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 组件自动释放钩子：挂到 MonoBehaviour 上，把加载的资源地址登记进来，
    /// 组件销毁（OnDestroy）时自动调用 CAssetSystem.Release 释放引用（防泄漏）。
    ///
    /// 用法：加载资源后 <c>GetComponent&lt;CAutoRelease&gt;().Track(address)</c>；
    /// 或直接用便捷扩展 <see cref="TrackRelease"/>（自动挂载 + 登记）。
    /// 示例：<c>var sprite = CAssetSystem.Instance.LoadAsset&lt;Sprite&gt;(addr); go.TrackRelease(addr);</c>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CAutoRelease : MonoBehaviour
    {
        private readonly List<string> _addresses = new List<string>();
        private bool _tracking;

        /// <summary>登记一个已加载的资源地址（销毁时自动 Release）。</summary>
        public void Track(string address)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (!_addresses.Contains(address)) _addresses.Add(address);
            _tracking = true;
        }

        /// <summary>登记多个地址。</summary>
        public void Track(IEnumerable<string> addresses)
        {
            if (addresses == null) return;
            foreach (var a in addresses) Track(a);
        }

        /// <summary>是否登记了资源。</summary>
        public bool HasTracked => _addresses.Count > 0;

        /// <summary>登记的地址数。</summary>
        public int TrackedCount => _addresses.Count;

        private void OnDestroy()
        {
            // 运行时销毁自动释放（编辑模式 DestroyImmediate 可能不触发 OnDestroy，
            // 测试/编辑器脚本请显式调用 ReleaseAll）
            ReleaseAll();
        }

        /// <summary>释放全部登记资源并清空（测试/手动销毁流程调用；OnDestroy 自动调用）。</summary>
        public void ReleaseAll()
        {
            if (!_tracking) return;
            _tracking = false;

            var system = CAssetSystem.Instance;
            if (system == null) return;

            foreach (var address in _addresses)
            {
                system.Release(address);
            }
            _addresses.Clear();
        }
    }

    /// <summary>自动释放便捷扩展。</summary>
    public static class CAutoReleaseExtensions
    {
        /// <summary>把已加载的资源地址登记到组件的自动释放（无组件自动挂一个），返回组件。</summary>
        public static CAutoRelease TrackRelease(this GameObject go, string address)
        {
            var hook = go.GetComponent<CAutoRelease>();
            if (hook == null) hook = go.AddComponent<CAutoRelease>();
            hook.Track(address);
            return hook;
        }

        /// <summary>把已加载的资源地址登记到组件的自动释放（组件所在 GameObject）。</summary>
        public static CAutoRelease TrackRelease(this Component component, string address)
            => component != null ? component.gameObject.TrackRelease(address) : null;
    }
}
