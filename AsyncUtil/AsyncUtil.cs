using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsyncTool.Jobs;

namespace AsyncTool.Infrastructure
{
    /// <summary>
    /// 异步工具类，负责管理任务与令牌等辅助逻辑。
    /// </summary>
    public static class AsyncUtil
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _tokenDic = new();
        private static readonly ConcurrentDictionary<string, IReadOnlyList<WorkJob>> _workJobDic = new();

        private static readonly Random _global = new();
        private static readonly ThreadLocal<Random> _local = new(() =>
        {
            lock (_global)
            {
                return new Random(_global.Next());
            }
        });

        /// <summary>
        /// 获取指定 ID 的取消令牌。
        /// </summary>
        public static CancellationTokenSource? GetToken(string id)
        {
            _tokenDic.TryGetValue(id, out var token);
            return token;
        }

        /// <summary>
        /// 获取指定 ID 的工作任务集合。
        /// </summary>
        public static IEnumerable<WorkJob>? GetWorkJobs(string id)
        {
            _workJobDic.TryGetValue(id, out var workJobs);
            return workJobs;
        }

        /// <summary>
        /// 添加或替换指定 ID 的取消令牌。
        /// </summary>
        public static void AddToken(string id, CancellationTokenSource token)
        {
            _tokenDic[id] = token;
        }

        /// <summary>
        /// 添加或替换指定 ID 的工作任务集合。
        /// </summary>
        public static void AddWorkJobs(string id, IEnumerable<WorkJob> works)
        {
            var list = works?.ToList() ?? new List<WorkJob>();
            _workJobDic[id] = list;
        }

        /// <summary>
        /// 根据 ID 移除取消令牌。
        /// </summary>
        public static void RemoveToken(string id)
        {
            _tokenDic.TryRemove(id, out _);
        }

        /// <summary>
        /// 根据 ID 移除工作任务集合。
        /// </summary>
        public static void RemoveWorkJobs(string id)
        {
            _workJobDic.TryRemove(id, out _);
        }

        /// <summary>
        /// 生成 12 位随机数字串（首位为 1-9，其余为 0-9）。
        /// </summary>
        public static string Generate12Digit()
        {
            var random = _local.Value ?? CreateRandom();
            var sb = new StringBuilder(12);

            sb.Append(random.Next(1, 10));
            for (var i = 0; i < 11; i++)
            {
                sb.Append(random.Next(0, 10));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 组合异步任务 ID 与工作任务 ID。
        /// </summary>
        public static string GenerateId(string asId, string workId)
        {
            return string.Concat(asId, "_", workId);
        }

        private static Random CreateRandom()
        {
            lock (_global)
            {
                return new Random(_global.Next());
            }
        }
    }
}
