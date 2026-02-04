# Log Buffer Rewrite Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace reflection-based console reading with a hook-based ring buffer that captures logs with timestamps.

**Architecture:** Register `Application.logMessageReceived` callback to capture all Unity logs into a 10k-entry ring buffer. Each entry stores timestamp, sequence ID, type, message, and stack trace. Queries filter the buffer directly instead of using slow reflection APIs. Buffer auto-clears on domain reload; manual clear via `clear` action.

**Tech Stack:** Unity C# (Application.logMessageReceived), Python (FastMCP tool updates)

---

## Task 1: Create LogBuffer Data Structure

**Files:**
- Create: `MCPForUnity/Editor/Helpers/LogBuffer.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Helpers/LogBufferTests.cs`

**Step 1: Write the failing test for LogBuffer**

Create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Helpers/LogBufferTests.cs`:

```csharp
using System;
using NUnit.Framework;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnityTests.Editor.Helpers
{
    public class LogBufferTests
    {
        private LogBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _buffer = new LogBuffer(100); // Small buffer for testing
        }

        [Test]
        public void Add_SingleEntry_CanBeRetrieved()
        {
            _buffer.Add(LogType.Log, "Test message", "stack trace");

            var entries = _buffer.Query();

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Test message", entries[0].Message);
            Assert.AreEqual(LogType.Log, entries[0].Type);
            Assert.AreEqual("stack trace", entries[0].StackTrace);
            Assert.Greater(entries[0].SequenceId, 0);
            Assert.That(entries[0].Timestamp, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Add_ExceedsCapacity_OldestEntriesDropped()
        {
            // Fill buffer beyond capacity
            for (int i = 0; i < 150; i++)
            {
                _buffer.Add(LogType.Log, $"Message {i}", null);
            }

            var entries = _buffer.Query();

            Assert.AreEqual(100, entries.Count); // Capacity is 100
            Assert.AreEqual("Message 50", entries[0].Message); // Oldest retained
            Assert.AreEqual("Message 149", entries[99].Message); // Newest
        }

        [Test]
        public void Query_FilterByType_ReturnsOnlyMatchingTypes()
        {
            _buffer.Add(LogType.Log, "Info message", null);
            _buffer.Add(LogType.Warning, "Warning message", null);
            _buffer.Add(LogType.Error, "Error message", null);

            var errors = _buffer.Query(types: new[] { LogType.Error });

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual("Error message", errors[0].Message);
        }

        [Test]
        public void Query_FilterBySinceSequenceId_ReturnsOnlyNewer()
        {
            _buffer.Add(LogType.Log, "First", null);
            var firstEntries = _buffer.Query();
            long firstId = firstEntries[0].SequenceId;

            _buffer.Add(LogType.Log, "Second", null);
            _buffer.Add(LogType.Log, "Third", null);

            var newer = _buffer.Query(sinceSequenceId: firstId);

            Assert.AreEqual(2, newer.Count);
            Assert.AreEqual("Second", newer[0].Message);
            Assert.AreEqual("Third", newer[1].Message);
        }

        [Test]
        public void Query_FilterBySinceTimestamp_ReturnsOnlyNewer()
        {
            _buffer.Add(LogType.Log, "Old message", null);
            var beforeTime = DateTime.UtcNow;

            System.Threading.Thread.Sleep(50); // Ensure time difference

            _buffer.Add(LogType.Log, "New message", null);

            var newer = _buffer.Query(sinceTimestamp: beforeTime);

            Assert.AreEqual(1, newer.Count);
            Assert.AreEqual("New message", newer[0].Message);
        }

        [Test]
        public void Query_FilterByText_CaseInsensitive()
        {
            _buffer.Add(LogType.Log, "Hello World", null);
            _buffer.Add(LogType.Log, "Goodbye World", null);

            var matches = _buffer.Query(filterText: "hello");

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("Hello World", matches[0].Message);
        }

        [Test]
        public void Query_FilterByRegex_Works()
        {
            _buffer.Add(LogType.Log, "DIAGNOSTIC: error 123", null);
            _buffer.Add(LogType.Log, "INFO: normal", null);
            _buffer.Add(LogType.Log, "CONTACTS: particle 456", null);

            var matches = _buffer.Query(filterRegex: "DIAGNOSTIC|CONTACTS.*particle");

            Assert.AreEqual(2, matches.Count);
        }

        [Test]
        public void Query_WithCount_LimitsResults()
        {
            for (int i = 0; i < 50; i++)
            {
                _buffer.Add(LogType.Log, $"Message {i}", null);
            }

            var limited = _buffer.Query(count: 10);

            Assert.AreEqual(10, limited.Count);
            // Should return most recent
            Assert.AreEqual("Message 49", limited[9].Message);
        }

        [Test]
        public void Query_WithPagination_Works()
        {
            for (int i = 0; i < 50; i++)
            {
                _buffer.Add(LogType.Log, $"Message {i}", null);
            }

            var result = _buffer.QueryPaged(pageSize: 10, cursor: 0);

            Assert.AreEqual(10, result.Entries.Count);
            Assert.AreEqual(10, result.NextCursor);
            Assert.AreEqual(50, result.TotalMatches);
            Assert.IsTrue(result.HasMore);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            _buffer.Add(LogType.Log, "Message", null);

            _buffer.Clear();
            var entries = _buffer.Query();

            Assert.AreEqual(0, entries.Count);
        }

        [Test]
        public void GetStats_ReturnsCorrectCounts()
        {
            _buffer.Add(LogType.Log, "Info", null);
            _buffer.Add(LogType.Warning, "Warn", null);
            _buffer.Add(LogType.Error, "Err1", null);
            _buffer.Add(LogType.Error, "Err2", null);

            var stats = _buffer.GetStats();

            Assert.AreEqual(1, stats.LogCount);
            Assert.AreEqual(1, stats.WarningCount);
            Assert.AreEqual(2, stats.ErrorCount);
            Assert.AreEqual(4, stats.TotalCount);
        }

        [Test]
        public void SequenceId_AlwaysIncreases()
        {
            _buffer.Add(LogType.Log, "First", null);
            _buffer.Add(LogType.Log, "Second", null);

            var entries = _buffer.Query();

            Assert.Less(entries[0].SequenceId, entries[1].SequenceId);
        }
    }
}
```

**Step 2: Run test to verify it fails**

Open Unity Test Runner, run `LogBufferTests`. Expected: Compilation error - `LogBuffer` type doesn't exist.

**Step 3: Write LogBuffer implementation**

Create `MCPForUnity/Editor/Helpers/LogBuffer.cs`:

```csharp
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
            var result = QueryPaged(
                types: types,
                sinceSequenceId: sinceSequenceId,
                sinceTimestamp: sinceTimestamp,
                filterText: filterText,
                filterRegex: filterRegex,
                pageSize: count ?? int.MaxValue,
                cursor: 0
            );
            return result.Entries;
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
```

**Step 4: Create meta file for LogBuffer.cs**

Unity requires `.meta` files. Create `MCPForUnity/Editor/Helpers/LogBuffer.cs.meta`:

```
fileFormatVersion: 2
guid: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

**Step 5: Run tests to verify they pass**

Open Unity Test Runner, run all `LogBufferTests`. Expected: All 12 tests PASS.

**Step 6: Commit**

```bash
git add MCPForUnity/Editor/Helpers/LogBuffer.cs MCPForUnity/Editor/Helpers/LogBuffer.cs.meta TestProjects/UnityMCPTests/Assets/Tests/EditMode/Helpers/LogBufferTests.cs
git commit -m "feat(logs): add LogBuffer ring buffer data structure

10k-entry ring buffer with timestamps, sequence IDs, and efficient
query filtering by type, time, text, and regex."
```

---

## Task 2: Create LogCapture Service (Hook Registration)

**Files:**
- Create: `MCPForUnity/Editor/Services/LogCaptureService.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Services/LogCaptureServiceTests.cs`

**Step 1: Write the failing test for LogCaptureService**

Create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Services/LogCaptureServiceTests.cs`:

```csharp
using System;
using NUnit.Framework;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnityTests.Editor.Services
{
    public class LogCaptureServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            // Ensure service is initialized and buffer is clear
            LogCaptureService.Initialize();
            LogCaptureService.Clear();
        }

        [Test]
        public void CapturesDebugLog()
        {
            string uniqueMsg = $"Test-{Guid.NewGuid()}";
            Debug.Log(uniqueMsg);

            var entries = LogCaptureService.Query(types: new[] { LogType.Log });

            Assert.IsTrue(entries.Exists(e => e.Message.Contains(uniqueMsg)),
                "Should capture Debug.Log message");
        }

        [Test]
        public void CapturesDebugWarning()
        {
            string uniqueMsg = $"Warn-{Guid.NewGuid()}";
            Debug.LogWarning(uniqueMsg);

            var entries = LogCaptureService.Query(types: new[] { LogType.Warning });

            Assert.IsTrue(entries.Exists(e => e.Message.Contains(uniqueMsg)),
                "Should capture Debug.LogWarning message");
        }

        [Test]
        public void CapturesDebugError()
        {
            string uniqueMsg = $"Error-{Guid.NewGuid()}";
            Debug.LogError(uniqueMsg);

            var entries = LogCaptureService.Query(types: new[] { LogType.Error });

            Assert.IsTrue(entries.Exists(e => e.Message.Contains(uniqueMsg)),
                "Should capture Debug.LogError message");
        }

        [Test]
        public void HasTimestamp()
        {
            var before = DateTime.UtcNow;
            Debug.Log($"Timestamp-{Guid.NewGuid()}");
            var after = DateTime.UtcNow;

            var entries = LogCaptureService.Query();
            var entry = entries[entries.Count - 1]; // Most recent

            Assert.GreaterOrEqual(entry.Timestamp, before);
            Assert.LessOrEqual(entry.Timestamp, after);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            Debug.Log($"ToClear-{Guid.NewGuid()}");

            LogCaptureService.Clear();
            var entries = LogCaptureService.Query();

            // May have some system logs, but our specific message should be gone
            Assert.AreEqual(0, entries.Count);
        }

        [Test]
        public void SinceSequenceId_ReturnsOnlyNewer()
        {
            Debug.Log($"First-{Guid.NewGuid()}");
            long afterFirst = LogCaptureService.LatestSequenceId;

            Debug.Log($"Second-{Guid.NewGuid()}");
            Debug.Log($"Third-{Guid.NewGuid()}");

            var newer = LogCaptureService.Query(sinceSequenceId: afterFirst);

            Assert.AreEqual(2, newer.Count);
        }

        [Test]
        public void Instance_IsSingleton()
        {
            var buffer1 = LogCaptureService.Buffer;
            var buffer2 = LogCaptureService.Buffer;

            Assert.AreSame(buffer1, buffer2);
        }
    }
}
```

**Step 2: Run test to verify it fails**

Open Unity Test Runner, run `LogCaptureServiceTests`. Expected: Compilation error - `LogCaptureService` type doesn't exist.

**Step 3: Write LogCaptureService implementation**

Create `MCPForUnity/Editor/Services/LogCaptureService.cs`:

```csharp
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Singleton service that captures Unity log messages into a ring buffer.
    /// Auto-initializes on domain load via InitializeOnLoad.
    /// </summary>
    [InitializeOnLoad]
    public static class LogCaptureService
    {
        private static LogBuffer _buffer;
        private static bool _isSubscribed = false;
        private static readonly object _initLock = new object();

        public const int DefaultCapacity = 10000;

        static LogCaptureService()
        {
            Initialize();
        }

        public static void Initialize(int capacity = DefaultCapacity)
        {
            lock (_initLock)
            {
                if (_buffer == null)
                {
                    _buffer = new LogBuffer(capacity);
                }

                if (!_isSubscribed)
                {
                    Application.logMessageReceived += OnLogMessageReceived;
                    _isSubscribed = true;
                }
            }
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            // Extract just the first line for the message (stack trace is separate)
            string firstLine = message;
            int newlineIdx = message.IndexOf('\n');
            if (newlineIdx > 0)
            {
                firstLine = message.Substring(0, newlineIdx);
            }

            _buffer?.Add(type, firstLine, stackTrace);
        }

        public static LogBuffer Buffer
        {
            get
            {
                Initialize();
                return _buffer;
            }
        }

        public static void Clear()
        {
            _buffer?.Clear();
        }

        public static List<LogBuffer.LogEntry> Query(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterText = null,
            string filterRegex = null,
            int? count = null)
        {
            Initialize();
            return _buffer.Query(types, sinceSequenceId, sinceTimestamp, filterText, filterRegex, count);
        }

        public static LogBuffer.PagedResult QueryPaged(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterText = null,
            string filterRegex = null,
            int pageSize = 50,
            int cursor = 0)
        {
            Initialize();
            return _buffer.QueryPaged(types, sinceSequenceId, sinceTimestamp, filterText, filterRegex, pageSize, cursor);
        }

        public static LogBuffer.BufferStats GetStats(string filterText = null, string filterRegex = null)
        {
            Initialize();
            return _buffer.GetStats(filterText, filterRegex);
        }

        public static long LatestSequenceId
        {
            get
            {
                Initialize();
                return _buffer.LatestSequenceId;
            }
        }
    }
}
```

**Step 4: Create meta file**

Create `MCPForUnity/Editor/Services/LogCaptureService.cs.meta`:

```
fileFormatVersion: 2
guid: b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

**Step 5: Run tests to verify they pass**

Open Unity Test Runner, run all `LogCaptureServiceTests`. Expected: All 7 tests PASS.

**Step 6: Commit**

```bash
git add MCPForUnity/Editor/Services/LogCaptureService.cs MCPForUnity/Editor/Services/LogCaptureService.cs.meta TestProjects/UnityMCPTests/Assets/Tests/EditMode/Services/LogCaptureServiceTests.cs
git commit -m "feat(logs): add LogCaptureService with Application.logMessageReceived hook

Singleton service auto-initializes on domain load, captures all Unity
logs with timestamps into the ring buffer."
```

---

## Task 3: Rewrite ReadConsole to Use LogCaptureService

**Files:**
- Modify: `MCPForUnity/Editor/Tools/ReadConsole.cs` (full rewrite)
- Modify: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ReadConsoleTests.cs`

**Step 1: Update existing tests for new API**

Replace `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ReadConsoleTests.cs`:

```csharp
using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Services;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ReadConsoleTests
    {
        [SetUp]
        public void SetUp()
        {
            LogCaptureService.Clear();
        }

        [Test]
        public void HandleCommand_Clear_Works()
        {
            Debug.Log("Log to clear");

            var getBefore = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" }
            }));
            Assert.IsTrue(getBefore.Value<bool>("success"));
            var dataBefore = getBefore["data"] as JObject;
            Assert.Greater(dataBefore?["entries"]?.Count() ?? 0, 0, "Should have logs before clear");

            var result = ToJObject(ReadConsole.HandleCommand(new JObject { ["action"] = "clear" }));
            Assert.IsTrue(result.Value<bool>("success"));

            var getAfter = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" }
            }));
            Assert.IsTrue(getAfter.Value<bool>("success"));
            var dataAfter = getAfter["data"] as JObject;
            Assert.AreEqual(0, dataAfter?["entries"]?.Count() ?? 0, "Should be empty after clear");
        }

        [Test]
        public void HandleCommand_Get_Works()
        {
            string uniqueMessage = $"Test Log Message {Guid.NewGuid()}";
            Debug.Log(uniqueMessage);

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["count"] = 100
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"] as JObject;
            var entries = data?["entries"] as JArray;
            Assert.IsNotNull(entries);
            Assert.Greater(entries.Count, 0);

            bool found = false;
            foreach (var entry in entries)
            {
                if (entry["message"]?.ToString().Contains(uniqueMessage) == true)
                {
                    found = true;
                    // Verify timestamp exists
                    Assert.IsNotNull(entry["timestamp"], "Entry should have timestamp");
                    Assert.IsNotNull(entry["sequenceId"], "Entry should have sequenceId");
                    break;
                }
            }
            Assert.IsTrue(found, $"Message '{uniqueMessage}' not found");
        }

        [Test]
        public void HandleCommand_Get_WithSinceSequenceId_ReturnsOnlyNewer()
        {
            Debug.Log($"First-{Guid.NewGuid()}");

            var firstResult = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" }
            }));
            var latestId = firstResult["data"]?["latestSequenceId"]?.Value<long>() ?? 0;

            Debug.Log($"Second-{Guid.NewGuid()}");
            Debug.Log($"Third-{Guid.NewGuid()}");

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["sinceSequenceId"] = latestId
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var entries = result["data"]?["entries"] as JArray;
            Assert.AreEqual(2, entries?.Count, "Should return only 2 newer entries");
        }

        [Test]
        public void HandleCommand_Get_WithRegexFilter_MatchesPattern()
        {
            string uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            Debug.Log($"DIAGNOSTIC-{uniqueId}: particle 1842 contact");
            Debug.Log($"UNRELATED-{uniqueId}: some other message");
            Debug.Log($"CONTACTS-{uniqueId}: particle 1842 data");

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["filterRegex"] = $"DIAGNOSTIC|CONTACTS.*particle 1842",
                ["count"] = 100
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var entries = result["data"]?["entries"] as JArray;
            Assert.IsNotNull(entries);

            int matchCount = 0;
            bool foundUnrelated = false;
            foreach (var entry in entries)
            {
                var msg = entry["message"]?.ToString() ?? "";
                if (msg.Contains(uniqueId))
                {
                    if (msg.Contains("DIAGNOSTIC") || msg.Contains("CONTACTS"))
                        matchCount++;
                    if (msg.Contains("UNRELATED"))
                        foundUnrelated = true;
                }
            }

            Assert.AreEqual(2, matchCount, "Should match DIAGNOSTIC and CONTACTS");
            Assert.IsFalse(foundUnrelated, "Should not match UNRELATED");
        }

        [Test]
        public void HandleCommand_Get_WithBothFilters_ReturnsError()
        {
            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["filterText"] = "some text",
                ["filterRegex"] = "some.*pattern"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("filterText").Or.Contain("filterRegex"));
        }

        [Test]
        public void HandleCommand_Get_WithInvalidRegex_ReturnsError()
        {
            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["filterRegex"] = "[invalid(regex"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString().ToLower(), Does.Contain("regex").Or.Contain("pattern"));
        }

        [Test]
        public void HandleCommand_Get_CountOnly_ReturnsStats()
        {
            Debug.Log($"Info-{Guid.NewGuid()}");
            Debug.LogWarning($"Warn-{Guid.NewGuid()}");
            Debug.LogError($"Error-{Guid.NewGuid()}");

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["countOnly"] = true
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.GreaterOrEqual(data["log"]?.Value<int>() ?? 0, 1);
            Assert.GreaterOrEqual(data["warning"]?.Value<int>() ?? 0, 1);
            Assert.GreaterOrEqual(data["error"]?.Value<int>() ?? 0, 1);
            Assert.GreaterOrEqual(data["total"]?.Value<int>() ?? 0, 3);
        }

        [Test]
        public void HandleCommand_Get_Pagination_Works()
        {
            for (int i = 0; i < 25; i++)
            {
                Debug.Log($"Page-{Guid.NewGuid().ToString().Substring(0,8)}-{i}");
            }

            var result = ToJObject(ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["types"] = new JArray { "log" },
                ["pageSize"] = 10,
                ["cursor"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var data = result["data"] as JObject;
            var entries = data?["entries"] as JArray;
            Assert.AreEqual(10, entries?.Count);
            Assert.IsNotNull(data?["nextCursor"]);
            Assert.Greater(data?["totalMatches"]?.Value<int>() ?? 0, 10);
        }
    }
}
```

**Step 2: Run tests to verify they fail**

Open Unity Test Runner, run `ReadConsoleTests`. Expected: Most tests fail because ReadConsole still uses old reflection API.

**Step 3: Rewrite ReadConsole.cs**

Replace `MCPForUnity/Editor/Tools/ReadConsole.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles reading and clearing Unity Editor console log entries.
    /// Uses LogCaptureService ring buffer for efficient querying with timestamps.
    /// </summary>
    [McpForUnityTool("read_console", AutoRegister = false)]
    public static class ReadConsole
    {
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
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions are 'get' or 'clear'."
                    );
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
            try
            {
                LogCaptureService.Clear();

                // Also clear Unity's console for consistency
                var logEntriesType = typeof(UnityEditor.EditorApplication).Assembly
                    .GetType("UnityEditor.LogEntries");
                var clearMethod = logEntriesType?.GetMethod("Clear",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                clearMethod?.Invoke(null, null);

                return new SuccessResponse("Console cleared successfully.");
            }
            catch (Exception e)
            {
                McpLog.Error($"[ReadConsole] Failed to clear console: {e}");
                return new ErrorResponse($"Failed to clear console: {e.Message}");
            }
        }

        private static object GetConsoleEntries(ToolParams p)
        {
            // Extract parameters
            var typesRaw = (p.GetRaw("types") as JArray)?.ToObject<List<string>>()
                ?? new List<string> { "error", "warning" };
            int? count = p.GetInt("count");
            int? pageSize = p.GetInt("pageSize");
            int? cursor = p.GetInt("cursor");
            string filterText = p.Get("filterText");
            string filterRegexStr = p.Get("filterRegex");
            long? sinceSequenceId = p.GetLong("sinceSequenceId");
            string sinceTimestampStr = p.Get("sinceTimestamp");
            bool countOnly = p.GetBool("countOnly", false);
            bool includeStacktrace = p.GetBool("includeStacktrace", false);

            // Validate mutual exclusivity
            if (!string.IsNullOrEmpty(filterText) && !string.IsNullOrEmpty(filterRegexStr))
            {
                return new ErrorResponse("Cannot use both filterText and filterRegex - choose one.");
            }

            // Validate regex if provided
            if (!string.IsNullOrEmpty(filterRegexStr))
            {
                try
                {
                    new Regex(filterRegexStr);
                }
                catch (ArgumentException e)
                {
                    return new ErrorResponse($"Invalid regex pattern: {e.Message}");
                }
            }

            // Parse timestamp
            DateTime? sinceTimestamp = null;
            if (!string.IsNullOrEmpty(sinceTimestampStr))
            {
                if (DateTime.TryParse(sinceTimestampStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                {
                    sinceTimestamp = parsed.ToUniversalTime();
                }
                else
                {
                    return new ErrorResponse($"Invalid timestamp format: '{sinceTimestampStr}'. Use ISO 8601.");
                }
            }

            // Convert type strings to LogType array
            var logTypes = ConvertTypes(typesRaw);

            // Count only mode
            if (countOnly)
            {
                var stats = LogCaptureService.GetStats(filterText, filterRegexStr);
                return new SuccessResponse("Console entry counts.", new
                {
                    error = stats.ErrorCount,
                    warning = stats.WarningCount,
                    log = stats.LogCount,
                    total = stats.TotalCount,
                    latestSequenceId = stats.LatestSequenceId,
                    oldestSequenceId = stats.OldestSequenceId
                });
            }

            // Paged query
            bool usePaging = pageSize.HasValue || cursor.HasValue;
            if (usePaging)
            {
                var result = LogCaptureService.QueryPaged(
                    types: logTypes,
                    sinceSequenceId: sinceSequenceId,
                    sinceTimestamp: sinceTimestamp,
                    filterText: filterText,
                    filterRegex: filterRegexStr,
                    pageSize: pageSize ?? 50,
                    cursor: cursor ?? 0
                );

                return new SuccessResponse($"Retrieved {result.Entries.Count} log entries.", new
                {
                    entries = FormatEntries(result.Entries, includeStacktrace),
                    cursor = cursor ?? 0,
                    pageSize = pageSize ?? 50,
                    nextCursor = result.HasMore ? (int?)result.NextCursor : null,
                    totalMatches = result.TotalMatches,
                    hasMore = result.HasMore,
                    latestSequenceId = LogCaptureService.LatestSequenceId
                });
            }

            // Simple query with count limit
            var entries = LogCaptureService.Query(
                types: logTypes,
                sinceSequenceId: sinceSequenceId,
                sinceTimestamp: sinceTimestamp,
                filterText: filterText,
                filterRegex: filterRegexStr,
                count: count ?? 100
            );

            return new SuccessResponse($"Retrieved {entries.Count} log entries.", new
            {
                entries = FormatEntries(entries, includeStacktrace),
                latestSequenceId = LogCaptureService.LatestSequenceId
            });
        }

        private static LogType[] ConvertTypes(List<string> typeStrings)
        {
            var types = new List<LogType>();
            foreach (var t in typeStrings)
            {
                var lower = t.ToLower();
                if (lower == "all")
                {
                    return new[] { LogType.Log, LogType.Warning, LogType.Error, LogType.Exception, LogType.Assert };
                }
                else if (lower == "error")
                {
                    types.Add(LogType.Error);
                }
                else if (lower == "warning")
                {
                    types.Add(LogType.Warning);
                }
                else if (lower == "log")
                {
                    types.Add(LogType.Log);
                }
                else if (lower == "exception")
                {
                    types.Add(LogType.Exception);
                }
                else if (lower == "assert")
                {
                    types.Add(LogType.Assert);
                }
            }
            return types.ToArray();
        }

        private static List<object> FormatEntries(List<LogBuffer.LogEntry> entries, bool includeStacktrace)
        {
            var result = new List<object>();
            foreach (var entry in entries)
            {
                result.Add(new
                {
                    sequenceId = entry.SequenceId,
                    timestamp = entry.Timestamp.ToString("o"), // ISO 8601
                    type = entry.Type.ToString(),
                    message = entry.Message,
                    stackTrace = includeStacktrace ? entry.StackTrace : null
                });
            }
            return result;
        }
    }
}
```

**Step 4: Add GetLong helper to ToolParams**

Check if `ToolParams` has `GetLong`. If not, add to `MCPForUnity/Editor/Helpers/ToolParams.cs`:

```csharp
public long? GetLong(string name, string altName = null)
{
    var raw = GetRaw(name);
    if (raw == null && altName != null)
        raw = GetRaw(altName);

    if (raw == null) return null;

    if (raw.Type == JTokenType.Integer)
        return raw.Value<long>();

    if (raw.Type == JTokenType.String && long.TryParse(raw.Value<string>(), out var result))
        return result;

    return null;
}
```

**Step 5: Run tests to verify they pass**

Open Unity Test Runner, run all `ReadConsoleTests`. Expected: All tests PASS.

**Step 6: Commit**

```bash
git add MCPForUnity/Editor/Tools/ReadConsole.cs MCPForUnity/Editor/Helpers/ToolParams.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ReadConsoleTests.cs
git commit -m "feat(logs): rewrite ReadConsole to use LogCaptureService

Replace slow reflection-based log reading with efficient ring buffer queries.
All log entries now include timestamps and sequence IDs."
```

---

## Task 4: Update Python read_console Tool

**Files:**
- Modify: `Server/src/services/tools/read_console.py`

**Step 1: Update the Python tool with new parameters**

Replace `Server/src/services/tools/read_console.py`:

```python
"""
Defines the read_console tool for accessing Unity Editor console messages.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool, parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Read or clear Unity console messages with timestamps.

NOTE: To check for compilation errors before playing, use manage_editor(action='play', recompile=true) instead.
That handles compile + error check + play in one call. Only use read_console for debugging/diagnostics.

Key features:
- Timestamps on all entries (ISO 8601 UTC)
- Sequence IDs for efficient "what's new since last check" queries
- Ring buffer holds last 10k messages

Options:
- action: 'get' (default) or 'clear'
- types: ['error', 'warning', 'log', 'all'] - message types to include
- count: Max messages (default 100). Use page_size/cursor for pagination.
- since_sequence_id: Get only messages after this ID (efficient for polling)
- since_timestamp: Get only messages after this ISO 8601 time
- filter_text: Substring filter (case-insensitive)
- filter_regex: Regex filter (case-insensitive). Mutually exclusive with filter_text.
- count_only: Return only counts by type, not entries
- include_stacktrace: Include stack traces in output""",
    annotations=ToolAnnotations(
        title="Read Console",
    ),
)
async def read_console(
    ctx: Context,
    action: Annotated[Literal['get', 'clear'],
                      "Get or clear the Unity Editor console. Defaults to 'get'."] | None = None,
    types: Annotated[list[Literal['error', 'warning', 'log', 'all']] | str,
                     "Message types to get (accepts list or JSON string)"] | None = None,
    count: Annotated[int | str,
                     "Max messages to return (default 100). Ignored when paging."] | None = None,
    since_sequence_id: Annotated[int | str,
                                  "Get only messages with sequenceId > this value. Most efficient for 'what's new'."] | None = None,
    since_timestamp: Annotated[str,
                               "Get only messages after this ISO 8601 timestamp"] | None = None,
    filter_text: Annotated[str,
                           "Text filter (case-insensitive substring). Mutually exclusive with filter_regex."] | None = None,
    filter_regex: Annotated[str,
                            "Regex filter (case-insensitive). Mutually exclusive with filter_text."] | None = None,
    page_size: Annotated[int | str,
                         "Page size for pagination (default 50)"] | None = None,
    cursor: Annotated[int | str,
                      "Cursor for pagination (0-based offset)"] | None = None,
    count_only: Annotated[bool | str,
                          "Return only counts by type, not entries"] | None = None,
    include_stacktrace: Annotated[bool | str,
                                  "Include stack traces in output"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    action = action if action is not None else 'get'

    # Parse types if JSON string
    if isinstance(types, str):
        types = parse_json_payload(types)
    if types is not None and not isinstance(types, list):
        return {
            "success": False,
            "message": f"types must be a list, got {type(types).__name__}"
        }
    if types is not None:
        allowed_types = {"error", "warning", "log", "all"}
        normalized = [t.strip().lower() for t in types if isinstance(t, str)]
        invalid = [t for t in normalized if t not in allowed_types]
        if invalid:
            return {"success": False, "message": f"Invalid types: {invalid}. Allowed: {sorted(allowed_types)}"}
        types = normalized
    else:
        types = ['error', 'warning', 'log']

    # Validate mutual exclusivity
    if filter_text and filter_regex:
        return {"success": False, "message": "Cannot use both filter_text and filter_regex - choose one."}

    # Coerce parameters
    count_only = coerce_bool(count_only, default=False)
    include_stacktrace = coerce_bool(include_stacktrace, default=False)
    coerced_count = coerce_int(count) if count is not None else None
    coerced_page_size = coerce_int(page_size) if page_size is not None else None
    coerced_cursor = coerce_int(cursor) if cursor is not None else None
    coerced_since_seq = coerce_int(since_sequence_id) if since_sequence_id is not None else None

    # Default count for get action
    if action == "get" and coerced_count is None and coerced_page_size is None:
        coerced_count = 100

    params_dict = {
        "action": action,
        "types": types,
        "count": coerced_count,
        "sinceSequenceId": coerced_since_seq,
        "sinceTimestamp": since_timestamp,
        "filterText": filter_text,
        "filterRegex": filter_regex,
        "pageSize": coerced_page_size,
        "cursor": coerced_cursor,
        "countOnly": count_only,
        "includeStacktrace": include_stacktrace,
    }
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    resp = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "read_console", params_dict
    )

    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
```

**Step 2: Run Python tests**

```bash
cd Server && uv run pytest tests/ -v -k "read_console" --tb=short
```

Expected: Tests pass (or need minor updates for new response format).

**Step 3: Commit**

```bash
git add Server/src/services/tools/read_console.py
git commit -m "feat(logs): update read_console Python tool for new buffer API

Add since_sequence_id for efficient polling, timestamps on all entries,
and improved documentation."
```

---

## Task 5: Add Integration Tests

**Files:**
- Modify: `Server/tests/integration/` - add read_console tests if missing

**Step 1: Check for existing integration tests**

```bash
ls Server/tests/integration/ | grep -i console
```

**Step 2: Add basic integration test if needed**

Create or update `Server/tests/integration/test_read_console.py`:

```python
import pytest
from tests.integration.conftest import unity_connection

@pytest.mark.integration
async def test_read_console_get(unity_connection):
    """Test basic console read."""
    result = await unity_connection.send_command("read_console", {
        "action": "get",
        "types": ["error", "warning", "log"],
        "count": 10
    })
    assert result.get("success") is True
    assert "entries" in result.get("data", {}) or isinstance(result.get("data"), list)

@pytest.mark.integration
async def test_read_console_has_timestamps(unity_connection):
    """Test that entries have timestamps."""
    result = await unity_connection.send_command("read_console", {
        "action": "get",
        "types": ["log"],
        "count": 5
    })
    assert result.get("success") is True
    data = result.get("data", {})
    entries = data.get("entries", [])
    if entries:
        assert "timestamp" in entries[0]
        assert "sequenceId" in entries[0]

@pytest.mark.integration
async def test_read_console_since_sequence_id(unity_connection):
    """Test filtering by sequence ID."""
    # First call to get latest ID
    result1 = await unity_connection.send_command("read_console", {
        "action": "get",
        "types": ["log"],
        "count": 1
    })
    latest_id = result1.get("data", {}).get("latestSequenceId", 0)

    # Second call with since filter
    result2 = await unity_connection.send_command("read_console", {
        "action": "get",
        "types": ["log"],
        "sinceSequenceId": latest_id
    })
    assert result2.get("success") is True
    # Should return 0 or few entries (only new ones since first call)
    entries = result2.get("data", {}).get("entries", [])
    assert isinstance(entries, list)

@pytest.mark.integration
async def test_read_console_count_only(unity_connection):
    """Test count only mode."""
    result = await unity_connection.send_command("read_console", {
        "action": "get",
        "countOnly": True
    })
    assert result.get("success") is True
    data = result.get("data", {})
    assert "total" in data
    assert "error" in data
    assert "warning" in data
    assert "log" in data
```

**Step 3: Run integration tests**

```bash
cd Server && uv run pytest tests/integration/test_read_console.py -v --tb=short
```

Note: Requires Unity running with MCP plugin.

**Step 4: Commit**

```bash
git add Server/tests/integration/test_read_console.py
git commit -m "test(logs): add integration tests for read_console timestamps and sequence IDs"
```

---

## Task 6: Final Cleanup and Documentation

**Files:**
- Remove unused code from old ReadConsole.cs (already done in Task 3)
- Update any documentation if exists

**Step 1: Verify no orphaned reflection code remains**

Search for old reflection patterns:

```bash
grep -r "LogEntries" MCPForUnity/Editor/Tools/ReadConsole.cs
grep -r "GetEntryInternal" MCPForUnity/Editor/Tools/ReadConsole.cs
```

Expected: Only the Clear method should reference LogEntries (for clearing Unity's console).

**Step 2: Run all Unity tests**

Open Unity Test Runner, run all tests. Expected: All pass.

**Step 3: Run all Python tests**

```bash
cd Server && uv run pytest tests/ -v --tb=short
```

Expected: All pass.

**Step 4: Final commit**

```bash
git add -A
git commit -m "chore(logs): cleanup and verify log buffer rewrite complete"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | LogBuffer data structure | LogBuffer.cs, LogBufferTests.cs |
| 2 | LogCaptureService hook | LogCaptureService.cs, LogCaptureServiceTests.cs |
| 3 | Rewrite ReadConsole.cs | ReadConsole.cs, ReadConsoleTests.cs |
| 4 | Update Python tool | read_console.py |
| 5 | Integration tests | test_read_console.py |
| 6 | Cleanup & verify | All files |

**Key improvements:**
- O(n) ring buffer scan instead of slow reflection API
- Timestamps on all entries (DateTime.UtcNow)
- Sequence IDs for efficient "since last check" queries
- 10k message capacity
- Auto-clear on domain reload
