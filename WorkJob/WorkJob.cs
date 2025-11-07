using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsyncTool.Infrastructure;
using AsyncTool.Results;

namespace AsyncTool.Jobs
{
    public enum WorkJobStatus
    {
        Start = 0,
        Running = 1,
        Finish = 2,
        Failed = 3
    }

    public class WorkJob
    {
        public sealed class Builder
        {
            private readonly WorkJob _job = new();

            public Builder WithId(string workId)
            {
                _job.Id(workId);
                return this;
            }

            public Builder WithWork(Func<Task<object>> func)
            {
                _job.Work(func);
                return this;
            }

            public Builder WithWork(Func<object, Task<object>> func)
            {
                _job.Work(func);
                return this;
            }

            public Builder WithParam(object param)
            {
                _job.Param(param);
                return this;
            }

            public Builder WithTimeout(int milliseconds)
            {
                _job.Timeout(milliseconds);
                return this;
            }

            public Builder WithRetry(int count)
            {
                _job.Retry(count);
                return this;
            }

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
        private volatile int _status = (int)WorkJobStatus.Start;
        private string? _workJobId;
        private string? _asId;

        private WorkJob()
        {
        }

        public WorkJobStatus Status => (WorkJobStatus)_status;

        public IReadOnlyList<WorkJob> NextWorkJobs => _nextWorkJobs;

        public IReadOnlyList<WorkJob> DependsOnWorkJobs => _dependsOnWorkJobs;

        public string? WorkJobId => _workJobId;

        public WorkJob Id(string workId)
        {
            _workJobId = workId;
            return this;
        }

        public WorkJob Work(Func<object, Task<object>> func)
        {
            _funcWithParam = func ?? throw new ArgumentNullException(nameof(func));
            _funcWithoutParam = null;
            return this;
        }

        public WorkJob Work(Func<Task<object>> func)
        {
            _funcWithoutParam = func ?? throw new ArgumentNullException(nameof(func));
            _funcWithParam = null;
            return this;
        }

        public WorkJob Param(object param)
        {
            _param = param;
            return this;
        }

        public WorkJob Timeout(int milliseconds)
        {
            _timeout = milliseconds;
            return this;
        }

        public WorkJob Retry(int count)
        {
            _retryCount = Math.Max(0, count);
            return this;
        }

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

        public void Stop()
        {
            ChangeStatus(WorkJobStatus.Failed);
        }

        public Task DoWorkAsync(string asId)
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
                    return Task.CompletedTask;
                }

                foreach (var job in _dependsOnWorkJobs)
                {
                    if (job.Status == WorkJobStatus.Failed)
                    {
                        ChangeStatus(WorkJobStatus.Failed);
                        return Task.CompletedTask;
                    }

                    if (job.Status == WorkJobStatus.Running || job.Status == WorkJobStatus.Start)
                    {
                        return Task.CompletedTask;
                    }
                }
            }

            return DoJobAsync();
        }

        private async Task DoJobAsync()
        {
            if (Status != WorkJobStatus.Start)
            {
                return;
            }

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
            SaveResult(lastException ?? new Exception("任务执行失败"));
        }

        private void PropagateFailure()
        {
            foreach (var job in _nextWorkJobs)
            {
                Interlocked.Exchange(ref job._status, (int)WorkJobStatus.Failed);
            }
        }

        private void SaveResult(object? value)
        {
            if (!string.IsNullOrEmpty(_asId) && !string.IsNullOrEmpty(_workJobId) && value != null)
            {
                WorkJobResult.AddResult(AsyncUtil.GenerateId(_asId!, _workJobId!), value);
            }
        }

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

        private void ChangeStatus(WorkJobStatus status)
        {
            Interlocked.Exchange(ref _status, (int)status);
        }
    }
}

