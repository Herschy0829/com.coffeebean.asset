using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace CoffeeBean.Asset.Tests
{
    /// <summary>
    /// 锁定"资源管理用 UniTask"这个决定：公开异步面**不能**退回 C# Task。
    ///
    /// 为什么值得锁：Task 那条路每个句柄都会 new 一个
    /// <c>TaskCompletionSource&lt;T&gt;(RunContinuationsAsynchronously)</c>（实测 ~104 B）并经调度器排队；
    /// 更麻烦的是续体**不保证回到 Unity 主线程** —— 旧实现里 <c>ConfigureAwait(false)</c> 之后紧接着
    /// <c>Object.Instantiate</c>，就是一次真实的"主线程违规"隐患。
    /// 谁哪天把返回类型改回 Task（哪怕只是"顺手统一一下"），这条会立刻红。
    /// </summary>
    public class CAssetAsyncSurfaceTests
    {
        [Test]
        public void CAssetSystem_AsyncApi_ReturnsUniTask()
        {
            AssertUniTaskOf(typeof(CAssetSystem).GetMethod("LoadAssetAsync"));
            AssertUniTaskOf(typeof(CAssetSystem).GetMethod("LoadAssetsByLabelAsync"));
            AssertUniTaskOf(typeof(CAssetSystem).GetMethod("InstantiateAsync"));
            AssertUniTaskOf(typeof(CAssetSystem).GetMethod("LoadSceneAsync"));

            Assert.AreEqual(typeof(UniTask), typeof(CAssetSystem).GetMethod("PreloadAsync").ReturnType,
                "PreloadAsync 应返回 UniTask（不是 Task）");
        }

        [Test]
        public void IAssetBackend_AsyncApi_ReturnsUniTask()
        {
            AssertUniTaskOf(typeof(IAssetBackend).GetMethod("LoadAssetAsync"));
            Assert.AreEqual(typeof(UniTask<bool>), typeof(IAssetBackend).GetMethod("HasAddressAsync").ReturnType,
                "后端契约也必须是 UniTask —— 否则 CAssetSystem 又要桥接 Task");
        }

        [Test]
        public void CAssetExtensions_AsyncApi_ReturnsUniTask()
        {
            int checkedCount = 0;
            foreach (MethodInfo method in typeof(CAssetExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.Name.EndsWith("Async")) continue;
                checkedCount++;

                string name = method.ReturnType.Name;
                Assert.IsTrue(name == "UniTask" || name == "UniTask`1",
                    $"{method.Name} 应返回 UniTask/UniTask<T>，实际 {method.ReturnType}");
            }
            Assert.Greater(checkedCount, 0, "应至少有一个 *Async 扩展方法");
        }

        [Test]
        public void CCatalogUpdater_UpdateAsync_ReturnsUniTaskOfBool()
        {
            Assert.AreEqual(typeof(UniTask<bool>), typeof(CCatalogUpdater).GetMethod("UpdateAsync").ReturnType);
        }

        private static void AssertUniTaskOf(MethodInfo method)
        {
            Assert.IsNotNull(method);
            Assert.IsTrue(method.ReturnType.IsGenericType, $"{method.Name} 应返回 UniTask<T>");
            Assert.AreEqual(typeof(UniTask<>), method.ReturnType.GetGenericTypeDefinition(),
                $"{method.Name} 应返回 UniTask<T>（不是 Task<T>）");
        }
    }
}
