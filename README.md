# AsyncTool 项目文档

> 异步任务调度工具，支持任务依赖、超时、重试、结果收集等功能，适合在服务端或桌面应用中快速构建复杂的任务流水线。

```
┌────────────┐      ┌────────────┐      ┌────────────┐
│ WorkJob 建造器 ──▶ │ Async 调度器 │ ──▶ │ WorkJobResult │
└────────────┘      └────────────┘      └────────────┘
        │                 │                     │
        │                 │                     └── 各任务结果集中存储
        │                 └── 控制任务启动/停止/超时
        └── 构建任务节点及依赖
```

---

## 📦 项目结构

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

示例运行输出（节选）：
```
[load-config] 开始加载配置...
⋮
任务组启动完成，Id: 125034115998
--- 结果汇总 ---
load-config: config:v1
⋮
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
