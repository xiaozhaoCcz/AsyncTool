# AsyncTool 项目文档

## 概览
`AsyncTool` 是一个基于 .NET 8 的轻量级异步任务调度器，提供如下核心能力：
- 以 `WorkJob` 为最小执行单元，可组合形成依赖链或有向图。
- 使用 `Async` 入口类负责调度、串联任务，并对任务组进行生命周期管理。
- 支持任务结果集中存储、查询与清理。
- 提供任务重试、执行超时控制、任务组超时退出、手工停止等完善的容错机制。

目录结构概览：
- `WorkJob/WorkJob.cs`：描述单个任务节点及其依赖关系、执行逻辑。
- `Async/Async.cs`：调度入口，负责启动、监控和停止整组任务。
- `AsyncUtil/AsyncUtil.cs`：提供令牌与任务集合的并发存储、ID 生成等工具。
- `WorkJobResult/WorkJobResult.cs`：线程安全地保存每个任务的输出结果或异常。
- `Program.cs`：示例程序，可直接运行演示核心流程。

构建/运行：
```bash
cd /Users/xiaozhao/Desktop/xz/async-tool
# 构建
dotnet build
# 运行示例流程
dotnet run
```

示例运行（`Program.cs` 默认示例）：
```
Root job finished
Child job received: payload
Async flow started with Id: 210111007963
Root result: RootResult
Child result: Processed payload
Execution finished.
```

---

## 1. 基础任务链执行
### 功能说明
- 通过 `WorkJob` 定义任务节点。
- 使用 `Next` 构建父子任务关系。
- 调用 `Async.StartAsync` 或 `Async.Start` 启动任务组。
- 结果通过 `WorkJobResult` 按 `AsyncId_WorkJobId` 键保存。

### 示例代码
```csharp
var root = WorkJob.CreateBuilder()
    .WithId("root")
    .WithWork(async () =>
    {
        await Task.Delay(300);
        Console.WriteLine("Root done");
        return (object)"root-result";
    })
    .Build();

var child = WorkJob.CreateBuilder()
    .WithId("child")
    .WithParam("data")
    .WithWork(async obj =>
    {
        await Task.Delay(200);
        Console.WriteLine($"Child received {obj}");
        return (object)$"child-result-{obj}";
    })
    .Build();

root.Next(child);

var asyncId = await Async.StartAsync(new[] { root }, 5000);

Console.WriteLine($"AsyncId={asyncId}");
Console.WriteLine($"Root result={WorkJobResult.GetResult(AsyncUtil.GenerateId(asyncId, "root"))}");
Console.WriteLine($"Child result={WorkJobResult.GetResult(AsyncUtil.GenerateId(asyncId, "child"))}");
```

### 运行结果
```
Root done
Child received data
AsyncId=xxxxxxxxxxxx
Root result=root-result
Child result=child-result-data
```

---

## 2. 任务组停止（Stop）
### 功能说明
- `Async.Stop(asId)` 会：
  - 取消对应的 `CancellationTokenSource`。
  - 调用每个任务的 `Stop()`，将其状态标记为 `Failed`。
  - 清理任务集合与缓存的执行结果。
- 可用于手动终止整组任务，例如 UI 取消或服务关闭时。

### 示例代码
```csharp
var longJob = WorkJob.CreateBuilder()
    .WithId("long")
    .WithWork(async () =>
    {
        await Task.Delay(2000);
        Console.WriteLine("Long job still running...");
        return (object)"long";
    })
    .Build();

var asyncId = await Async.StartAsync(new[] { longJob }, 10000);

// 500ms 后手动停止
await Task.Delay(500);
Async.Stop(asyncId);
```

### 运行结果
```
Execution finished.
```
> 停止后不会再看到 "Long job still running..."，并且对应该任务的结果已经被清理。

---

## 3. 任务组超时
### 功能说明
- `Async.StartAsync` 接收任务组超时时间（毫秒）。
- 任意任务链执行累计超过 `timeoutMilliseconds` 会触发 `TimeoutException("异步任务执行超时")`，并调用 `Async.Stop` 清理。

### 示例代码
```csharp
var slow = WorkJob.CreateBuilder()
    .WithId("slow")
    .WithWork(async () =>
    {
        await Task.Delay(2000);
        return (object)"slow";
    })
    .Build();

try
{
    await Async.StartAsync(new[] { slow }, timeoutMilliseconds: 500);
}
catch (TimeoutException ex)
{
    Console.WriteLine(ex.Message);
}
```

### 运行结果
```
异步任务执行超时。
```

---

## 4. 子任务超时
### 功能说明
- `WorkJob.Timeout(int milliseconds)` 为单个任务设置执行超时。
- 内部通过 `CancellationTokenSource` + `WaitAsync` 包裹任务委托，抛出 `TimeoutException("Job execution timed out")`。
- 支持同时配置 `_funcWithParam` 或 `_funcWithoutParam`，按需选择。

### 示例代码
```csharp
var timeoutJob = WorkJob.CreateBuilder()
    .WithId("timeout")
    .WithTimeout(200)
    .WithWork(async () =>
    {
        await Task.Delay(1000);
        return (object)"never";
    })
    .Build();

try
{
    await Async.StartAsync(new[] { timeoutJob }, 5000);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}
```

### 运行结果
```
Job execution timed out
```

---

## 5. 子任务失败重试
### 功能说明
- `WorkJob.Retry(int count)` 设置最大重试次数（失败后继续尝试的次数）。
- `_retryCount` 次内，遇到异常会继续重试；超过次数后标记 `Failed` 并向后续节点传播。
- 支持捕获 `OperationCanceledException`、`TimeoutException` 以及其他异常。

### 示例代码
```csharp
var attempts = 0;
var retryJob = WorkJob.CreateBuilder()
    .WithId("retry")
    .WithRetry(2)
    .WithWork(async () =>
    {
        attempts++;
        Console.WriteLine($"Attempt {attempts}");
        if (attempts < 3)
        {
            throw new InvalidOperationException("Simulated failure");
        }

        await Task.Delay(100);
        return (object)"success";
    })
    .Build();

var asyncId = await Async.StartAsync(new[] { retryJob }, 5000);
Console.WriteLine(WorkJobResult.GetResult(AsyncUtil.GenerateId(asyncId, "retry")));
```

### 运行结果
```
Attempt 1
Attempt 2
Attempt 3
success
```

---

## 6. 忽略某些子任务（可选流转）
### 功能说明
- 通过 `Next(optionalJob, isMust: false)` 可以在拓扑中跳过某个子任务。
- 当 `isMust` 为 `false` 时，该子任务不会加入 `_nextWorkJobs` 集合，相当于忽略执行。
- 适合在运行时按条件决定是否挂载任务。

### 示例代码
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

root.Next(optional, isMust: false); // 不会真正执行

await Async.StartAsync(new[] { root }, 2000);
```

### 运行结果
```
Root running
```
> 日志中未出现 "Optional running"，证明可选任务被忽略。

---

## 7. 任务结果管理
### 功能说明
- 执行成功后，`WorkJob.SaveResult` 将结果写入 `WorkJobResult`（线程安全字典）。
- 失败或超时后会把异常对象作为结果保存，便于外部诊断。
- `Async.Stop` 会在清理阶段同步移除相关结果，避免数据残留。

### 查询示例
```csharp
var asyncId = await Async.StartAsync(new[] { rootJob, childJob }, 5000);
var rootKey = AsyncUtil.GenerateId(asyncId, rootJob.WorkJobId!);
var rootResult = WorkJobResult.GetResult(rootKey);
Console.WriteLine(rootResult);
```

输出示例：
```
RootResult
```

---

## 8. 开发注意事项
- `WorkJob` 的执行委托需返回 `object`（可为任何引用或值类型，内部以 `object` 保存）。
- 构建依赖关系时务必确保不会形成死循环，否则将导致任务组无法完成。
- `Timeout` 与 `Retry` 可组合使用：先等待、超时抛出，再按重试策略继续尝试。
- 调度链路中任意节点失败会触发后续任务失败传播，必要时可在业务层自定义降级策略。

## 9. 扩展点建议
- 在 `WorkJobResult` 中增加结果持久化。
- 支持任务优先级及并行度控制。
- 加入事件/日志钩子，便于监控任务生命周期。
- 在 `Async` 中提供 `IObservable` 或回调，用于实时反馈阶段性进度。

---

如需进一步示例或将上述代码整合为自动化测试，可在 `Program.cs` 中构建命令行开关，或编写单元测试覆盖上述场景。欢迎根据业务需要扩展。
