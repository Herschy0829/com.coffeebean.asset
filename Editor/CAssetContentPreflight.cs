using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 一条 Addressables 条目的最小快照（喂给 <see cref="CAssetContentPreflight.Evaluate"/>，便于单测）。
    /// </summary>
    public struct CAssetEntryRecord
    {
        public string Address;
        public string AssetPath;
        public string GroupName;
        /// <summary>资源是否还在（资源被删但条目留在 group 里是打包失败的常见原因）。</summary>
        public bool AssetExists;
    }

    /// <summary>
    /// 一个 Profile 变量的最小快照（名字 + **原始值**，原始值里才有 <c>[变量]</c> / <c>{路径}</c> 语法）。
    /// </summary>
    public struct CAssetProfileValueRecord
    {
        public string Name;
        public string RawValue;
    }

    /// <summary>打包预检结果。</summary>
    public sealed class CAssetPreflightReport
    {
        /// <summary>同一个 address 出现多次（catalog 里后者覆盖前者，只有运行期才 LogError）。</summary>
        public List<string> DuplicateAddresses = new List<string>();

        /// <summary>address 为空的条目。</summary>
        public List<string> EmptyAddresses = new List<string>();

        /// <summary>资源已不存在（条目是死的）—— 构建会报错的那类。</summary>
        public List<string> DeadEntries = new List<string>();

        /// <summary>未定义的 Profile 变量名（会被 Unity **静默吃掉**，路径就错了 —— 见下方说明）。</summary>
        public List<string> UnresolvedProfileVariables = new List<string>();

        /// <summary>空 group（不致命，但通常是漏配地址的信号）。</summary>
        public List<string> EmptyGroups = new List<string>();

        public int GroupCount;
        public int EntryCount;

        /// <summary>前 20 条明细就够定位问题，避免面板被刷爆。</summary>
        public const int MaxItemsPerCategory = 20;

        public bool HasProblems =>
            DuplicateAddresses.Count > 0 || EmptyAddresses.Count > 0 ||
            DeadEntries.Count > 0 || UnresolvedProfileVariables.Count > 0;

        /// <summary>给面板/Console 用的一行摘要。</summary>
        public string Summary =>
            $"条目 {EntryCount} / 组 {GroupCount}；重复地址 {DuplicateAddresses.Count}、" +
            $"空地址 {EmptyAddresses.Count}、失效条目 {DeadEntries.Count}、" +
            $"未定义 Profile 变量 {UnresolvedProfileVariables.Count}、空组 {EmptyGroups.Count}";

        /// <summary>多行明细（无问题时给一句"没发现问题"）。</summary>
        public string Details()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Summary);
            AppendList(sb, "重复地址", DuplicateAddresses);
            AppendList(sb, "空地址", EmptyAddresses);
            AppendList(sb, "失效条目（资源已不存在）", DeadEntries);
            AppendList(sb, "未定义的 Profile 变量", UnresolvedProfileVariables);
            AppendList(sb, "空 group", EmptyGroups);
            if (!HasProblems) sb.AppendLine("没有发现会让打包失败的问题。");
            return sb.ToString().TrimEnd();
        }

        private static void AppendList(StringBuilder sb, string title, List<string> items)
        {
            if (items == null || items.Count == 0) return;
            sb.AppendLine($"· {title}（{items.Count}）：");
            int shown = items.Count < MaxItemsPerCategory ? items.Count : MaxItemsPerCategory;
            for (int i = 0; i < shown; i++) sb.AppendLine("    " + items[i]);
            if (items.Count > shown) sb.AppendLine($"    …另有 {items.Count - shown} 条");
        }
    }

    /// <summary>
    /// 打包预检：**不构建**，秒级扫一遍 Addressables 配置，抓那些「编辑器里跑得好好的、
    /// 打包后才炸（或只有真机上才暴露）」的常见问题。
    ///
    /// 为什么需要它：默认播放模式是 <c>Use Asset Database (fastest)</c>，
    /// 编辑器里读的是 AssetDatabase，**不经过 bundle 打包**。所以只靠"在编辑器里跑一遍"
    /// 无法证明资源打得进包。这个预检 + Analyze 规则 + 真构建，是三层递进的验证手段。
    ///
    /// 与 Unity 解耦的部分（<see cref="Evaluate"/>）纯逻辑、可单测；
    /// <see cref="Run"/> 只负责把真实 settings 映射成记录。
    /// </summary>
    public static class CAssetContentPreflight
    {
        /// <summary>纯逻辑评估（单测入口）。</summary>
        public static CAssetPreflightReport Evaluate(IEnumerable<CAssetEntryRecord> entries,
            IEnumerable<string> groupNames, IEnumerable<CAssetProfileValueRecord> profileValues)
        {
            var report = new CAssetPreflightReport();
            var seenAddresses = new Dictionary<string, string>(); // address → 第一次出现的 group

            var groupEntryCounts = new Dictionary<string, int>();
            if (groupNames != null)
            {
                foreach (string name in groupNames)
                {
                    report.GroupCount++;
                    groupEntryCounts[name] = 0;
                }
            }

            if (entries != null)
            {
                foreach (CAssetEntryRecord entry in entries)
                {
                    report.EntryCount++;

                    string group = entry.GroupName ?? string.Empty;
                    if (groupEntryCounts.ContainsKey(group)) groupEntryCounts[group]++;

                    if (string.IsNullOrEmpty(entry.Address))
                    {
                        report.EmptyAddresses.Add($"{group} / {entry.AssetPath}");
                    }
                    else if (seenAddresses.TryGetValue(entry.Address, out string firstGroup))
                    {
                        report.DuplicateAddresses.Add($"\"{entry.Address}\"（{firstGroup} 与 {group}）");
                    }
                    else
                    {
                        seenAddresses[entry.Address] = group;
                    }

                    if (!entry.AssetExists || string.IsNullOrEmpty(entry.AssetPath))
                    {
                        report.DeadEntries.Add($"{group} / {entry.Address} → {entry.AssetPath}");
                    }
                }
            }

            foreach (KeyValuePair<string, int> kv in groupEntryCounts)
            {
                if (kv.Value == 0) report.EmptyGroups.Add(kv.Key);
            }

            CheckProfileValues(profileValues, report);
            return report;
        }

        /// <summary>
        /// 检查 Profile 变量里引用的变量名是否都存在。
        ///
        /// **为什么不能用"解析后残留方括号"来判断**（实测过）：Addressables 里 <c>[...]</c> 是正常语法，
        /// 既能引用变量（<c>[BuildTarget]</c>）也能写内联 C# 表达式
        /// （<c>[UnityEditor.EditorUserBuildSettings.activeBuildTarget]</c>），而 <c>{...}</c> 是构建期路径替换；
        /// 更坑的是**未定义的变量会被静默吃掉** —— <c>[NotDefinedVariable]/x</c> 解析成 <c>NotDefinedVariable/x</c>，
        /// 方括号都没了，路径却是错的（只有真机构建/加载时才炸）。
        ///
        /// 所以判据是：抽出 <c>[...]</c> 里的内容，**是已定义变量 → 放行；含 '.' → 视为 C# 表达式放行；
        /// 其余裸标识符 → 疑似拼错的变量名**。
        /// </summary>
        private static void CheckProfileValues(IEnumerable<CAssetProfileValueRecord> profileValues,
            CAssetPreflightReport report)
        {
            if (profileValues == null) return;

            var records = new List<CAssetProfileValueRecord>(profileValues);
            var defined = new HashSet<string>();
            foreach (CAssetProfileValueRecord record in records)
            {
                if (!string.IsNullOrEmpty(record.Name)) defined.Add(record.Name);
            }

            foreach (CAssetProfileValueRecord record in records)
            {
                if (string.IsNullOrEmpty(record.RawValue)) continue;
                foreach (Match match in ProfileTokenRegex.Matches(record.RawValue))
                {
                    string token = match.Groups[1].Value.Trim();
                    if (token.Length == 0) continue;
                    if (defined.Contains(token)) continue;          // 正常引用其它变量
                    if (token.IndexOf('.') >= 0) continue;          // 内联 C# 表达式

                    report.UnresolvedProfileVariables.Add($"变量 {record.Name} = {record.RawValue} → 未定义的 [{token}]");
                }
            }
        }

        private static readonly Regex ProfileTokenRegex = new Regex(@"\[([^\[\]]+)\]", RegexOptions.Compiled);

        /// <summary>对当前工程的 Addressables 设置跑一遍预检。</summary>
        public static CAssetPreflightReport Run(AddressableAssetSettings settings)
        {
            if (settings == null) return new CAssetPreflightReport();

            var entries = new List<CAssetEntryRecord>();
            var groupNames = new List<string>();

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null) continue;
                groupNames.Add(group.Name);

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry == null) continue;
                    string assetPath = entry.AssetPath;
                    bool exists = !string.IsNullOrEmpty(assetPath)
                                  && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null;

                    entries.Add(new CAssetEntryRecord
                    {
                        Address = entry.address,
                        AssetPath = assetPath,
                        GroupName = group.Name,
                        AssetExists = exists,
                    });
                }
            }

            // Profile 变量：检查 [变量] 引用是否都存在（未定义的会被静默吃掉，路径就错了）
            return Evaluate(entries, groupNames, CollectProfileValues(settings));
        }

        /// <summary>
        /// 收集所有 Profile 变量的**原始值**。
        ///
        /// 只收变量就够：组的 bundle 路径（2.x 挂在 <see cref="BundledAssetGroupSchema"/> 上的
        /// <c>ProfileValueReference</c>）本身只是"引用哪个变量"，真正带 <c>[...]</c> / <c>{...}</c> 语法的
        /// 是变量值。所以查变量值即可覆盖 group 路径里的变量引用。
        /// </summary>
        private static List<CAssetProfileValueRecord> CollectProfileValues(AddressableAssetSettings settings)
        {
            var result = new List<CAssetProfileValueRecord>();
            AddressableAssetProfileSettings profile = settings.profileSettings;
            if (profile == null) return result;

            try
            {
                List<string> names = profile.GetVariableNames();
                for (int i = 0; i < names.Count; i++)
                {
                    result.Add(new CAssetProfileValueRecord
                    {
                        Name = names[i],
                        RawValue = profile.GetValueByName(settings.activeProfileId, names[i]),
                    });
                }
            }
            catch
            {
                // 拿不到变量列表不该让预检整体失败
            }
            return result;
        }
    }
}
