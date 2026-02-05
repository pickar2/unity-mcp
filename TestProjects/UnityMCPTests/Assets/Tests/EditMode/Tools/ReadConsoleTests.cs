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
