using System;
using AsyncTool.Jobs;

namespace AsyncTool.Infrastructure
{
    public static class WorkJobLinker
    {
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
