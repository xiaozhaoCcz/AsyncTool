using System;
using AsyncTool.Jobs;

namespace AsyncTool.Infrastructure
{
    /// <summary>
    /// 提供描述任务依赖关系的辅助方法，避免在业务代码中反复调用 <see cref="WorkJob.Next(WorkJob)"/>。
    /// </summary>
    public static class WorkJobLinker
    {
        /// <summary>
        /// 批量建立任务之间的依赖关系。
        /// </summary>
        /// <param name="edges">边集合：<c>From</c> 为父任务，<c>To</c> 为子任务，<c>IsMust</c> 表示是否强依赖。</param>
        public static void Link(params (WorkJob From, WorkJob To, bool IsMust)[] edges)
        {
            foreach (var (from, to, isMust) in edges)
            {
                if (isMust)
                {
                    from.Next(to);
                }
                else
                {
                    from.Next(to, false);
                }
            }
        }

        /// <summary>
        /// 将一组任务按顺序串联成链式依赖关系。
        /// </summary>
        /// <param name="isMust">是否强依赖，<c>true</c> 时必须执行父任务才执行子任务。</param>
        /// <param name="chain">顺序链上的任务集合。</param>
        public static void ConnectSequentially(bool isMust, params WorkJob[] chain)
        {
            if (chain == null || chain.Length < 2)
            {
                return;
            }

            for (var i = 0; i < chain.Length - 1; i++)
            {
                var from = chain[i];
                var to = chain[i + 1];
                if (isMust)
                {
                    from.Next(to);
                }
                else
                {
                    from.Next(to, false);
                }
            }
        }
    }
}
