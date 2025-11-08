using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncTool;
using AsyncTool.Infrastructure;
using AsyncTool.Jobs;
using AsyncTool.Options;
using AsyncTool.Results;
using Xunit;

namespace AsyncTool.Tests
{
    public class AsyncSchedulerTests
    {
        [Fact]
        public async Task PriorityControlsExecutionOrderAndConcurrency()
        {
            var startedOrder = new List<string>();
            var concurrency = 0;
            var peakConcurrency = 0;
            var sync = new object();

            var options = new AsyncOptions
            {
                MaxDegreeOfParallelism = 2,
                OnJobStarted = job =>
                {
                    lock (sync)
                    {
                        concurrency++;
                        peakConcurrency = Math.Max(peakConcurrency, concurrency);
                        startedOrder.Add(job.WorkJobId!);
                    }
                },
                OnJobCompleted = job =>
                {
                    lock (sync)
                    {
                        concurrency--;
                    }
                }
            };

            var high = WorkJob.CreateBuilder()
                .WithId("high")
                .WithPriority(100)
                .WithWork(async () =>
                {
                    await Task.Delay(150);
                    return (object)"high";
                })
                .Build();

            var mid = WorkJob.CreateBuilder()
                .WithId("mid")
                .WithPriority(50)
                .WithWork(async () =>
                {
                    await Task.Delay(100);
                    return (object)"mid";
                })
                .Build();

            var low = WorkJob.CreateBuilder()
                .WithId("low")
                .WithPriority(10)
                .WithWork(async () =>
                {
                    await Task.Delay(80);
                    return (object)"low";
                })
                .Build();

            var lowest = WorkJob.CreateBuilder()
                .WithId("lowest")
                .WithPriority(5)
                .WithWork(async () =>
                {
                    await Task.Delay(50);
                    return (object)"lowest";
                })
                .Build();

            var asyncId = await Async.StartAsync(new[] { high, mid, low, lowest }, 2000, options);
            Async.Stop(asyncId);

            var priorityMap = new Dictionary<string, int>
            {
                ["high"] = 100,
                ["mid"] = 50,
                ["low"] = 10,
                ["lowest"] = 5
            };

            Assert.True(startedOrder.Zip(startedOrder.Skip(1), (prev, next) => priorityMap[prev] >= priorityMap[next]).All(x => x));
            Assert.True(peakConcurrency <= 2, "并发数量超过限制");
        }

        [Fact]
        public async Task RetryEventuallySucceeds()
        {
            var attempt = 0;
            var job = WorkJob.CreateBuilder()
                .WithId("retry")
                .WithRetry(2)
                .WithWork(async () =>
                {
                    attempt++;
                    if (attempt < 3)
                    {
                        throw new InvalidOperationException("fail");
                    }

                    await Task.Delay(10);
                    return (object)"success";
                })
                .Build();

            var asyncId = await Async.StartAsync(new[] { job }, 2000);
            var key = AsyncUtil.GenerateId(asyncId, "retry");
            var result = WorkJobResult.GetResult(key);

            Assert.Equal("success", result);
            Async.Stop(asyncId);
        }

        [Fact]
        public async Task TimeoutJobFailsAndRaisesEvent()
        {
            var failedJobs = new ConcurrentBag<string>();

            var options = new AsyncOptions
            {
                OnJobFailed = (job, _) => failedJobs.Add(job.WorkJobId!)
            };

            var timeoutJob = WorkJob.CreateBuilder()
                .WithId("timeout")
                .WithTimeout(100)
                .WithWork(async () =>
                {
                    await Task.Delay(500);
                    return (object)"never";
                })
                .Build();

            await Assert.ThrowsAsync<Exception>(() => Async.StartAsync(new[] { timeoutJob }, 1000, options));

            Assert.Contains("timeout", failedJobs);
        }

        [Fact]
        public async Task OptionalJobCanBeSkipped()
        {
            var executed = new ConcurrentBag<string>();

            var options = new AsyncOptions
            {
                OnJobCompleted = job => executed.Add(job.WorkJobId!)
            };

            var root = WorkJob.CreateBuilder()
                .WithId("root")
                .WithWork(async () =>
                {
                    await Task.Delay(10);
                    return (object)"root";
                })
                .Build();

            var optional = WorkJob.CreateBuilder()
                .WithId("optional")
                .WithWork(async () =>
                {
                    await Task.Delay(10);
                    return (object)"optional";
                })
                .Build();

            root.Next(optional, isMust: false);

            var asyncId = await Async.StartAsync(new[] { root }, 1000, options);
            Async.Stop(asyncId);

            Assert.Contains("root", executed);
            Assert.DoesNotContain("optional", executed);
        }
    }
}
