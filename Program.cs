using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AsyncTool;
using AsyncTool.Infrastructure;
using AsyncTool.Jobs;
using AsyncTool.Options;
using AsyncTool.Results;

// 演示：构建一个包含 10 个节点的复杂任务流，涵盖优先级、并行度、重试、超时与结果收集。
// 每个任务均通过 WorkJob Builder 定义，最终由 Async.Start 统一调度执行。
var loadConfig = WorkJob.CreateBuilder()
    .WithId("load-config")
    .WithPriority(100)
    .WithWork(async () =>
    {
        Console.WriteLine("[load-config] 开始加载配置...");
        for (var step = 1; step <= 3; step++)
        {
            await Task.Delay(80);
            Console.WriteLine($"[load-config] 正在处理配置段 {step}/3");
        }

        Console.WriteLine("[load-config] 配置加载完成");
        return (object)"config:v1";
    })
    .Build();

var loadUsers = WorkJob.CreateBuilder()
    .WithId("load-users")
    .WithPriority(90)
    .WithWork(async () =>
    {
        Console.WriteLine("[load-users] 开始拉取用户数据...");
        for (var page = 1; page <= 4; page++)
        {
            await Task.Delay(70);
            Console.WriteLine($"[load-users] 第 {page}/4 页数据同步完成");
        }

        Console.WriteLine("[load-users] 用户数据拉取完成");
        return (object)"users:128";
    })
    .Build();

var loadOrders = WorkJob.CreateBuilder()
    .WithId("load-orders")
    .WithPriority(80)
    .WithWork(async () =>
    {
        Console.WriteLine("[load-orders] 开始拉取订单数据...");
        for (var batch = 1; batch <= 5; batch++)
        {
            await Task.Delay(60);
            Console.WriteLine($"[load-orders] 订单批次 {batch}/5 已处理");
        }

        Console.WriteLine("[load-orders] 订单数据拉取完成");
        return (object)"orders:256";
    })
    .Build();

var mergeData = WorkJob.CreateBuilder()
    .WithId("merge-data")
    .WithPriority(70)
    .WithWork(async () =>
    {
        Console.WriteLine("[merge-data] 开始数据合并...");
        for (var stage = 1; stage <= 3; stage++)
        {
            await Task.Delay(90);
            Console.WriteLine($"[merge-data] 合并阶段 {stage}/3 完成");
        }

        Console.WriteLine("[merge-data] 数据合并完成");
        return (object)"merged:ok";
    })
    .Build();

var trainingAttempts = 0;
var trainModel = WorkJob.CreateBuilder()
    .WithId("train-model")
    .WithPriority(60)
    .WithRetry(2)
    .WithWork(async () =>
    {
        trainingAttempts++;
        Console.WriteLine($"[train-model] 第 {trainingAttempts} 次模型训练开始");

        for (var epoch = 1; epoch <= 4; epoch++)
        {
            await Task.Delay(100);
            Console.WriteLine($"[train-model] Epoch {epoch}/4 完成");
        }

        if (trainingAttempts < 2)
        {
            Console.WriteLine("[train-model] 本次训练失败，准备重试");
            throw new InvalidOperationException("训练失败");
        }

        Console.WriteLine("[train-model] 模型训练成功");
        return (object)"model:v2";
    })
    .Build();

var generateReport = WorkJob.CreateBuilder()
    .WithId("generate-report")
    .WithPriority(50)
    .WithTimeout(2000)
    .WithWork(async () =>
    {
        Console.WriteLine("[generate-report] 开始生成报告...");
        for (var section = 1; section <= 5; section++)
        {
            await Task.Delay(90);
            Console.WriteLine($"[generate-report] 报告章节 {section}/5 已生成");
        }

        Console.WriteLine("[generate-report] 报告生成完成");
        return (object)"report:ready";
    })
    .Build();

var notifyTeam = WorkJob.CreateBuilder()
    .WithId("notify-team")
    .WithPriority(40)
    .WithWork(async () =>
    {
        Console.WriteLine("[notify-team] 开始通知团队...");
        string[] targets = { "产品", "运营", "客服" };
        for (var i = 0; i < targets.Length; i++)
        {
            await Task.Delay(60);
            Console.WriteLine($"[notify-team] 已通知 {targets[i]}");
        }

        Console.WriteLine("[notify-team] 全部团队通知完成");
        return (object)"notify:sent";
    })
    .Build();

var archiveRaw = WorkJob.CreateBuilder()
    .WithId("archive-raw")
    .WithPriority(55)
    .WithParam("oss://raw-bucket")
    .WithWork(async destination =>
    {
        Console.WriteLine("[archive-raw] 开始归档原始数据...");
        for (var part = 1; part <= 4; part++)
        {
            await Task.Delay(85);
            Console.WriteLine($"[archive-raw] 正在上传分片 {part}/4 至 {destination}");
        }

        Console.WriteLine($"[archive-raw] 原始数据归档至 {destination}");
        return (object)$"archive:{destination}";
    })
    .Build();

var cleanupTemp = WorkJob.CreateBuilder()
    .WithId("cleanup-temp")
    .WithPriority(45)
    .WithWork(async () =>
    {
        Console.WriteLine("[cleanup-temp] 开始清理临时文件...");
        for (var dir = 1; dir <= 3; dir++)
        {
            await Task.Delay(70);
            Console.WriteLine($"[cleanup-temp] 临时目录 {dir}/3 清理完成");
        }

        Console.WriteLine("[cleanup-temp] 临时文件清理完成");
        return (object)"cleanup:done";
    })
    .Build();

var auditLog = WorkJob.CreateBuilder()
    .WithId("audit-log")
    .WithPriority(30)
    .WithWork(async () =>
    {
        Console.WriteLine("[audit-log] 开始写入审计日志...");
        for (var entry = 1; entry <= 5; entry++)
        {
            await Task.Delay(50);
            Console.WriteLine($"[audit-log] 第 {entry}/5 条日志写入完成");
        }

        Console.WriteLine("[audit-log] 审计日志写入完成");
        return (object)"audit:ok";
    })
    .Build();

// 辅助工具：统一控制并发量，监听任务事件，便于观察运行过程。
var options = new AsyncOptions
{
    MaxDegreeOfParallelism = 3,
    OnJobStarted = job => Console.WriteLine($"[event] {job.WorkJobId} started"),
    OnJobCompleted = job => Console.WriteLine($"[event] {job.WorkJobId} completed"),
    OnJobFailed = (job, ex) => Console.WriteLine($"[event] {job.WorkJobId} failed: {ex.Message}")
};

// 构建依赖关系（共 10 个任务）
WorkJobLinker.Link(
    (loadConfig, loadUsers, true),
    (loadConfig, loadOrders, true),
    (loadUsers, mergeData, true),
    (loadOrders, mergeData, true),
    (loadUsers, archiveRaw, true),
    (mergeData, trainModel, true),
    (trainModel, generateReport, true),
    (trainModel, cleanupTemp, true),
    (generateReport, notifyTeam, true),
    (generateReport, auditLog, true),
    (cleanupTemp, auditLog, true)
);

// 汇总所有任务，方便结果打印或后续扩展。
var allJobs = new List<WorkJob>
{
    loadConfig,
    loadUsers,
    loadOrders,
    mergeData,
    trainModel,
    generateReport,
    notifyTeam,
    archiveRaw,
    cleanupTemp,
    auditLog
};

try
{
    var asyncId = Async.Start(new[] { loadConfig }, timeoutMilliseconds: 8000, options);

    Console.WriteLine($"任务组启动完成，Id: {asyncId}");
    Console.WriteLine("--- 结果汇总 ---");

    foreach (var job in allJobs)
    {
        var key = AsyncUtil.GenerateId(asyncId, job.WorkJobId!);
        var result = WorkJobResult.GetResult(key);
        Console.WriteLine($"{job.WorkJobId}: {result}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"任务组执行失败: {ex}");
}
finally
{
    Console.WriteLine("执行结束。");

    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}
