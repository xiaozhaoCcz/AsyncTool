using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AsyncTool.Jobs
{
    /// <summary>
    /// 表示任务执行时可用的上下文信息，包含自定义参数以及依赖任务的结果集。
    /// </summary>
    public sealed class WorkJobExecutionContext
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyDictionary =
            new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

        private static readonly IReadOnlyList<object> EmptyList = Array.Empty<object>();

        private WorkJobExecutionContext(object? param, IReadOnlyDictionary<string, object> dependencyResults, IReadOnlyList<object> dependencyValues)
        {
            Param = param;
            DependencyResults = dependencyResults;
            DependencyValues = dependencyValues;
        }

        /// <summary>
        /// 用户在构建任务时指定的原始参数对象。
        /// </summary>
        public object? Param { get; }

        /// <summary>
        /// 依赖任务的结果映射，键为依赖任务的 Id。
        /// </summary>
        public IReadOnlyDictionary<string, object> DependencyResults { get; }

        /// <summary>
        /// 依赖任务的结果集合，按照依赖声明顺序排列。
        /// </summary>
        public IReadOnlyList<object> DependencyValues { get; }

        /// <summary>
        /// 尝试根据任务 Id 获取依赖的执行结果。
        /// </summary>
        public bool TryGetDependencyResult(string workJobId, out object result)
        {
            return DependencyResults.TryGetValue(workJobId, out result!);
        }

        internal static WorkJobExecutionContext Create(object? param, IReadOnlyDictionary<string, object>? dependencyResults, IReadOnlyList<object>? dependencyValues)
        {
            return new WorkJobExecutionContext(
                param,
                dependencyResults ?? EmptyDictionary,
                dependencyValues ?? EmptyList);
        }
    }
}

