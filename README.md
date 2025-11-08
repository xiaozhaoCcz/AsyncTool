# ⚙️ AsyncTool 项目文档

> 🧰 异步任务调度工具，支持任务依赖、超时、重试、结果收集等功能，适合在服务端或桌面应用中快速构建复杂的任务流水线。

```
┌────────────┐      ┌────────────┐      ┌────────────┐
│ WorkJob 建造器 ──▶ │ Async 调度器 │ ──▶ │ WorkJobResult │
└────────────┘      └────────────┘      └────────────┘
        │                 │                     │
        │                 │                     └── 各任务结果集中存储
        │                 └── 控制任务启动/停止/超时
        └── 构建任务节点及依赖
```

> 🔗 运行示意：`@README.md (5-13)` 展示了任务建造器、调度器与结果存储之间的关系，可作为下面运行示例的流程参照。

---

## 🗂️ 项目结构

| 模块 | 路径 | 说明 |
| --- | --- | --- |
| 核心调度 | `Async/Async.cs` | `Async.Start/StartAsync` 负责启动、超时控制、停止清理 |
| 任务定义 | `WorkJob/WorkJob.cs` | `WorkJob` 使用建造者模式描述任务、依赖、重试与超时 |
| 工具类 | `AsyncUtil/AsyncUtil.cs` | 管理任务组令牌、ID 生成与任务集合缓存 |
| 结果缓存 | `WorkJobResult/WorkJobResult.cs` | 基于 `ConcurrentDictionary` 存取任务结果或异常 |
| 调度配置 | `AsyncOptions/AsyncOptions.cs` | 定义最大并行度与事件钩子选项 |
| 示例程序 | `Program.cs` | 构建 10 个任务的复杂依赖链并运行 |
| 文档 | `README.md` | 当前文件，包含使用指南与示例 |

构建/运行：
```bash
cd /Users/xiaozhao/Desktop/xz/async-tool
# 构建
dotnet build
# 运行示例（Program.cs）
dotnet run
```

## ▶️ 运行流程示例

1. 🧱 **准备阶段**：根任务通过 `WorkJob.CreateBuilder()` 定义（参照 `@README.md (5-13)` 中左侧建造器节点），配置 Id、优先级等元数据。
2. 🚦 **调度阶段**：调用 `Async.Start` 后，调度器会根据依赖关系和优先级分发任务（对应 `@README.md (5-13)` 中的中间节点）。
3. 📥 **结果汇总**：每个任务完成后都会调用 `WorkJobResult.AddResult` 写入缓存，即便返回 `null` 也会记录，最终可在示例程序末尾统一打印（参考 `@README.md (5-13)` 的右侧结果存储节点）。
program.cs例子比较复杂，下面有介绍简单的例子
```c#
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
    .WithWork(async context =>
    {
        context.TryGetDependencyResult("load-users", out var usersResult);
        context.TryGetDependencyResult("load-orders", out var ordersResult);
        var usersSummary = usersResult?.ToString() ?? "null";
        var ordersSummary = ordersResult?.ToString() ?? "null";

        Console.WriteLine("[merge-data] 开始数据合并...");
        Console.WriteLine($"[merge-data] 依赖结果 -> load-users: {usersSummary}, load-orders: {ordersSummary}");
        for (var stage = 1; stage <= 3; stage++)
        {
            await Task.Delay(90);
            Console.WriteLine($"[merge-data] 合并阶段 {stage}/3 完成");
        }

        Console.WriteLine("[merge-data] 数据合并完成");
        return (object)$"merged:{usersSummary}+{ordersSummary}";
    })
    .Build();

> **提示**  
> `WithWork(Func<WorkJobExecutionContext, Task<object>>)` 会在执行时注入 `WorkJobExecutionContext`，其中包含：
> - `Param`：通过 `WithParam` 设置的自定义参数；
> - `DependencyResults`：以依赖任务 Id 为键的结果字典；
> - `DependencyValues`：依赖结果的顺序列表，与依赖声明顺序一致；
> - `TryGetDependencyResult(string workJobId, out object result)`：便捷地根据任务 Id 获取结果。
>
> 当任务依赖两个节点时，上下文中即可获得两个结果；依赖三个节点则可以获得三个结果。即使依赖任务返回 `null`，结果也会被记录并可正常读取。

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
```

示例运行输出：
```c#
[event] load-config started
[load-config] 开始加载配置...
[load-config] 正在处理配置段 1/3
[load-config] 正在处理配置段 2/3
[load-config] 正在处理配置段 3/3
[load-config] 配置加载完成
[event] load-config completed
[event] load-users started
[load-users] 开始拉取用户数据...
[event] load-orders started
[load-orders] 开始拉取订单数据...
[load-orders] 订单批次 1/5 已处理
[load-users] 第 1/4 页数据同步完成
[load-orders] 订单批次 2/5 已处理
[load-users] 第 2/4 页数据同步完成
[load-orders] 订单批次 3/5 已处理
[load-users] 第 3/4 页数据同步完成
[load-orders] 订单批次 4/5 已处理
[load-users] 第 4/4 页数据同步完成
[load-users] 用户数据拉取完成
[event] load-users completed
[event] archive-raw started
[archive-raw] 开始归档原始数据...
[load-orders] 订单批次 5/5 已处理
[load-orders] 订单数据拉取完成
[event] load-orders completed
[event] merge-data started
[merge-data] 开始数据合并...
[archive-raw] 正在上传分片 1/4 至 oss://raw-bucket
[merge-data] 合并阶段 1/3 完成
[archive-raw] 正在上传分片 2/4 至 oss://raw-bucket
[merge-data] 合并阶段 2/3 完成
[archive-raw] 正在上传分片 3/4 至 oss://raw-bucket
[merge-data] 合并阶段 3/3 完成
[merge-data] 数据合并完成
[event] merge-data completed
[event] train-model started
[train-model] 第 1 次模型训练开始
[archive-raw] 正在上传分片 4/4 至 oss://raw-bucket
[archive-raw] 原始数据归档至 oss://raw-bucket
[event] archive-raw completed
[train-model] Epoch 1/4 完成
[train-model] Epoch 2/4 完成
[train-model] Epoch 3/4 完成
[train-model] Epoch 4/4 完成
[train-model] 本次训练失败，准备重试
[train-model] 第 2 次模型训练开始
[train-model] Epoch 1/4 完成
[train-model] Epoch 2/4 完成
[train-model] Epoch 3/4 完成
[train-model] Epoch 4/4 完成
[train-model] 模型训练成功
[event] train-model completed
[event] generate-report started
[generate-report] 开始生成报告...
[event] cleanup-temp started
[cleanup-temp] 开始清理临时文件...
[cleanup-temp] 临时目录 1/3 清理完成
[generate-report] 报告章节 1/5 已生成
[cleanup-temp] 临时目录 2/3 清理完成
[generate-report] 报告章节 2/5 已生成
[cleanup-temp] 临时目录 3/3 清理完成
[cleanup-temp] 临时文件清理完成
[event] cleanup-temp completed
[generate-report] 报告章节 3/5 已生成
[generate-report] 报告章节 4/5 已生成
[generate-report] 报告章节 5/5 已生成
[generate-report] 报告生成完成
[event] generate-report completed
[event] notify-team started
[notify-team] 开始通知团队...
[event] audit-log started
[audit-log] 开始写入审计日志...
[audit-log] 第 1/5 条日志写入完成
[notify-team] 已通知 产品
[audit-log] 第 2/5 条日志写入完成
[notify-team] 已通知 运营
[audit-log] 第 3/5 条日志写入完成
[notify-team] 已通知 客服
[notify-team] 全部团队通知完成
[event] notify-team completed
[audit-log] 第 4/5 条日志写入完成
[audit-log] 第 5/5 条日志写入完成
[audit-log] 审计日志写入完成
[event] audit-log completed
任务组启动完成，Id: 780500985438
--- 结果汇总 ---
load-config: config:v1
load-users: users:128
load-orders: orders:256
merge-data: merged:ok
train-model: model:v2
generate-report: report:ready
notify-team: notify:sent
archive-raw: archive:oss://raw-bucket
cleanup-temp: cleanup:done
audit-log: audit:ok
```

---

## 🚀 快速上手：最小任务组示例

以下示例构建 4 个任务：`LoadConfig -> LoadUsers -> MergeData -> GenerateReport`，展示依赖链的基本用法。

```csharp
var loadConfig = WorkJob.CreateBuilder()
    .WithId("load-config")
    .WithPriority(100)
    .WithWork(async () =>
    {
        await Task.Delay(100);
        return (object)"config";
    })
    .Build();

var loadUsers = WorkJob.CreateBuilder()
    .WithId("load-users")
    .WithPriority(80)
    .WithWork(async () =>
    {
        await Task.Delay(120);
        return (object)"users";
    })
    .Build();

var mergeData = WorkJob.CreateBuilder()
    .WithId("merge-data")
    .WithPriority(60)
    .WithWork(async () =>
    {
        await Task.Delay(150);
        return (object)"merged";
    })
    .Build();

var generateReport = WorkJob.CreateBuilder()
    .WithId("generate-report")
    .WithPriority(40)
    .WithWork(async () =>
    {
        await Task.Delay(180);
        return (object)"report";
    })
    .Build();

loadConfig.Next(loadUsers);
loadUsers.Next(mergeData);
mergeData.Next(generateReport);

var options = new AsyncOptions
{
    MaxDegreeOfParallelism = 2,
    OnJobStarted = job => Console.WriteLine($"{job.WorkJobId} started")
};

var asyncId = await Async.StartAsync(new[] { loadConfig }, timeoutMilliseconds: 2000, options);

Console.WriteLine($"AsyncId={asyncId}");
Console.WriteLine(WorkJobResult.GetResult(AsyncUtil.GenerateId(asyncId, "generate-report")));
```

运行结果：
```
AsyncId=xxxxxxxxxxxx
report
```

---

## 🧩 核心能力详解（配 4+ 任务示例）

### 1. 复杂依赖与结果收集
- 使用建造者链式配置任务。
- 依赖通过 `Next` 串联，支持一个任务连接多个后续节点。
- 结果在任务结束后写入 `WorkJobResult`，可按需获取。

```csharp
var prepare = WorkJob.CreateBuilder()
    .WithId("prepare")
    .WithWork(async () =>
    {
        await Task.Delay(80);
        return (object)"ready";
    })
    .Build();

var fetchA = WorkJob.CreateBuilder()
    .WithId("fetch-a")
    .WithWork(async () =>
    {
        await Task.Delay(120);
        return (object)"A";
    })
    .Build();

var fetchB = WorkJob.CreateBuilder()
    .WithId("fetch-b")
    .WithWork(async () =>
    {
        await Task.Delay(150);
        return (object)"B";
    })
    .Build();

var aggregate = WorkJob.CreateBuilder()
    .WithId("aggregate")
    .WithWork(async () =>
    {
        await Task.Delay(200);
        return (object)"A+B";
    })
    .Build();

var finalize = WorkJob.CreateBuilder()
    .WithId("finalize")
    .WithWork(async () =>
    {
        await Task.Delay(100);
        return (object)"done";
    })
    .Build();

prepare.Next(fetchA);
prepare.Next(fetchB);
fetchA.Next(aggregate);
fetchB.Next(aggregate);
aggregate.Next(finalize);

var asId = await Async.StartAsync(new[] { prepare }, 5000);
var resultKey = AsyncUtil.GenerateId(asId, "finalize");
Console.WriteLine(WorkJobResult.GetResult(resultKey));
```

### 2. 任务优先级与并行度控制
- `WorkJob.WithPriority` 为任务赋予优先级，调度时会先执行权重更高的任务。
- 通过 `AsyncOptions.MaxDegreeOfParallelism` 限制同时运行的任务数量。

```csharp
var jobA = WorkJob.CreateBuilder()
    .WithId("A")
    .WithPriority(100)
    .WithWork(async () =>
    {
        await Task.Delay(200);
        return (object)"A";
    })
    .Build();

var jobB = WorkJob.CreateBuilder()
    .WithId("B")
    .WithPriority(80)
    .WithWork(async () =>
    {
        await Task.Delay(150);
        return (object)"B";
    })
    .Build();

var jobC = WorkJob.CreateBuilder()
    .WithId("C")
    .WithPriority(60)
    .WithWork(async () =>
    {
        await Task.Delay(120);
        return (object)"C";
    })
    .Build();

var jobD = WorkJob.CreateBuilder()
    .WithId("D")
    .WithPriority(40)
    .WithWork(async () =>
    {
        await Task.Delay(100);
        return (object)"D";
    })
    .Build();

var options = new AsyncOptions
{
    MaxDegreeOfParallelism = 2
};

await Async.StartAsync(new[] { jobA, jobB, jobC, jobD }, 2000, options);
```

---

### 3. 任务组停止（Stop）
- `Async.Stop` 会取消令牌、调用所有任务的 `Stop()` 并清理缓存。
- 适合用户主动取消或服务关闭场景。

```csharp
var longJob = WorkJob.CreateBuilder()
    .WithId("long")
    .WithWork(async () =>
    {
        await Task.Delay(5000);
        return (object)"long";
    })
    .Build();

var asId = await Async.StartAsync(new[] { longJob }, 10000);
await Task.Delay(500);
Async.Stop(asId);
```

> 停止后不再执行剩余逻辑，结果缓存同步清理。

### 4. 任务组超时
- 调用 `Async.StartAsync` 时传入超时，所有任务累计耗时若超出则终止。

```csharp
var slow = WorkJob.CreateBuilder()
    .WithId("slow")
    .WithWork(async () =>
    {
        await Task.Delay(3000);
        return (object)"slow";
    })
    .Build();

try
{
    await Async.StartAsync(new[] { slow }, timeoutMilliseconds: 1000);
}
catch (TimeoutException ex)
{
    Console.WriteLine(ex.Message);
}
```

### 5. 子任务超时
- `WorkJob.Timeout` 控制单个任务的最大执行时间。
- 内部使用 `CancellationTokenSource` + `WaitAsync` 实现。

```csharp
var timeoutJob = WorkJob.CreateBuilder()
    .WithId("timeout")
    .WithTimeout(200)
    .WithWork(async () =>
    {
        await Task.Delay(1000);
        return (object)"too-slow";
    })
    .Build();

try
{
    await Async.StartAsync(new[] { timeoutJob }, 5000);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message); // Job execution timed out
}
```

### 6. 子任务失败重试
- `WorkJob.Retry` 设置最大重试次数（失败后自动重试）。
- 捕获 `OperationCanceledException`、`TimeoutException` 及其他异常。

```csharp
var attempt = 0;
var retryJob = WorkJob.CreateBuilder()
    .WithId("retry")
    .WithRetry(2)
    .WithWork(async () =>
    {
        attempt++;
        if (attempt < 3)
        {
            throw new InvalidOperationException("fail");
        }

        await Task.Delay(100);
        return (object)"success";
    })
    .Build();

var retryAsId = await Async.StartAsync(new[] { retryJob }, 5000);
Console.WriteLine(WorkJobResult.GetResult(AsyncUtil.GenerateId(retryAsId, "retry")));
```

### 7. 忽略某些子任务（可选流转）
- `Next(optionalJob, isMust: false)` 不会将任务加入后续列表，达到“忽略”效果。

```csharp
var root = WorkJob.CreateBuilder()
    .WithId("root")
    .WithWork(async () =>
    {
        Console.WriteLine("Root running");
        return (object)"root";
    })
    .Build();

var optional = WorkJob.CreateBuilder()
    .WithId("optional")
    .WithWork(async () =>
    {
        Console.WriteLine("Optional running");
        return (object)"optional";
    })
    .Build();

root.Next(optional, isMust: false);
await Async.StartAsync(new[] { root }, 2000);
```

> 控制台仅输出 `Root running`，说明可选任务被跳过。

### 8. 任务结果管理
- `WorkJobResult` 使用 `
```

### AsyncOptions 配置项

| 选项 | 类型 | 说明 |
| --- | --- | --- |
| `MaxDegreeOfParallelism` | `int` | 最大并发任务数，`<=0` 表示不限制 |
| `OnJobStarted` | `Action<WorkJob>` | 任务真正进入 `Running` 状态时触发 |
| `OnJobCompleted` | `Action<WorkJob>` | 任务成功完成并写入结果后触发 |
| `OnJobFailed` | `Action<WorkJob, Exception>` | 任务失败（超时、异常、依赖失败等）时触发 |
```

## 🧪 自动化测试
- 测试项目：`AsyncTool.Tests`
- 覆盖场景：任务优先级与并发控制、任务重试成功、超时触发失败事件、可选任务跳过等核心容错机制。
- 运行命令：
```bash
dotnet test AsyncTool.Tests/AsyncTool.Tests.csproj
```

---

## 🧪 扩展示例：Program.cs 复杂任务组
```
- 合理配置 `AsyncOptions`：限制并行度并订阅事件钩子，便于监控任务进度。
```
