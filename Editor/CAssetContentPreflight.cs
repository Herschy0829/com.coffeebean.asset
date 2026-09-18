using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

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

    /// <summary>打包预检结果。</summary>
    public sealed class CAssetPreflightReport
    {
        /// <summary>同一个 address 出现多次（catalog 里后者覆盖前者，只有运行期才 LogError）。</summary>
        public List<string> DuplicateAddresses = new List<string>();

        /// <summary>address 为空的条目。</summary>
        public List<string> EmptyAddresses = new List<string>();

        /// <summary>资源已不存在（条目是死的）—— 构建会报错的那类。</summary>
        public List<string> DeadEntries = new List<string>();

        /// <summary>Profile 变量没解析出来（路径里还留着 <c>[Var]</c>）—— 构建失败的经典原因。</summary>
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
            $"未解析 Profile 变量 {UnresolvedProfileVariables.Count}、空组 {EmptyGroups.Count}";

        /// <summary>多行明细（无问题时给一句"没发现问题"）。</summary>
        public string Details()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Summary);
            AppendList(sb, "重复地址", DuplicateAddresses);
            AppendList(sb, "空地址", EmptyAddresses);
            AppendList(sb, "失效条目（资源已不存在）", DeadEntries);
            AppendList(sb, "未解析的 Profile 变量", UnresolvedProfileVariables);
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
            IEnumerable<string> groupNames, IEnumerable<string> resolvedProfileStrings)
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

            if (resolvedProfileStrings != null)
            {
                foreach (string text in resolvedProfileStrings)
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    if (text.IndexOf('[') >= 0 || text.IndexOf(']') >= 0)
                    {
                        report.UnresolvedProfileVariables.Add(text);
                    }
                }
            }

            return report;
        }

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

            // Profile 变量：变量值 / bundle 路径解析后不该再残留 [变量]
            return Evaluate(entries, groupNames, CollectProfileStrings(settings));
        }

        /// <summary>
        /// 收集"会被写进 catalog 的路径字符串"，用于发现未解析的 Profile 变量。
        ///
        /// 2.x 里 group 本身没有 BuildPath/LoadPath（1.x 有），路径挂在
        /// <see cref="BundledAssetGroupSchema"/> 上，类型是 <see cref="ProfileValueReference"/>。
        /// 变量之间可以相互引用，所以变量**值**本身也要查一遍：嵌了没定义的变量同样会残留 []。
        /// </summary>
        private static List<string> CollectProfileStrings(AddressableAssetSettings settings)
        {
            var result = new List<string>();
            AddressableAssetProfileSettings profile = settings.profileSettings;
            if (profile == null) return result;

            try
            {
                List<string> names = profile.GetVariableNames();
                for (int i = 0; i < names.Count; i++)
                {
                    string raw = profile.GetValueByName(settings.activeProfileId, names[i]);
                    if (!string.IsNullOrEmpty(raw)) result.Add($"变量 {names[i]} = {raw}");
                }
            }
            catch
            {
                // 拿不到变量列表不该让预检整体失败
            }

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null) continue;
                foreach (AddressableAssetGroupSchema schema in group.Schemas)
                {
                    if (!(schema is BundledAssetGroupSchema bundled)) continue;
                    result.Add($"{group.Name}.BuildPath = {bundled.BuildPath.GetValue(settings)}");
                    result.Add($"{group.Name}.LoadPath = {bundled.LoadPath.GetValue(settings)}");
                }
            }
            return result;
        }
    }
}
