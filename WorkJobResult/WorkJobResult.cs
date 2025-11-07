using System.Collections.Concurrent;

namespace AsyncTool.Results
{
    /// <summary>
    /// 统一缓存工作任务的执行结果或异常，便于在流程外部查询。
    /// </summary>
    public static class WorkJobResult
    {
        private static readonly ConcurrentDictionary<string, object> _results = new();

        /// <summary>
        /// 写入或更新指定任务的执行结果。
        /// </summary>
        public static void AddResult(string id, object result)
        {
            _results[id] = result;
        }

        /// <summary>
        /// 根据任务 Id 读取结果。
        /// </summary>
        public static object? GetResult(string id)
        {
            _results.TryGetValue(id, out var value);
            return value;
        }

        /// <summary>
        /// 移除指定任务的结果。
        /// </summary>
        public static void RemoveResult(string id)
        {
            _results.TryRemove(id, out _);
        }
    }
}