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
    .WithWork(async () =>
    {
        await Task.Delay(100);
        return (object)"config";
    })
    .Build();

var loadUsers = WorkJob.CreateBuilder()
    .WithId("load-users")
    .WithWork(async () =>
    {
        await Task.Delay(120);
        return (object)"users";
    })
    .Build();

var mergeData = WorkJob.CreateBuilder()
    .WithId("merge-data")
    .WithWork(async () =>
    {
        await Task.Delay(150);
        return (object)"merged";
    })
    .Build();

var generateReport = WorkJob.CreateBuilder()
    .WithId("generate-report")
    .WithWork(async () =>
    {
        await Task.Delay(180);
        return (object)"report";
    })
    .Build();

loadConfig.Next(loadUsers);
loadUsers.Next(mergeData);
mergeData.Next(generateReport);

var asyncId = await Async.StartAsync(new[] { loadConfig }, timeoutMilliseconds: 2000);

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

### 2. 任务组停止（Stop）
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

### 3. 任务组超时
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

### 4. 子任务超时
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

### 5. 子任务失败重试
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

### 6. 可选任务（忽略某一分支）
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

### 7. 任务结果管理
- `WorkJobResult` 使用 `ConcurrentDictionary`，线程安全。
- 成功写入业务返回值，失败写入捕获的异常对象。
- 调用 `Async.Stop` 会同步清空缓存数据。

```csharp
var job = WorkJob.CreateBuilder()
    .WithId("sample")
    .WithWork(async () =>
    {
        await Task.Delay(50);
        return (object)"ok";
    })
    .Build();

var sampleAsId = await Async.StartAsync(new[] { job }, 1000);
var key = AsyncUtil.GenerateId(sampleAsId, "sample");
Console.WriteLine(WorkJobResult.GetResult(key));
WorkJobResult.RemoveResult(key);
```

---

## 🛠️ 深入细节

### WorkJob 建造者方法速览

| 方法 | 作用 | 备注 |
| --- | --- | --- |
| `WithId(string)` | 设置任务唯一标识 | 必填，否则 `Build()` 抛异常 |
| `WithWork(Func<Task<object>>)` | 注册无参异步委托 | 与 `WithWork(Func<object, Task<object>>) 互斥` |
| `WithWork(Func<object, Task<object>>)` | 注册带参异步委托 | 默认从 `WithParam` 提供参数 |
| `WithParam(object)` | 设置执行参数 | 仅对带参委托生效 |
| `WithTimeout(int)` | 单任务超时控制（毫秒） | `<=0` 时视为不限时 |
| `WithRetry(int)` | 设置失败后重试次数 | 小于 0 时按 0 处理 |

### 状态流转

```
Start ──▶ Running ──▶ Finish
  │           │
  │           └──▶ Failed (异常/超时/手动 Stop)
  └──────────────▶ Failed (依赖任务失败)
```

- `WorkJob.Status` 提供任务当前状态。
- `PropagateFailure` 会将失败状态向后续任务传播。

### 调度流程

1. `Async.Start/StartAsync` 计算任务组 ID，注册令牌与任务集合。
2. 递归执行根任务及其依赖树，控制累积超时。
3. 任务完成后写入结果，失败则抛出异常并停止任务组。
4. `Stop` 在任何异常或手动调用时触发清理。

---

## ✅ 最佳实践
- 使用建造者统一创建任务，避免遗漏关键配置。
- 拆分任务为“小而专”的步骤，便于重试、超时处理。
- 为所有对外可访问的任务设置唯一 `Id`，方便结果检索。
- 合理设置超时与重试次数，避免任务悬挂或无限循环。
- 结合日志/监控系统记录 `AsyncId`，方便排查问题。

---

## 🧪 扩展示例：Program.cs 复杂任务组

示例程序构建 10 个任务，涵盖依赖、重试、超时、可选通知等场景，可直接运行观察日志：
`Program.cs`
```3:202:Program.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
// ... existing code ...
```

运行后可看到每个任务在 `for` 循环中的详细步骤、重试记录以及最终结果表。

---

## 📚 后续规划
- ✅ 建造者模式统一任务创建（已完成）。
- ☐ 增加任务优先级与并行度控制。
- ☐ 暴露事件钩子，支持 UI/日志实时展示任务进度。
- ☐ 提供单元测试样例，覆盖核心容错场景。

欢迎根据业务需求继续扩展，或在 `Program.cs` 中加入更多演示用例。🎯
