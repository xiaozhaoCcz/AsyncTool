using System.Collections.Concurrent;

namespace AsyncTool.Results
{
    public static class WorkJobResult
    {
        private static readonly ConcurrentDictionary<string, object> _results = new();

        public static void AddResult(string id, object result)
        {
            _results[id] = result;
        }

        public static object? GetResult(string id)
        {
            _results.TryGetValue(id, out var value);
            return value;
        }

        public static void RemoveResult(string id)
        {
            _results.TryRemove(id, out _);
        }
    }
}