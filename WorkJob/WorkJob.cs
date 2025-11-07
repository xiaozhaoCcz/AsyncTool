using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsyncTool.Infrastructure;
using AsyncTool.Options;
using AsyncTool.Results;

namespace AsyncTool.Jobs
{
    /// <summary>
    /// 表示单个任务在调度过程中的生命周期状态。
    /// </summary>
    public enum WorkJobStatus
    {
        Start = 0,
        Running = 1,
        Finish = 2,
        Failed = 3
    }

    /// <summary>
    /// 描述单个可调度任务，支持配置优先级、重试、超时、参数与依赖关系。
    /// </summary>
    public class WorkJob
    {
        /// <summary>
        /// 构建 WorkJob 的建造者，提供流式 API 定义任务行为。
        /// </summary>
        public sealed class Builder
        {
            private readonly WorkJob _job = new();

            /// <summary>
            /// 指定任务唯一 Id。
            /// </summary>
            public Builder WithId(string workId)
            {
                _job.Id(workId);
                return this;
            }

            /// <summary>
            /// 指定无参的任务委托。
            /// </summary>
            public Builder WithWork(Func<Task<object>> func)
            {
                _job.Work(func);
                return this;
            }

            /// <summary>
            /// 指定带参数的任务委托。
            /// </summary>
            public Builder WithWork(Func<object, Task<object>> func)
            {
                _job.Work(func);
                return this;
            }

            /// <summary>
            /// 设置执行时需要的参数对象。
            /// </summary>
            public Builder WithParam(object param)
            {
                _job.Param(param);
                return this;
            }

            /// <summary>
            /// 设置单次执行的超时时间（毫秒）。
            /// </summary>
            public Builder WithTimeout(int milliseconds)
            {
                _job.Timeout(milliseconds);
                return this;
            }

            /// <summary>
            /// 设置失败后的最大重试次数。
            /// </summary>
            public Builder WithRetry(int count)
            {
                _job.Retry(count);
                return this;
            }

            /// <summary>
            /// 设置调度时的优先级（数值越大优先级越高）。
            /// </summary>
            public Builder WithPriority(int priority)
            {
                _job._priority = priority;
                return this;
            }

            /// <summary>
            /// 构建最终的 <see cref="WorkJob"/> 实例。
            /// </summary>
            public WorkJob Build()
            {
                if (string.IsNullOrWhiteSpace(_job._workJobId))
                {
                    throw new InvalidOperationException("WorkJob 必须指定 Id。");
                }

                if (_job._funcWithParam is null && _job._funcWithoutParam is null)
                {
                    throw new InvalidOperationException("WorkJob 必须配置至少一个执行委托。");
                }

                return _job;
            }
        }

        /// <summary>
        /// 创建一个新的建造者实例。
        /// </summary>
        public static Builder CreateBuilder()
        {
            return new Builder();
        }

        private readonly List<WorkJob> _dependsOnWorkJobs = [];
        private readonly List<WorkJob> _nextWorkJobs = [];
        private readonly object _lock = new();

        private Func<object, Task<object>>? _funcWithParam;
        private Func<Task<object>>? _funcWithoutParam;
        private object? _param;
        private object? _result;
        private int? _timeout;
        private int _retryCount;
        private int _priority;
        private volatile int _status = (int)WorkJobStatus.Start;
        private string? _workJobId;
        private string? _asId;

        private WorkJob()
        {
        }

        /// <summary>
        /// 当前任务的运行状态。
        /// </summary>
        public WorkJobStatus Status => (WorkJobStatus)_status;

        /// <summary>
        /// 子任务集合（执行完成后即将触发的任务）。
        /// </summary>
        public IReadOnlyList<WorkJob> NextWorkJobs => _nextWorkJobs;

        /// <summary>
        /// 依赖任务集合（该任务开始前必须完成的任务）。
        /// </summary>
        public IReadOnlyList<WorkJob> DependsOnWorkJobs => _dependsOnWorkJobs;

        /// <summary>
        /// 任务唯一 Id。
        /// </summary>
        public string? WorkJobId => _workJobId;

        /// <summary>
        /// 调度优先级，数值越大越优先执行。
        /// </summary>
        public int Priority => _priority;

        /// <summary>
        /// 设置任务 Id。
        /// </summary>
        public WorkJob Id(string workId)
        {
            _workJobId = workId;
            return this;
        }

        /// <summary>
        /// 配置带参数的执行委托。
        /// </summary>
        public WorkJob Work(Func<object, Task<object>> func)
        {
            _funcWithParam = func ?? throw new ArgumentNullException(nameof(func));
            _funcWithoutParam = null;
            return this;
        }

        /// <summary>
        /// 配置无参执行委托。
        /// </summary>
        public WorkJob Work(Func<Task<object>> func)
        {
            _funcWithoutParam = func ?? throw new ArgumentNullException(nameof(func));
            _funcWithParam = null;
            return this;
        }

        /// <summary>
        /// 传入执行参数。
        /// </summary>
        public WorkJob Param(object param)
        {
            _param = param;
            return this;
        }

        /// <summary>
        /// 配置超时时间（毫秒）。
        /// </summary>
        public WorkJob Timeout(int milliseconds)
        {
            _timeout = milliseconds;
            return this;
        }

        /// <summary>
        /// 配置最大重试次数。
        /// </summary>
        public WorkJob Retry(int count)
        {
            _retryCount = Math.Max(0, count);
            return this;
        }

        /// <summary>
        /// 直接设置优先级（供 Builder 外部调用）。
        /// </summary>
        public WorkJob PriorityLevel(int priority)
        {
            _priority = priority;
            return this;
        }

        /// <summary>
        /// 建立强依赖的子任务。
        /// </summary>
        public WorkJob Next(WorkJob job, bool isMust)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (isMust)
            {
                _nextWorkJobs.Add(job);
                job._dependsOnWorkJobs.Add(this);
            }

            return this;
        }

        /// <summary>
        /// 建立强依赖的单个子任务。
        /// </summary>
        public WorkJob Next(WorkJob job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            _nextWorkJobs.Add(job);
            job._dependsOnWorkJobs.Add(this);
            return this;
        }

        /// <summary>
        /// 为多个子任务建立强依赖。
        /// </summary>
        public WorkJob Next(params WorkJob[] jobs)
        {
            if (jobs == null)
            {
                throw new ArgumentNullException(nameof(jobs));
            }

            foreach (var job in jobs)
            {
                if (job == null)
                {
                    continue;
                }

                _nextWorkJobs.Add(job);
                job._dependsOnWorkJobs.Add(this);
            }

            return this;
        }

        /// <summary>
        /// 将任务状态标记为失败，用于停止流程。
        /// </summary>
        public void Stop()
        {
            ChangeStatus(WorkJobStatus.Failed);
        }

        /// <summary>
        /// 调度入口：在满足依赖的情况下执行任务。
        /// </summary>
        public Task DoWorkAsync(string asId, AsyncOptions? options)
        {
            _asId = asId;

            lock (_lock)
            {
                if (Status == WorkJobStatus.Running || Status == WorkJobStatus.Finish)
                {
                    return Task.CompletedTask;
                }

                if (Status == WorkJobStatus.Failed)
                {
                    PropagateFailure();
                    options?.OnJobFailed?.Invoke(this, new InvalidOperationException("任务已处于失败状态"));
                    return Task.CompletedTask;
                }

                foreach (var job in _dependsOnWorkJobs)
                {
                    if (job.Status == WorkJobStatus.Failed)
                    {
                        ChangeStatus(WorkJobStatus.Failed);
                        options?.OnJobFailed?.Invoke(this, new InvalidOperationException($"依赖任务 {job.WorkJobId} 失败"));
                        return Task.CompletedTask;
                    }

                    if (job.Status == WorkJobStatus.Running || job.Status == WorkJobStatus.Start)
                    {
                        return Task.CompletedTask;
                    }
                }
            }

            return DoJobAsync(options);
        }

        /// <summary>
        /// 真正的执行逻辑，包含超时、重试、结果写入与事件触发。
        /// </summary>
        private async Task DoJobAsync(AsyncOptions? options)
        {
            if (Status != WorkJobStatus.Start)
            {
                return;
            }

            options?.OnJobStarted?.Invoke(this);
            ChangeStatus(WorkJobStatus.Running);

            var currentRetry = 0;
            Exception? lastException = null;

            while (currentRetry <= _retryCount)
            {
                try
                {
                    _result = null;

                    if (_funcWithParam != null)
                    {
                        var param = _param ?? new object();

                        if (!_timeout.HasValue || _timeout < 0)
                        {
                            _result = await _funcWithParam(param).ConfigureAwait(false);
                        }
                        else
                        {
                            _result = await ExecuteJobTimeoutAsync(_funcWithParam, _timeout.Value, param).ConfigureAwait(false);
                        }
                    }
                    else if (_funcWithoutParam != null)
                    {
                        if (!_timeout.HasValue || _timeout < 0)
                        {
                            _result = await _funcWithoutParam().ConfigureAwait(false);
                        }
                        else
                        {
                            _result = await ExecuteJobTimeoutAsync(_funcWithoutParam, _timeout.Value).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("未配置可执行的任务委托。");
                    }

                    ChangeStatus(WorkJobStatus.Finish);

                    SaveResult(_result);
                    options?.OnJobCompleted?.Invoke(this);
                    return;
                }
                catch (OperationCanceledException ex)
                {
                    lastException = ex;
                }
                catch (TimeoutException ex)
                {
                    lastException = ex;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                if (currentRetry < _retryCount)
                {
                    currentRetry++;
                    continue;
                }

                break;
            }

            ChangeStatus(WorkJobStatus.Failed);
            PropagateFailure();

            var failure = lastException ?? new Exception("任务执行失败");
            SaveResult(failure);
            options?.OnJobFailed?.Invoke(this, failure);
        }

        /// <summary>
        /// 将失败状态向下游任务传播。
        /// </summary>
        private void PropagateFailure()
        {
            foreach (var job in _nextWorkJobs)
            {
                Interlocked.Exchange(ref job._status, (int)WorkJobStatus.Failed);
            }
        }

        /// <summary>
        /// 将任务执行结果（或异常）写入全局结果存储。
        /// </summary>
        private void SaveResult(object? value)
        {
            if (!string.IsNullOrEmpty(_asId) && !string.IsNullOrEmpty(_workJobId) && value != null)
            {
                WorkJobResult.AddResult(AsyncUtil.GenerateId(_asId!, _workJobId!), value);
            }
        }

        /// <summary>
        /// 执行带参数委托并应用超时限制。
        /// </summary>
        private static async Task<object> ExecuteJobTimeoutAsync(Func<object, Task<object>> func, int timeout, object param)
        {
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                return await func(param).WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Job execution timed out", ex);
            }
        }

        /// <summary>
        /// 执行无参委托并应用超时限制。
        /// </summary>
        private static async Task<object> ExecuteJobTimeoutAsync(Func<Task<object>> func, int timeout)
        {
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                return await func().WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Job execution timed out", ex);
            }
        }

        /// <summary>
        /// 更新内部状态字段。
        /// </summary>
        private void ChangeStatus(WorkJobStatus status)
        {
            Interlocked.Exchange(ref _status, (int)status);
        }
    }
}

