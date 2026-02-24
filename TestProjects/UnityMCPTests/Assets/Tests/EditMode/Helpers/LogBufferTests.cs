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

            var matches = _buffer.Query(filterRegex: "(?i)hello");

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
