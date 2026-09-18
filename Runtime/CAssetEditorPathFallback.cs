#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoffeeBean
{
    /// <summary>
    /// **编辑器专用**的资源路径兜底：catalog 里查不到的地址，直接按资源路径从 AssetDatabase 读出来。
    ///
    /// 目的：迭代期不用"每加一个资源就手工往 Addressables group 里加一条"。
    /// 例如配置表里写 `Building/Building_1002`，只要磁盘上存在
    /// `Assets/.../Building_1002.prefab`，编辑器里就能直接加载出来。
    ///
    /// **它不是 FastMode 的能力** —— FastMode 依然只认 catalog（key → 资源位置），
    /// 只是把"从 bundle 读"换成"从 AssetDatabase 读"。真正"按路径读"是这个类做的，
    /// 而且**只存在于编辑器**（整个文件包在 `#if UNITY_EDITOR` 里，player 构建里没有这段代码）。
    ///
    /// **代价**：靠兜底才加载成功的地址，打包后一定失败（catalog 里没有它）。
    /// 所以每次兜底都会：
    /// 1. 记进 <see cref="Recorded"/>（并持久化到 EditorPrefs，**跨进出 Play 模式的域重载**还在）；
    /// 2. 打一条 Warning（同一地址只报一次），把实际解析到的路径写清楚。
    /// Hub 的「Addressables 设置」面板里会把这份清单列出来 —— 出包前照着核对。
    /// </summary>
    public static class CAssetEditorPathFallback
    {
        private const string PrefsKey = "CoffeeBean.Asset.EditorFallback.Recorded";
        private const int MaxRecorded = 2000;

        /// <summary>开关（由 CAssetSystem 从 CAssetOptions.EditorPathFallback 同步过来）。</summary>
        public static bool Enabled = true;

        /// <summary>搜索根目录；默认整个 Assets。</summary>
        public static string[] Roots = { "Assets" };

        /// <summary>地址 → 实际解析到的资源路径（按首次兜底顺序）。</summary>
        public static readonly Dictionary<string, string> Recorded = new Dictionary<string, string>();

        private static Dictionary<string, string> _pathIndex;   // 去掉扩展名的路径（小写）→ 真实路径
        private static bool _indexBuilt;
        private static bool _reportedMissingIndex;

        static CAssetEditorPathFallback()
        {
            LoadRecorded();
            // 资源库变化后索引作废（新增资源能被下一次查找看到）
            EditorApplication.projectChanged += InvalidateIndex;
        }

        /// <summary>清空兜底清单（面板上"清单已核对完"时用）。</summary>
        public static void ClearRecorded()
        {
            Recorded.Clear();
            EditorPrefs.SetString(PrefsKey, string.Empty);
        }

        /// <summary>把清单整理成给人看的多行文本。</summary>
        public static string DescribeRecorded()
        {
            if (Recorded.Count == 0) return "（没有地址走过编辑器路径兜底）";

            var sb = new StringBuilder();
            sb.AppendLine($"以下 {Recorded.Count} 个地址在 catalog 里查不到，是编辑器按路径直读兜底的 —— 打包后它们会失败：");
            foreach (KeyValuePair<string, string> kv in Recorded)
            {
                sb.AppendLine($"  \"{kv.Key}\"  →  {kv.Value}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>让路径索引作废（新增/删除/移动资源后重新枚举）。</summary>
        public static void InvalidateIndex()
        {
            _pathIndex = null;
            _indexBuilt = false;
        }

        /// <summary>
        /// 按地址（路径形状）找资源。命中返回 true 并输出资源与实际路径。
        /// 匹配规则：忽略扩展名，按**路径后缀**匹配（`Building/Building_1002` 能命中
        /// `Assets/Art/Building/Building_1002.prefab`）；也接受 `Assets/...` 完整路径。
        /// </summary>
        public static bool TryLoad<T>(string address, out T asset, out string resolvedPath) where T : Object
        {
            asset = null;
            resolvedPath = null;
            if (string.IsNullOrEmpty(address)) return false;

            string normalized = address.Replace('\\', '/').TrimStart('/');

            // 1) 完整路径直读（最快，也是配置里最常见的形式）
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal) ||
                normalized.StartsWith("Packages/", StringComparison.Ordinal))
            {
                asset = AssetDatabase.LoadAssetAtPath<T>(normalized);
                if (asset != null)
                {
                    resolvedPath = normalized;
                    return true;
                }
            }

            // 2) 后缀匹配（去掉扩展名的索引）
            Dictionary<string, string> index = GetPathIndex();
            if (index != null)
            {
                string suffix = "/" + normalized.ToLowerInvariant();
                foreach (KeyValuePair<string, string> kv in index)
                {
                    if (!kv.Key.EndsWith(suffix, StringComparison.Ordinal)) continue;

                    asset = AssetDatabase.LoadAssetAtPath<T>(kv.Value);
                    if (asset != null)
                    {
                        resolvedPath = kv.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>记一次兜底（去重、持久化、打一次警告）。</summary>
        public static void Report(string address, string resolvedPath)
        {
            if (string.IsNullOrEmpty(address)) return;

            bool isNew = !Recorded.ContainsKey(address);
            Recorded[address] = resolvedPath;
            if (isNew)
            {
                SaveRecorded();
                Debug.LogWarning(
                    $"[CoffeeBean.Asset] 地址 \"{address}\" 不在 Addressables catalog 里，" +
                    $"编辑器按路径兜底加载了 {resolvedPath}。\n" +
                    "打包后这个地址会失败 —— 请在出包前把它加进 group（Hub → Addressables 设置 → 编辑器兜底清单）。");
            }
        }

        // ========== 内部 ==========

        private static Dictionary<string, string> GetPathIndex()
        {
            if (_indexBuilt) return _pathIndex;

            _indexBuilt = true;
            _pathIndex = new Dictionary<string, string>(StringComparer.Ordinal);

            string[] roots = (Roots == null || Roots.Length == 0) ? new[] { "Assets" } : Roots;
            string[] guids;
            try
            {
                guids = AssetDatabase.FindAssets(string.Empty, roots);
            }
            catch (Exception e)
            {
                if (!_reportedMissingIndex)
                {
                    _reportedMissingIndex = true;
                    Debug.LogWarning($"[CoffeeBean.Asset] 建立路径索引失败（兜底将只支持完整路径）：{e.Message}");
                }
                return _pathIndex;
            }

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                string withoutExtension = StripExtension(path).ToLowerInvariant();
                if (!_pathIndex.ContainsKey(withoutExtension)) _pathIndex[withoutExtension] = path;
            }
            return _pathIndex;
        }

        private static string StripExtension(string path)
        {
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            return dot > slash ? path.Substring(0, dot) : path;
        }

        private static void LoadRecorded()
        {
            Recorded.Clear();
            try
            {
                string raw = EditorPrefs.GetString(PrefsKey, string.Empty);
                if (string.IsNullOrEmpty(raw)) return;

                foreach (string line in raw.Split('\n'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    Recorded[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoffeeBean.Asset] 读取兜底清单失败：{e.Message}");
            }
        }

        private static void SaveRecorded()
        {
            try
            {
                var sb = new StringBuilder();
                int written = 0;
                foreach (KeyValuePair<string, string> kv in Recorded)
                {
                    if (written++ >= MaxRecorded) break;
                    sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
                }
                EditorPrefs.SetString(PrefsKey, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoffeeBean.Asset] 保存兜底清单失败：{e.Message}");
            }
        }
    }
}
#endif
