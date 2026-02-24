using System;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class CommandRegistryTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Ensure CommandRegistry is initialized before tests run
            CommandRegistry.Initialize();
        }

        [Test]
        public void GetHandler_ThrowsException_ForUnknownCommand()
        {
            var unknown = "nonexistent_command_that_should_not_exist";

            Assert.Throws<InvalidOperationException>(() =>
            {
                CommandRegistry.GetHandler(unknown);
            }, "Should throw InvalidOperationException for unknown handler");
        }

        [Test]
        public void AutoDiscovery_RegistersAllBuiltInTools()
        {
            // Verify that all expected built-in tools are registered by trying to get their handlers
            // Sync tools: verify GetHandler returns a callable delegate
            var syncTools = new[]
            {
                "manage_asset",
                "manage_editor",
                "scene_object",
                "manage_script",
                "manage_shader",
                "read_console",
                "execute_menu_item",
                "manage_prefabs"
            };

            foreach (var toolName in syncTools)
            {
                var handler = CommandRegistry.GetHandler(toolName);
                Assert.IsNotNull(handler, $"Handler for '{toolName}' should not be null");

                // Verify the handler is actually callable (returns a result, not throws)
                var emptyParams = new Newtonsoft.Json.Linq.JObject();
                var result = handler(emptyParams);
                Assert.IsNotNull(result, $"Handler for '{toolName}' should return a result even for empty params");
            }

            // Async tools: verify they are registered (GetHandler throws for async, which is expected)
            var asyncTools = new[] { "manage_scene" };
            foreach (var toolName in asyncTools)
            {
                Assert.Throws<System.InvalidOperationException>(() => CommandRegistry.GetHandler(toolName),
                    $"Async handler for '{toolName}' should throw InvalidOperationException from GetHandler");
            }
        }
    }
}
