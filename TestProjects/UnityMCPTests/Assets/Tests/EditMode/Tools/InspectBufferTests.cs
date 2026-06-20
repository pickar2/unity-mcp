using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class InspectBufferTests
    {
        [Test]
        public void HandleCommand_MissingTarget_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["start"] = 0,
                ["count"] = 8
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("target"));
        }

        [Test]
        public void HandleCommand_InvalidTargetFormat_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["target"] = "InvalidTarget",  // No component.field syntax
                ["start"] = 0,
                ["count"] = 8
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void HandleCommand_NonexistentGameObject_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["target"] = "NonexistentObject12345/SomeComponent.bufferField",
                ["start"] = 0,
                ["count"] = 8
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
            // In EditMode, buffer inspection returns play mode error; in PlayMode it would return "not found"
            Assert.That(result["error"]?.ToString(),
                Does.Contain("not found").IgnoreCase.Or.Contain("play mode").IgnoreCase);
        }

        [Test]
        public void HandleCommand_ListOnly_ReturnsDiscoveryFormat()
        {
            var paramsObj = new JObject
            {
                ["target"] = "*/SomeComponent.*",
                ["list_only"] = true
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            // Should succeed even if no buffers found - returns empty list
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.IsNotNull(result["data"]);
        }

        [Test]
        public void HandleCommand_WildcardTarget_TriggersDiscovery()
        {
            var paramsObj = new JObject
            {
                ["target"] = "*/SomeComponent.*"
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            // Should succeed - wildcard triggers discovery mode
            Assert.IsTrue(result.Value<bool>("success"));
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.IsNotNull(data["matches"]);
        }

        [Test]
        public void ParseFormat_ValidFormat_ParsesCorrectly()
        {
            // Test the format string parser
            var fields = InspectBuffer.ParseFormatString("position:float3@0,velocity:float3@16,mass:float@32,id:int@36");

            Assert.IsNotNull(fields);
            Assert.AreEqual(4, fields.Count);
            Assert.AreEqual("position", fields[0].Name);
            Assert.AreEqual("float3", fields[0].Type);
            Assert.AreEqual(0, fields[0].Offset);
            Assert.AreEqual("id", fields[3].Name);
            Assert.AreEqual("int", fields[3].Type);
            Assert.AreEqual(36, fields[3].Offset);
        }

        [Test]
        public void ParseFormat_SingleField_ParsesCorrectly()
        {
            var fields = InspectBuffer.ParseFormatString("value:float@0");

            Assert.IsNotNull(fields);
            Assert.AreEqual(1, fields.Count);
            Assert.AreEqual("value", fields[0].Name);
            Assert.AreEqual("float", fields[0].Type);
            Assert.AreEqual(0, fields[0].Offset);
        }

        [Test]
        public void ParseFormat_InvalidFormat_ReturnsNull()
        {
            var fields = InspectBuffer.ParseFormatString("invalid format string");
            Assert.IsNull(fields);
        }

        [Test]
        public void ParseFormat_EmptyString_ReturnsNull()
        {
            var fields = InspectBuffer.ParseFormatString("");
            Assert.IsNull(fields);
        }

        [Test]
        public void ParseFormat_NullString_ReturnsNull()
        {
            var fields = InspectBuffer.ParseFormatString(null);
            Assert.IsNull(fields);
        }

        [Test]
        public void ParseFormat_MissingOffset_ReturnsNull()
        {
            var fields = InspectBuffer.ParseFormatString("name:float");
            Assert.IsNull(fields);
        }

        [Test]
        public void ParseFormat_MissingType_ReturnsNull()
        {
            var fields = InspectBuffer.ParseFormatString("name@0");
            Assert.IsNull(fields);
        }
    }
}
