using UnityEngine;

namespace CoffeeBean.Asset.Demo
{
    /// <summary>
    /// 资源模块示例（场景挂载后运行，IMGUI 演示）：
    /// 加载 / 释放（引用计数）、实例化、统计。
    /// 说明：地址需要工程里有对应的 Addressable 资源（示例用自身配置的地址，无资源时返回 null 并告警）。
    /// </summary>
    public sealed class AssetDemo : MonoBehaviour
    {
        private string _address = "Assets/DemoAssets/TestSprite.png";
        private string _status = "就绪";
        private Sprite _loaded;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 520, 340));

            GUILayout.Label("<b>CoffeeBean Asset 演示（Addressables）</b>");
            _address = GUILayout.TextField("资源地址:", _address);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("同步加载", GUILayout.Width(100), GUILayout.Height(30)))
            {
                _loaded = CAssetSystem.Instance.LoadAsset<Sprite>(_address);
                UpdateStatus(_loaded != null ? $"已加载（缓存 {CAssetSystem.Instance.GetCacheStats().cacheCount} 个）" : "加载失败（地址不存在？）");
            }
            if (GUILayout.Button("释放", GUILayout.Width(100), GUILayout.Height(30)))
            {
                CAssetSystem.Instance.Release(_address);
                _loaded = null;
                UpdateStatus("已释放一次");
            }
            if (GUILayout.Button("统计", GUILayout.Width(100), GUILayout.Height(30)))
            {
                var (cache, total) = CAssetSystem.Instance.GetCacheStats();
                UpdateStatus($"缓存 {cache} 个资源，总引用 {total}");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (_loaded != null)
            {
                GUILayout.Label($"Sprite: {_loaded.name} ({_loaded.texture.width}x{_loaded.texture.height})");
            }

            GUILayout.Space(8);
            GUILayout.Label("说明：");
            GUILayout.Label("· 每次加载成功引用计数 +1（含缓存命中），Release 归零才真正释放；");
            GUILayout.Label("· 异步 API（LoadAssetAsync/InstantiateAsync/PreloadAsync）基于 C# Task；");
            GUILayout.Label("· 组件绑定：Image.LoadSprite(address) / TMP_Text.LoadFont(address) 等扩展；");
            GUILayout.Label("· 更新下载：new CCatalogUpdater().UpdateAsync(progress) 检测并下载新内容。");
            GUILayout.Label($"\n{_status}");

            GUILayout.EndArea();
        }

        private void UpdateStatus(string msg)
        {
            _status = msg;
            Debug.Log("[AssetDemo] " + msg);
        }
    }
}
