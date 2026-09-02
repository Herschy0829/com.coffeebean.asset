using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 资源依赖分析工具（入口：Window &gt; CoffeeBean 收敛到 CoffeeBean Hub）：
    /// 选择资源后显示：正向依赖（它依赖谁）+ 反向引用（谁引用了它，含场景/预制体/材质/贴图等）。
    /// </summary>
    [CoffeeBeanTool("资源依赖分析", "查看资源依赖树与被引用关系（正向依赖 / 反向引用）", "Asset")]
    public sealed class CAssetDependencyWindow : EditorWindow
    {
        private Object _target;
        private Vector2 _scroll;
        private string _search = "";

        // 入口统一收敛到 CoffeeBean Hub（Window > CoffeeBean），不注册独立菜单项（避免子菜单抢占主入口）
        public static void Open() => GetWindow<CAssetDependencyWindow>("资源依赖分析");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("资源依赖分析", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 目标资源
            _target = EditorGUILayout.ObjectField("目标资源", _target, typeof(Object), false);
            if (_target == null)
            {
                EditorGUILayout.HelpBox("选择 Assets 下的一个资源（Prefab/Material/Texture/ScriptableObject 等）。", MessageType.Info);
                return;
            }

            string path = AssetDatabase.GetAssetPath(_target);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
            {
                EditorGUILayout.HelpBox("请选择 Assets 目录下的资源（非 Package）。", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"目标: {path}", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            _search = EditorGUILayout.TextField("过滤:", _search);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // 反向引用：谁引用了它
            var referencers = FindReferencers(path);
            EditorGUILayout.LabelField($"被引用（{referencers.Count}）", EditorStyles.boldLabel);
            if (referencers.Count == 0)
                EditorGUILayout.LabelField("（无资源引用它——可能仅在场景实例或运行时加载）", EditorStyles.miniLabel);
            foreach (var refPath in referencers)
            {
                if (!string.IsNullOrEmpty(_search) && !refPath.ToLowerInvariant().Contains(_search.ToLowerInvariant())) continue;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("定位", GUILayout.Width(48))) SelectAsset(refPath);
                EditorGUILayout.LabelField(refPath);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);

            // 正向依赖：它依赖谁（自身外）
            var dependencies = AssetDatabase.GetDependencies(path, false)
                .Where(d => d != path && d.StartsWith("Assets/"))
                .OrderBy(d => d)
                .ToList();
            EditorGUILayout.LabelField($"正向依赖（{dependencies.Count}）", EditorStyles.boldLabel);
            foreach (var dep in dependencies)
            {
                if (!string.IsNullOrEmpty(_search) && !dep.ToLowerInvariant().Contains(_search.ToLowerInvariant())) continue;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("定位", GUILayout.Width(48))) SelectAsset(dep);
                EditorGUILayout.LabelField(dep);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>查找引用指定资源的全部资源（反向依赖）。</summary>
        private static List<string> FindReferencers(string targetPath)
        {
            var result = new List<string>();
            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);

            // 扫描 Assets 下全部资源（排除目录），用 GetDependencies 检查是否含目标
            string[] allAssets = AssetDatabase.FindAssets("", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p) && p != targetPath)
                .ToArray();

            foreach (var assetPath in allAssets)
            {
                string[] deps = AssetDatabase.GetDependencies(assetPath, false);
                if (deps.Any(d => AssetDatabase.AssetPathToGUID(d) == targetGuid))
                {
                    result.Add(assetPath);
                }
            }
            return result;
        }

        private static void SelectAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }
    }
}
