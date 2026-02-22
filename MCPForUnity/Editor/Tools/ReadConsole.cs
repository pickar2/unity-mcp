using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("read_console", AutoRegister = false)]
    public static class ReadConsole
    {
        private static MethodInfo _clearMethod;

        static ReadConsole()
        {
            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType != null)
                {
                    BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    _clearMethod = logEntriesType.GetMethod("Clear", staticFlags);
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ReadConsole] Failed to reflect LogEntries.Clear: {e.Message}");
            }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);
            string action = p.Get("action", "get").ToLower();

            try
            {
                if (action == "clear")
                {
                    return ClearConsole();
                }
                else if (action == "get")
                {
                    return GetConsoleEntries(p);
                }
                else
                {
                    return new ErrorResponse($"Unknown action: '{action}'. Valid actions are 'get' or 'clear'.");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ReadConsole] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        private static object ClearConsole()
        {
            LogCaptureService.Clear();

            // Also clear Unity's console for visual consistency
            try
            {
                _clearMethod?.Invoke(null, null);
            }
            catch (Exception e)
            {
                McpLog.Warn($"[ReadConsole] Failed to clear Unity console: {e.Message}");
            }

            return new SuccessResponse("Console cleared successfully.");
        }

        private static object GetConsoleEntries(ToolParams p)
        {
            var typesToken = p.GetRaw("types") as JArray;
            var types = typesToken?.Select(t => t.ToString().ToLower()).ToList()
                ?? new List<string> { "error", "warning", "log" };

            int? count = p.GetInt("count");
            int? pageSize = p.GetInt("pageSize");
            int? cursor = p.GetInt("cursor");
            string filterRegexStr = p.Get("filterRegex");
            long? sinceSequenceId = p.GetLong("sinceSequenceId");
            string sinceTimestampStr = p.Get("sinceTimestamp");
            bool countOnly = p.GetBool("countOnly", false);
            bool includeStacktrace = p.GetBool("includeStacktrace", false);

            if (!string.IsNullOrEmpty(filterRegexStr))
            {
                try
                {
                    new Regex(filterRegexStr, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException e)
                {
                    return new ErrorResponse($"Invalid regex pattern: {e.Message}");
                }
            }

            if (types.Contains("all"))
            {
                types = new List<string> { "error", "warning", "log" };
            }

            DateTime? sinceTimestamp = null;
            if (!string.IsNullOrEmpty(sinceTimestampStr))
            {
                if (DateTime.TryParse(sinceTimestampStr, out var parsed))
                {
                    sinceTimestamp = parsed.ToUniversalTime();
                }
            }

            var logTypes = ConvertToLogTypes(types);

            if (countOnly)
            {
                var stats = LogCaptureService.GetStats(filterRegexStr);
                return new SuccessResponse("Console entry counts.", new
                {
                    error = stats.ErrorCount,
                    warning = stats.WarningCount,
                    log = stats.LogCount,
                    total = stats.TotalCount,
                    latestSequenceId = stats.LatestSequenceId
                });
            }

            bool usePaging = pageSize.HasValue || cursor.HasValue;

            if (usePaging)
            {
                int resolvedPageSize = Math.Max(1, Math.Min(pageSize ?? 50, 5000));
                int resolvedCursor = Math.Max(0, cursor ?? 0);

                var result = LogCaptureService.QueryPaged(
                    types: logTypes,
                    sinceSequenceId: sinceSequenceId,
                    sinceTimestamp: sinceTimestamp,
                    filterRegex: filterRegexStr,
                    pageSize: resolvedPageSize,
                    cursor: resolvedCursor
                );

                var formattedEntries = FormatEntries(result.Entries, includeStacktrace);

                return new SuccessResponse($"Retrieved {formattedEntries.Count} log entries.", new
                {
                    entries = formattedEntries,
                    cursor = resolvedCursor,
                    pageSize = resolvedPageSize,
                    nextCursor = result.HasMore ? (int?)result.NextCursor : null,
                    totalMatches = result.TotalMatches,
                    hasMore = result.HasMore,
                    latestSequenceId = LogCaptureService.LatestSequenceId
                });
            }
            else
            {
                var entries = LogCaptureService.Query(
                    types: logTypes,
                    sinceSequenceId: sinceSequenceId,
                    sinceTimestamp: sinceTimestamp,
                    filterRegex: filterRegexStr,
                    count: count
                );

                var formattedEntries = FormatEntries(entries, includeStacktrace);

                return new SuccessResponse($"Retrieved {formattedEntries.Count} log entries.", new
                {
                    entries = formattedEntries,
                    latestSequenceId = LogCaptureService.LatestSequenceId
                });
            }
        }

        private static LogType[] ConvertToLogTypes(List<string> typeStrings)
        {
            var result = new List<LogType>();
            foreach (var t in typeStrings)
            {
                switch (t.ToLower())
                {
                    case "error":
                    case "exception":
                    case "assert":
                        if (!result.Contains(LogType.Error))
                            result.Add(LogType.Error);
                        break;
                    case "warning":
                        if (!result.Contains(LogType.Warning))
                            result.Add(LogType.Warning);
                        break;
                    case "log":
                        if (!result.Contains(LogType.Log))
                            result.Add(LogType.Log);
                        break;
                }
            }
            return result.ToArray();
        }

        private static List<object> FormatEntries(List<LogBuffer.LogEntry> entries, bool includeStacktrace)
        {
            var result = new List<object>();
            foreach (var entry in entries)
            {
                result.Add(new
                {
                    sequenceId = entry.SequenceId,
                    timestamp = entry.Timestamp.ToString("o"),
                    type = GetTypeString(entry.Type),
                    message = entry.Message,
                    stackTrace = includeStacktrace ? entry.StackTrace : null
                });
            }
            return result;
        }

        private static string GetTypeString(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return "error";
                case LogType.Warning:
                    return "warning";
                default:
                    return "log";
            }
        }
    }
}
