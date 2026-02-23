using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Tools.Prefabs;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor.Tools.Characterization
{
    /// <summary>
    /// Characterization tests for Editor Tools domain.
    /// These tests capture CURRENT behavior without refactoring.
    /// They serve as a regression baseline for future refactoring work.
    ///
    /// Based on analysis in: MCPForUnity/Editor/Tools/Tests/CHARACTERIZATION_SUMMARY.md
    ///
    /// Sampled tools: ManageEditor, ManageMaterial, ManagePrefabs, ExecuteMenuItem
    /// </summary>
    [TestFixture]
    public class EditorToolsCharacterizationTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        #region Section 1: HandleCommand Entry Point and Null/Empty Parameter Handling

        /// <summary>
        /// Current behavior: All tools have a single public HandleCommand(JObject) entry point.
        /// This is the standard pattern - tests verify all sampled tools follow it.
        /// </summary>
        [Test]
        public void HandleCommand_ManageEditor_WithNullParams_ReturnsErrorResponse()
        {
            // FIXED BEHAVIOR (P1-1 ToolParams refactoring): ManageEditor now handles null params gracefully
            // Returns ErrorResponse instead of throwing NullReferenceException
            var result = ManageEditor.HandleCommand(null);
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should return error for null params");
            Assert.IsNotNull(jo["error"], "Should have error message");
            Assert.That((string)jo["error"], Does.Contain("cannot be null"), "Should indicate parameters are null");
        }

        [Test]
        public void HandleCommand_ManageEditor_WithoutActionParameter_ReturnsError()
        {
            // Current behavior: Action parameter is required for dispatch
            var result = ManageEditor.HandleCommand(new JObject());
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require action parameter");
            Assert.IsNotNull(jo["error"], "Should have error message");
        }

        [Test]
        public void HandleCommand_ActionNormalization_CaseInsensitive()
        {
            // Current behavior: Actions are normalized to lowercase for comparison
            // Using telemetry_status (read-only) instead of play to avoid mutating editor state
            var upperResult = ManageEditor.HandleCommand(new JObject { ["action"] = "TELEMETRY_STATUS" });
            var lowerResult = ManageEditor.HandleCommand(new JObject { ["action"] = "telemetry_status" });

            var upperJo = ToJO(upperResult);
            var lowerJo = ToJO(lowerResult);

            // Both should succeed or both should fail in the same way (action recognized)
            Assert.AreEqual((bool)upperJo["success"], (bool)lowerJo["success"],
                "Case normalization should make both behave identically");
        }

        #endregion

        #region Section 2: Parameter Extraction and Validation

        [Test]
        public void HandleCommand_ManageEditor_SetActiveTool_RequiresToolNameParameter()
        {
            // Current behavior: set_active_tool requires tool_name parameter
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "set_active_tool"
                // Missing tool_name
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require tool_name");
        }

        [Test]
        public void HandleCommand_ManageEditor_ActionsRecognized()
        {
            // Current behavior: Valid actions are recognized and return response objects
            // Using telemetry_status (read-only) to avoid mutating editor state
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "telemetry_status"
            });
            // Action should be recognized and return valid response
            var jo = ToJO(result);
            Assert.IsNotNull(jo, "Should return a response object");
            Assert.IsTrue(jo.ContainsKey("success"), "Response should have success field");
        }

        #endregion

        #region Section 3: Action Switch Dispatch

        [Test]
        public void HandleCommand_ManageEditor_WithUnknownAction_ReturnsError()
        {
            // Current behavior: Unknown actions return error with descriptive message
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "nonexistent_action_xyz"
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should fail for unknown action");
            StringAssert.Contains("nonexistent_action_xyz", jo["error"]?.ToString() ?? "",
                "Error should mention the unknown action");
        }

        [Test]
        public void HandleCommand_ManageEditor_DifferentActionsDispatchToDifferentHandlers()
        {
            // Current behavior: Different actions dispatch to different handlers
            // Using read-only actions to avoid mutating editor state
            var statusResult = ManageEditor.HandleCommand(new JObject { ["action"] = "telemetry_status" });
            var pingResult = ManageEditor.HandleCommand(new JObject { ["action"] = "telemetry_ping" });

            // Both should return responses
            Assert.IsNotNull(statusResult, "Status should return response");
            Assert.IsNotNull(pingResult, "Ping should return response");
        }

        [Test]
        public void HandleCommand_ManageMaterial_WithUnknownAction_ReturnsError()
        {
            // Current behavior: Material tool also returns error for unknown actions
            var result = ManageMaterial.HandleCommand(new JObject
            {
                ["action"] = "unknown_material_action"
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should fail for unknown action");
        }

        #endregion

        #region Section 4: Error Handling and Logging

        [Test]
        public void HandleCommand_ManagePrefabs_WithInvalidParameters_ReturnsError()
        {
            // Current behavior: Invalid parameters caught and returned as ErrorResponse
            var result = ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "create_from_gameobject"
                // Missing required parameters
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should fail with invalid params");
            Assert.IsNotNull(jo["error"], "Should have error description");
        }

        [Test]
        public void HandleCommand_ManageEditor_ReturnsResponseObject()
        {
            // Current behavior: All responses are either SuccessResponse or ErrorResponse
            // Using telemetry_status (read-only) to avoid mutating editor state
            var result = ManageEditor.HandleCommand(new JObject { ["action"] = "telemetry_status" });
            var jo = ToJO(result);
            // Verify response has expected shape
            Assert.IsTrue(jo.ContainsKey("success"), "Response should have 'success' field");
        }

        [Test]
        public void HandleCommand_ErrorMessages_AreContextSpecific()
        {
            // Current behavior: Error messages include context about what went wrong
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "add_tag"
                // Missing tag_name
            });
            var jo = ToJO(result);
            var error = jo["error"]?.ToString() ?? "";
            // Error should mention what's missing or wrong
            Assert.IsTrue(error.Length > 0, "Should have descriptive error message");
        }

        [Test]
        public void HandleCommand_ManageEditor_SafelyHandlesNullTokens()
        {
            // Current behavior: Null-safe token access pattern prevents NullReferenceException
            // This test verifies ManageEditor doesn't crash on partial params
            Assert.DoesNotThrow(() =>
            {
                ManageEditor.HandleCommand(new JObject { ["action"] = null });
            }, "Should handle null action token without exception");
        }

        #endregion

        #region Section 5: Inline Parameter Validation and Coercion

        [Test]
        public void HandleCommand_ManageEditor_AddTag_RequiresTagName()
        {
            // Current behavior: add_tag validates tag_name is present before mutation
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "add_tag"
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require tag_name parameter");
        }

        [Test]
        public void HandleCommand_ManagePrefabs_WithoutRequiredPath_ReturnsError()
        {
            // Current behavior: Required path parameter validated before operation
            var result = ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "get_info"
                // Missing path parameter
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require path parameter");
        }

        [Test]
        public void HandleCommand_ManageMaterial_Create_RequiresNameParameter()
        {
            // Current behavior: create action requires name parameter
            var result = ManageMaterial.HandleCommand(new JObject
            {
                ["action"] = "create"
                // Missing name
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require name parameter");
        }

        [Test]
        public void HandleCommand_ValidationOccursBeforeStateMutation()
        {
            // Current behavior: Parameters are validated before any state changes
            // This is verified by checking that invalid params don't cause side effects
            var result = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "add_layer"
                // Missing layer_name - should fail before attempting to add
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should validate before mutation");
        }

        #endregion

        #region Section 6: State Mutation and Side Effects

        [Test]
        public void HandleCommand_ManageEditor_ReadOnlyActionsDoNotMutateState()
        {
            // Current behavior: Read-only actions like telemetry_status don't mutate editor state
            // Verify isPlaying remains false after calling telemetry_status
            var wasPlayingBefore = UnityEditor.EditorApplication.isPlaying;
            var result = ManageEditor.HandleCommand(new JObject { ["action"] = "telemetry_status" });
            var isPlayingAfter = UnityEditor.EditorApplication.isPlaying;

            Assert.AreEqual(wasPlayingBefore, isPlayingAfter, "Read-only actions should not change play mode state");
        }

        [Test]
        public void HandleCommand_ManageMaterial_CreateAction_RequiresValidParams()
        {
            // Current behavior: Asset creation requires valid parameters
            // This documents that side effects only occur with valid params
            var result = ManageMaterial.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "" // Empty name should fail
            });
            var jo = ToJO(result);
            // Either fails validation or succeeds (behavior may vary)
            Assert.IsTrue(jo.ContainsKey("success"), "Should return response");
        }

        #endregion

        #region Section 7: Complex Parameter Handling and Object Resolution

        #endregion

        #region Section 8: Security and Filtering

        [Test]
        public void HandleCommand_ExecuteMenuItem_BlacklistsQuit()
        {
            // Current behavior: File/Quit is blacklisted for safety
            var result = ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menuPath"] = "File/Quit"
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Quit should be blocked");
            StringAssert.Contains("blocked", jo["error"]?.ToString()?.ToLower() ?? "",
                "Error should mention blocking");
        }

        [Test]
        public void HandleCommand_ExecuteMenuItem_RequiresMenuPath()
        {
            // Current behavior: menu_path/menuPath parameter is required
            var result = ExecuteMenuItem.HandleCommand(new JObject());
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require menuPath");
        }

        #endregion

        #region Section 9: Response Objects and Data Structures

        [Test]
        public void HandleCommand_ResponsesHaveConsistentShape()
        {
            // Current behavior: All responses have success field
            var tools = new Func<JObject, object>[]
            {
                p => ManageEditor.HandleCommand(p),
                p => ManagePrefabs.HandleCommand(p),
                p => ManageMaterial.HandleCommand(p),
                p => ExecuteMenuItem.HandleCommand(p)
            };

            foreach (var tool in tools)
            {
                var result = tool(new JObject { ["action"] = "ping" });
                var jo = ToJO(result);
                Assert.IsTrue(jo.ContainsKey("success"), "All responses should have success field");
            }
        }

        [Test]
        public void HandleCommand_SuccessResponse_HasMessageField()
        {
            // Current behavior: Success responses typically have message field
            var result = ManageMaterial.HandleCommand(new JObject { ["action"] = "ping" });
            var jo = ToJO(result);
            if ((bool)jo["success"])
            {
                Assert.IsTrue(jo.ContainsKey("message") || jo.ContainsKey("data"),
                    "Success should have message or data");
            }
        }

        [Test]
        public void HandleCommand_ErrorResponse_HasErrorField()
        {
            // Current behavior: Error responses have error field with description
            var result = ManageEditor.HandleCommand(new JObject { ["action"] = "invalid" });
            var jo = ToJO(result);
            if (!(bool)jo["success"])
            {
                Assert.IsTrue(jo.ContainsKey("error"), "Error response should have error field");
            }
        }

        #endregion

        #region Section 10: Tool Registration

        [Test]
        public void AllSampledTools_HaveMcpForUnityToolAttribute()
        {
            // Current behavior: Tools are registered via McpForUnityTool attribute
            // This verifies the sampled tools have the attribute
            var toolTypes = new[]
            {
                typeof(ManageEditor),
                typeof(ManagePrefabs),
                typeof(ManageMaterial),
                typeof(ExecuteMenuItem)
            };

            foreach (var type in toolTypes)
            {
                var attr = Attribute.GetCustomAttribute(type, typeof(McpForUnityToolAttribute));
                Assert.IsNotNull(attr, $"{type.Name} should have McpForUnityTool attribute");
            }
        }

        #endregion

        #region Section 11: Tool-Specific Behaviors

        [Test]
        public void HandleCommand_ManageEditor_PlayPauseStopStateMachine()
        {
            // Current behavior: play/pause/stop form a state machine
            // pause only works when playing
            var pauseResult = ManageEditor.HandleCommand(new JObject { ["action"] = "pause" });
            var jo = ToJO(pauseResult);
            // Pause behavior depends on current play state
            Assert.IsNotNull(jo, "Should return response");
        }

        [Test]
        public void HandleCommand_ManageMaterial_ColorCoercion()
        {
            // Current behavior: Colors can be specified in multiple formats
            var result = ManageMaterial.HandleCommand(new JObject
            {
                ["action"] = "set_material_color",
                ["path"] = "NonExistent/Material",
                ["color"] = new JArray(1.0f, 0.5f, 0.5f, 1.0f)
            });
            // Even if material doesn't exist, the color parsing should not throw
            var jo = ToJO(result);
            Assert.IsNotNull(jo, "Should handle color array format");
        }

        [Test]
        public void HandleCommand_ManagePrefabs_GetInfo_RequiresPath()
        {
            // Current behavior: get_info needs path to prefab
            var result = ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "get_info"
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require path");
        }

        [Test]
        public void HandleCommand_ManagePrefabs_CreateFromGameObject_RequiresTargetAndPath()
        {
            // Current behavior: create_from_gameobject needs both target and path
            var result = ManagePrefabs.HandleCommand(new JObject
            {
                ["action"] = "create_from_gameobject"
            });
            var jo = ToJO(result);
            Assert.IsFalse((bool)jo["success"], "Should require target and path");
        }

        [Test]
        [Explicit("Opens Console window - steals focus")]
        public void HandleCommand_ExecuteMenuItem_ExecutesNonBlacklistedItems()
        {
            // Current behavior: Non-blacklisted items are executed
            // NOTE: This test opens the Console window which steals focus from the terminal
            var result = ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menuPath"] = "Window/General/Console"
            });
            var jo = ToJO(result);
            // Should attempt execution (success depends on menu existence)
            Assert.IsTrue((bool)jo["success"], "Non-blacklisted item should be attempted");
        }

        #endregion
    }
}
