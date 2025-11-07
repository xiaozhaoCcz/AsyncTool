using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncTool.Infrastructure;
using AsyncTool.Jobs;
using AsyncTool.Results;

namespace AsyncTool
{
    public static class Async
    {
        public static async Task<string> StartAsync(IEnumerable<WorkJob> workJobs, long timeoutMilliseconds)
        {
            if (workJobs == null)
            {
                throw new ArgumentNullException(nameof(workJobs));
            }

            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "超时时间必须大于 0。");
            }

            var workJobList = workJobs.ToList();
            if (workJobList.Count == 0)
            {
                throw new ArgumentException("至少需要一个工作任务。", nameof(workJobs));
            }

            var asId = AsyncUtil.Generate12Digit();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds));

            AsyncUtil.AddToken(asId, cts);
            AsyncUtil.AddWorkJobs(asId, workJobList);

            try
            {
                await BeginAsync(asId, workJobList, cts.Token, timeoutMilliseconds).ConfigureAwait(false);
                return asId;
            }
            catch
            {
                Stop(asId);
                throw;
            }
        }

        public static string Start(IEnumerable<WorkJob> workJobs, long timeoutMilliseconds)
        {
            return StartAsync(workJobs, timeoutMilliseconds).GetAwaiter().GetResult();
        }

        public static async Task BeginAsync(string asId, IEnumerable<WorkJob> workJobs, CancellationToken token, long timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
            {
                Stop(asId);
                throw new TimeoutException("异步任务超时。");
            }

            var tasks = new List<Task>();

            foreach (var workJob in workJobs)
            {
                token.ThrowIfCancellationRequested();
                tasks.Add(ExecuteJobAndChildrenAsync(asId, workJob, token, timeoutMilliseconds));
            }

            if (tasks.Count == 0)
            {
                return;
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public static async Task ExecuteJobAndChildrenAsync(string asId, WorkJob workJob, CancellationToken token, long timeoutMilliseconds)
        {
            token.ThrowIfCancellationRequested();

            var startTime = DateTimeOffset.UtcNow;
            await workJob.DoWorkAsync(asId).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var elapsed = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
            var remainingTimeout = timeoutMilliseconds - elapsed;

            if (remainingTimeout <= 0)
            {
                Stop(asId);
                throw new TimeoutException("异步任务执行超时。");
            }

            if (workJob.Status == WorkJobStatus.Failed)
            {
                Stop(asId);
                throw new Exception("工作任务执行失败。");
            }

            if (workJob.Status == WorkJobStatus.Finish && workJob.NextWorkJobs.Count > 0)
            {
                await BeginAsync(asId, workJob.NextWorkJobs, token, remainingTimeout).ConfigureAwait(false);
            }
        }

        public static void Stop(string asId)
        {
            try
            {
                var cts = AsyncUtil.GetToken(asId);
                cts?.Cancel();

                var workJobs = AsyncUtil.GetWorkJobs(asId);
                if (workJobs != null)
                {
                    foreach (var workJob in workJobs)
                    {
                        workJob.Stop();

                        if (!string.IsNullOrEmpty(workJob.WorkJobId))
                        {
                            var resultId = AsyncUtil.GenerateId(asId, workJob.WorkJobId!);
                            WorkJobResult.RemoveResult(resultId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                AsyncUtil.RemoveToken(asId);
                AsyncUtil.RemoveWorkJobs(asId);
            }
        }
    }
}