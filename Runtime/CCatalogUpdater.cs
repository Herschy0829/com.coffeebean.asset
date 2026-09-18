using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CoffeeBean
{
    /// <summary>
    /// 资源更新下载服务（对齐 Idle CheckUpdateAndDownload 的能力，做成可复用服务）：
    /// 检测 catalog 更新 → 更新 catalog → 计算下载大小 → 下载（进度回调）→ 成功/失败（可重试）。
    ///
    /// 全程 UniTask：句柄用 <c>ToUniTask()</c>（池化等待源 + 主线程恢复），
    /// 延时/让帧用 <c>UniTask.Delay</c> / <c>UniTask.Yield</c>。
    /// </summary>
    public sealed class CCatalogUpdater
    {
        private const string Tag = "CoffeeBean.Asset";

        /// <summary>内置失败重试次数（默认 3）。</summary>
        public int MaxRetry = 3;

        /// <summary>重试间隔（秒）。</summary>
        public float RetryIntervalSeconds = 1f;

        /// <summary>是否强制更新（autoReleaseHandle）。</summary>
        public bool AutoReleaseHandle = true;

        /// <summary>
        /// 执行更新检查与下载。
        /// </summary>
        /// <param name="progress">下载进度回调（0~1）。</param>
        /// <param name="token">取消令牌。</param>
        /// <returns>是否有更新且下载完成；false = 无更新 / 失败 / 已取消。</returns>
        public async UniTask<bool> UpdateAsync(IProgress<float> progress = null, CancellationToken token = default)
        {
            // Addressables 的 API 是主线程 API：入口先收口一次
            await UniTask.SwitchToMainThread(token);

            for (int attempt = 0; attempt <= MaxRetry; attempt++)
            {
                if (token.IsCancellationRequested) return false;
                bool ok = await TryUpdateOnce(progress, token);
                if (ok) return true;
                if (attempt < MaxRetry)
                {
                    CLog.Warn(Tag, $"更新失败，{RetryIntervalSeconds}s 后重试（{attempt + 1}/{MaxRetry}）");
                    try
                    {
                        await UniTask.Delay((int)(RetryIntervalSeconds * 1000), cancellationToken: token);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        private async UniTask<bool> TryUpdateOnce(IProgress<float> progress, CancellationToken token)
        {
            try
            {
                // 1. 初始化
                var initHandle = Addressables.InitializeAsync();
                await initHandle.ToUniTask();
                if (initHandle.Status != AsyncOperationStatus.Succeeded) return false;

                // 2. 检测更新
                var checkHandle = Addressables.CheckForCatalogUpdates(false);
                await checkHandle.ToUniTask();
                if (checkHandle.Status != AsyncOperationStatus.Succeeded) return false;

                if (checkHandle.Result == null || checkHandle.Result.Count == 0)
                {
                    CLog.Info(Tag, "没有检测到资源更新");
                    return true; // 无更新 = 成功
                }

                // 3. 更新 catalog
                var updateHandle = Addressables.UpdateCatalogs(checkHandle.Result, AutoReleaseHandle);
                await updateHandle.ToUniTask();
                if (updateHandle.Status != AsyncOperationStatus.Succeeded) return false;

                // 4. 计算总下载大小
                long totalSize = 0;
                var locators = updateHandle.Result;
                if (locators != null)
                {
                    foreach (var locator in locators)
                    {
                        if (token.IsCancellationRequested) return false;
                        totalSize += await GetDownloadSize(locator, token);
                    }
                }

                if (totalSize <= 0)
                {
                    CLog.Info(Tag, "catalog 已更新，无待下载内容");
                    return true;
                }

                // 5. 下载全部
                CLog.Info(Tag, $"开始下载资源，总大小 {FormatSize(totalSize)}");
                bool downloaded = await DownloadAll(locators, progress, token);
                return downloaded;
            }
            catch (Exception e)
            {
                CLog.Error(Tag, $"更新检查异常: {e.Message}");
                return false;
            }
        }

        private static async UniTask<long> GetDownloadSize(IResourceLocator locator, CancellationToken token)
        {
            var keys = new List<object>();
            keys.AddRange(locator.Keys);
            var sizeHandle = Addressables.GetDownloadSizeAsync(keys.GetEnumerator());
            await sizeHandle.ToUniTask();
            if (sizeHandle.Status != AsyncOperationStatus.Succeeded) return 0;
            return sizeHandle.Result;
        }

        private static async UniTask<bool> DownloadAll(List<IResourceLocator> locators, IProgress<float> progress, CancellationToken token)
        {
            long totalDownloaded = 0;
            long totalSize = 0;

            // 先算总大小（进度分母）
            foreach (var locator in locators)
            {
                totalSize += await GetDownloadSize(locator, token);
            }
            if (totalSize <= 0) totalSize = 1;

            foreach (var locator in locators)
            {
                if (token.IsCancellationRequested) return false;

                var keys = new List<object>();
                keys.AddRange(locator.Keys);

                var downloadHandle = Addressables.DownloadDependenciesAsync(keys, true);
                while (!downloadHandle.IsDone)
                {
                    if (token.IsCancellationRequested)
                    {
                        Addressables.Release(downloadHandle);
                        return false;
                    }
                    if (downloadHandle.Status == AsyncOperationStatus.Failed)
                    {
                        Addressables.Release(downloadHandle);
                        return false;
                    }
                    progress?.Report(downloadHandle.PercentComplete);
                    // UniTask.Yield 默认 Update 时机 → 天然回主线程，不需要配 Anything
                    await UniTask.Yield(token);
                }

                if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    Addressables.Release(downloadHandle);
                    return false;
                }

                totalDownloaded += 1; // 每个 locator 完成后累计（粗粒度）
                progress?.Report(Mathf.Clamp01(totalDownloaded / (float)locators.Count));
                Addressables.Release(downloadHandle);
            }

            progress?.Report(1f);
            CLog.Info(Tag, "资源下载完成");
            return true;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
