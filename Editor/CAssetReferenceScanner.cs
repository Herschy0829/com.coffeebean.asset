using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 反向依赖扫描器（"谁引用了我"）：**分帧增量**执行，绝不在 OnGUI 里做全工程扫描。
    ///
    /// **为什么必须这样**：一个 5000+ 资源的工程，逐个 <c>GetDependencies</c> 实测约 1.3 ms/项
    /// → 一次全量约 6.8 秒。这段逻辑原来直接写在 <c>OnGUI</c> 里，而 OnGUI 每帧至少跑
    /// Layout + Repaint **两次**，于是窗口一打开就永久卡在扫描里 —— 用户看到的"点按钮没反应"
    /// 就是这么来的（不是按钮坏了，是连一帧都画不完）。
    ///
    /// 现在：每帧只花一小段预算（默认 10 ms），界面全程可响应、可取消、有进度，
    /// 结果**边扫边出**。同一目标的结果会被复用，不再重复扫描。
    ///
    /// 与 GUI 解耦（不引用任何 EditorWindow / GUILayout），测试可以直接 Begin/Step 驱动。
    /// </summary>
    public sealed class CAssetReferenceScanner
    {
        /// <summary>单次 <see cref="Step"/> 最多占用多少毫秒（剩下的留给编辑器交互与渲染）。</summary>
        public double BudgetMillisecondsPerStep = 10.0;

        /// <summary>正在扫描的目标资源路径。</summary>
        public string TargetPath { get; private set; }

        /// <summary>是否还在扫描。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>是否已经有结果（扫描完成，或因参数不合法而立即结束）。</summary>
        public bool IsDone { get; private set; }

        /// <summary>已扫描的候选资源数。</summary>
        public int ScannedCount { get; private set; }

        /// <summary>候选资源总数。</summary>
        public int TotalCount { get; private set; }

        /// <summary>本次扫描累计耗时（毫秒）。</summary>
        public double ElapsedMilliseconds { get; private set; }

        /// <summary>反向引用：引用了目标资源的资源（扫描过程中会逐步增加）。</summary>
        public List<string> Referencers { get; private set; }

        /// <summary>正向依赖：目标资源依赖的资源（一次调用即可得到）。</summary>
        public List<string> Dependencies { get; private set; }

        /// <summary>进度 0~1。</summary>
        public float Progress
        {
            get
            {
                if (IsDone) return 1f;
                if (TotalCount <= 0) return 0f;
                return (float)ScannedCount / TotalCount;
            }
        }

        private string[] _candidates = new string[0];
        private int _index;
        private string _targetGuid;
        private string _invalidationKey;
        private System.Diagnostics.Stopwatch _stopwatch;

        /// <summary>
        /// 目标资源是否还是上次扫描的那一个（用于"同一目标不重复扫描"）。
        /// 用 *guid + 依赖哈希* 作为键：资源内容变了也会重新扫描。
        /// </summary>
        public bool IsResultFor(string targetPath)
        {
            if (!IsDone || string.IsNullOrEmpty(targetPath)) return false;
            return string.Equals(TargetPath, targetPath, StringComparison.Ordinal)
                   && _invalidationKey == BuildInvalidationKey(targetPath);
        }

        /// <summary>开始扫描（会丢弃上一次的结果）。</summary>
        public void Begin(string targetPath)
        {
            TargetPath = targetPath;
            Referencers = new List<string>();
            Dependencies = new List<string>();
            ScannedCount = 0;
            TotalCount = 0;
            ElapsedMilliseconds = 0;
            IsRunning = false;
            IsDone = false;
            _index = 0;
            _candidates = new string[0];
            _targetGuid = null;
            _invalidationKey = null;

            // 只处理 Assets 下的工程资源（Package 里的资源没有"谁引用我"的意义）
            if (string.IsNullOrEmpty(targetPath) || !targetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                IsDone = true;
                return;
            }

            Dependencies = CollectForwardDependencies(targetPath);
            _targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            _invalidationKey = BuildInvalidationKey(targetPath);
            _candidates = CollectCandidates(targetPath);
            TotalCount = _candidates.Length;

            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            IsRunning = TotalCount > 0;
            if (!IsRunning) Finish();
        }

        /// <summary>
        /// 推进一帧的工作量。返回 true 表示**还需要继续**调用（界面应安排下一帧再来一次）。
        /// </summary>
        public bool Step()
        {
            if (!IsRunning) return false;

            long budgetTicks = (long)(BudgetMillisecondsPerStep * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            while (_index < _candidates.Length)
            {
                string assetPath = _candidates[_index];
                _index++;
                ScannedCount++;

                if (References(assetPath))
                {
                    Referencers.Add(assetPath);
                }

                if (System.Diagnostics.Stopwatch.GetTimestamp() - started >= budgetTicks) break;
            }

            if (_index >= _candidates.Length) Finish();
            return IsRunning;
        }

        /// <summary>取消扫描（保留已扫到的部分结果）。</summary>
        public void Cancel()
        {
            if (!IsRunning) return;
            IsRunning = false;
            IsDone = true;
            StopTimer();
        }

        private void Finish()
        {
            IsRunning = false;
            IsDone = true;
            StopTimer();
            Referencers.Sort(StringComparer.Ordinal);
        }

        private void StopTimer()
        {
            if (_stopwatch == null) return;
            _stopwatch.Stop();
            ElapsedMilliseconds = _stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>目标资源是否被 <paramref name="assetPath"/> 引用。</summary>
        private bool References(string assetPath)
        {
            string[] deps = AssetDatabase.GetDependencies(assetPath, false);
            if (deps == null || deps.Length == 0) return false;

            // 快路径：直接命中主资源路径（绝大多数情况）
            for (int i = 0; i < deps.Length; i++)
            {
                if (string.Equals(deps[i], TargetPath, StringComparison.Ordinal)) return true;
            }

            // 慢路径：子资源 / 路径写法不同时按 GUID 比对（与原实现语义一致，避免漏报）
            if (string.IsNullOrEmpty(_targetGuid)) return false;
            for (int i = 0; i < deps.Length; i++)
            {
                if (AssetDatabase.AssetPathToGUID(deps[i]) == _targetGuid) return true;
            }
            return false;
        }

        private static List<string> CollectForwardDependencies(string targetPath)
        {
            var result = new List<string>();
            string[] deps = AssetDatabase.GetDependencies(targetPath, false);
            if (deps == null) return result;

            for (int i = 0; i < deps.Length; i++)
            {
                string dep = deps[i];
                if (string.IsNullOrEmpty(dep)) continue;
                if (string.Equals(dep, targetPath, StringComparison.Ordinal)) continue;
                if (!dep.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                result.Add(dep);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static string[] CollectCandidates(string targetPath)
        {
            var result = new List<string>();
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (string.Equals(path, targetPath, StringComparison.Ordinal)) continue;
                result.Add(path);
            }
            return result.ToArray();
        }

        /// <summary>资源内容变更后旧结果应作废：把依赖哈希拼进键里。</summary>
        private static string BuildInvalidationKey(string targetPath)
        {
            Hash128 hash = AssetDatabase.GetAssetDependencyHash(targetPath);
            return hash.ToString();
        }
    }
}
