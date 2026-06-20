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
