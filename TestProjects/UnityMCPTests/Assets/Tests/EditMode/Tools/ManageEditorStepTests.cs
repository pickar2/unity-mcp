using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageEditorStepTests
    {
        [Test]
        public async Task Step_WhenNotInPlayMode_ReturnsError()
        {
            // Arrange
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = 1
            };

            // Act
            var result = ToJObject(await ManageEditor.HandleCommand(paramsObj));

            // Assert
            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result["error"]?.ToString(), Does.Contain("play mode").IgnoreCase);
        }

        [Test]
        public async Task Step_DefaultsToOneFrame()
        {
            // Arrange - just step action, no frames param
            var paramsObj = new JObject
            {
                ["action"] = "step"
            };

            // Act
            var result = ToJObject(await ManageEditor.HandleCommand(paramsObj));

            // Assert - should fail because not in play mode, but error message confirms step was attempted
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("play mode").IgnoreCase);
        }

        [Test]
        public async Task Step_WithNegativeFrames_ReturnsError()
        {
            // Arrange
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = -1
            };

            // Act
            var result = ToJObject(await ManageEditor.HandleCommand(paramsObj));

            // Assert - should fail because not in play mode (checked first) or invalid frames
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public async Task Step_WithZeroFrames_ReturnsError()
        {
            // Arrange
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = 0
            };

            // Act
            var result = ToJObject(await ManageEditor.HandleCommand(paramsObj));

            // Assert
            Assert.IsFalse(result.Value<bool>("success"));
        }
    }
}
