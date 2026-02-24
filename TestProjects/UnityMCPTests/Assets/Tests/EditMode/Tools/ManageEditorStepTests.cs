using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageEditorStepTests
    {
        [Test]
        public void Step_WhenNotInPlayMode_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = 1
            };

            var result = ToJObject(ManageEditor.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result["error"]?.ToString(), Does.Contain("play mode").IgnoreCase);
        }

        [Test]
        public void Step_DefaultsToOneFrame()
        {
            var paramsObj = new JObject
            {
                ["action"] = "step"
            };

            var result = ToJObject(ManageEditor.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("play mode").IgnoreCase);
        }

        [Test]
        public void Step_WithNegativeFrames_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = -1
            };

            var result = ToJObject(ManageEditor.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void Step_WithZeroFrames_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = 0
            };

            var result = ToJObject(ManageEditor.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
        }
    }
}
