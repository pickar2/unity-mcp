using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Thread-safe ring buffer for capturing Unity log messages with timestamps.
    /// </summary>
    public class LogBuffer
    {
        public struct LogEntry
        {
            public long SequenceId;
            public DateTime Timestamp;
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        public struct BufferStats
        {
            public int TotalCount;
            public int LogCount;
            public int WarningCount;
            public int ErrorCount;
            public long OldestSequenceId;
            public long LatestSequenceId;
        }

        public struct PagedResult
        {
            public List<LogEntry> Entries;
            public int NextCursor;
            public int TotalMatches;
            public bool HasMore;
        }

        private readonly LogEntry[] _buffer;
        private readonly int _capacity;
        private readonly object _lock = new object();

        private int _head = 0;      // Next write position
        private int _count = 0;     // Current entry count
        private long _sequenceCounter = 0;

        public LogBuffer(int capacity = 10000)
        {
            _capacity = capacity;
            _buffer = new LogEntry[capacity];
        }

        public void Add(LogType type, string message, string stackTrace)
        {
            lock (_lock)
            {
                _sequenceCounter++;

                _buffer[_head] = new LogEntry
                {
                    SequenceId = _sequenceCounter,
                    Timestamp = DateTime.UtcNow,
                    Type = type,
                    Message = message ?? "",
                    StackTrace = stackTrace
                };

                _head = (_head + 1) % _capacity;
                if (_count < _capacity)
                    _count++;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _count = 0;
                // Don't reset _sequenceCounter - it should always increase
            }
        }

        public List<LogEntry> Query(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterText = null,
            string filterRegex = null,
            int? count = null)
        {
            // First get all matching entries to determine total
            var allResult = QueryPaged(
                types: types,
                sinceSequenceId: sinceSequenceId,
                sinceTimestamp: sinceTimestamp,
                filterText: filterText,
                filterRegex: filterRegex,
                pageSize: int.MaxValue,
                cursor: 0
            );

            if (!count.HasValue || count.Value >= allResult.TotalMatches)
            {
                return allResult.Entries;
            }

            // Return only the last N (most recent) entries
            int startIdx = allResult.TotalMatches - count.Value;
            return allResult.Entries.GetRange(startIdx, count.Value);
        }

        public PagedResult QueryPaged(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterText = null,
            string filterRegex = null,
            int pageSize = 50,
            int cursor = 0)
        {
            Regex regex = null;
            if (!string.IsNullOrEmpty(filterRegex))
            {
                regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
            }

            var allMatches = new List<LogEntry>();

            lock (_lock)
            {
                if (_count == 0)
                {
                    return new PagedResult
                    {
                        Entries = allMatches,
                        NextCursor = 0,
                        TotalMatches = 0,
                        HasMore = false
                    };
                }

                // Calculate start index in ring buffer
                int start = (_head - _count + _capacity) % _capacity;

                for (int i = 0; i < _count; i++)
                {
                    int idx = (start + i) % _capacity;
                    var entry = _buffer[idx];

                    // Apply filters
                    if (types != null && types.Length > 0)
                    {
                        bool typeMatch = false;
                        foreach (var t in types)
                        {
                            if (entry.Type == t ||
                                (t == LogType.Error && (entry.Type == LogType.Exception || entry.Type == LogType.Assert)))
                            {
                                typeMatch = true;
                                break;
                            }
                        }
                        if (!typeMatch) continue;
                    }

                    if (sinceSequenceId.HasValue && entry.SequenceId <= sinceSequenceId.Value)
                        continue;

                    if (sinceTimestamp.HasValue && entry.Timestamp <= sinceTimestamp.Value)
                        continue;

                    if (!string.IsNullOrEmpty(filterText))
                    {
                        if (entry.Message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }

                    if (regex != null && !regex.IsMatch(entry.Message))
                        continue;

                    allMatches.Add(entry);
                }
            }

            // Apply pagination
            int totalMatches = allMatches.Count;
            int startIdx = Math.Min(cursor, totalMatches);
            int endIdx = Math.Min(startIdx + pageSize, totalMatches);

            var pagedEntries = allMatches.GetRange(startIdx, endIdx - startIdx);
            bool hasMore = endIdx < totalMatches;

            return new PagedResult
            {
                Entries = pagedEntries,
                NextCursor = hasMore ? endIdx : 0,
                TotalMatches = totalMatches,
                HasMore = hasMore
            };
        }

        public BufferStats GetStats(string filterText = null, string filterRegex = null)
        {
            Regex regex = null;
            if (!string.IsNullOrEmpty(filterRegex))
            {
                regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
            }

            var stats = new BufferStats();

            lock (_lock)
            {
                if (_count == 0)
                    return stats;

                int start = (_head - _count + _capacity) % _capacity;
                long oldest = long.MaxValue;
                long latest = 0;

                for (int i = 0; i < _count; i++)
                {
                    int idx = (start + i) % _capacity;
                    var entry = _buffer[idx];

                    // Track sequence range
                    if (entry.SequenceId < oldest) oldest = entry.SequenceId;
                    if (entry.SequenceId > latest) latest = entry.SequenceId;

                    // Apply text filters if provided
                    if (!string.IsNullOrEmpty(filterText))
                    {
                        if (entry.Message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                    if (regex != null && !regex.IsMatch(entry.Message))
                        continue;

                    stats.TotalCount++;
                    switch (entry.Type)
                    {
                        case LogType.Error:
                        case LogType.Exception:
                        case LogType.Assert:
                            stats.ErrorCount++;
                            break;
                        case LogType.Warning:
                            stats.WarningCount++;
                            break;
                        default:
                            stats.LogCount++;
                            break;
                    }
                }

                stats.OldestSequenceId = oldest == long.MaxValue ? 0 : oldest;
                stats.LatestSequenceId = latest;
            }

            return stats;
        }

        public long LatestSequenceId
        {
            get
            {
                lock (_lock)
                {
                    return _sequenceCounter;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }
    }
}
