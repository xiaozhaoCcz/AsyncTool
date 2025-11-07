using System;
using AsyncTool.Jobs;

namespace AsyncTool.Options
{
    public class AsyncOptions
    {
        /// <summary>
        /// 最大并发执行的任务数。小于等于 0 表示不限制。
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; }

        /// <summary>
        /// 任务开始时触发。
        /// </summary>
        public Action<WorkJob>? OnJobStarted { get; set; }

        /// <summary>
        /// 任务成功完成时触发。
        /// </summary>
        public Action<WorkJob>? OnJobCompleted { get; set; }

        /// <summary>
        /// 任务失败时触发。
        /// </summary>
        public Action<WorkJob, Exception>? OnJobFailed { get; set; }
    }
}
