using System.Collections;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.PlayMode
{
    /// <summary>
    /// Play-mode-only coverage for ManageEditor's play-mode guard.
    /// The guard rejects `action=play, recompile=true` while the editor is in
    /// play mode (recompile would trigger a domain reload that kills the run).
    /// This state can ONLY be constructed in a PlayMode assembly — EditMode
    /// tests cannot flip EditorApplication.isPlaying without derailing the
    /// Test Runner.
    /// </summary>
    public class ManageEditorPlayModeGuardTests
    {
        /// <summary>
        /// When the editor is in play mode, a recompile=true play request must
        /// be rejected with a clear error message.
        ///
        /// Would fail if the guard at ManageEditor.cs were removed: the handler
        /// would instead return a pending-recompile success response.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayModeGuard_RecompileInPlayMode_ReturnsError()
        {
            // Sanity: this test only runs in play mode, where isPlaying is true.
            Assert.IsTrue(
                EditorApplication.isPlaying,
                "PlayMode test must run with EditorApplication.isPlaying == true."
            );

            var p = new JObject
            {
                ["action"] = "play",
                ["recompile"] = true,
            };

            object result = ManageEditor.HandleCommand(p);
            yield return null;

            // ErrorResponse serializes with success=false and an error message.
            var asErrorResponse = result as ErrorResponse;
            Assert.IsNotNull(
                asErrorResponse,
                $"Expected ErrorResponse when recompile=true in play mode, got: {result?.GetType().Name ?? "null"}"
            );
            Assert.IsFalse(
                asErrorResponse.Success,
                "Guard must return success=false for recompile during play mode."
            );
            StringAssert.Contains(
                "play mode",
                asErrorResponse.Error ?? "",
                "Error message should explain the play-mode constraint."
            );
        }

        /// <summary>
        /// Even string-form recompile ("true") must trip the play-mode guard.
        /// Covers the ToolParams.GetBool coercion path that mirrors the Python
        /// coerce_bool surface.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayModeGuard_RecompileStringTrueInPlayMode_ReturnsError()
        {
            Assert.IsTrue(EditorApplication.isPlaying);

            var p = new JObject
            {
                ["action"] = "play",
                ["recompile"] = "true",
            };

            object result = ManageEditor.HandleCommand(p);
            yield return null;

            var asErrorResponse = result as ErrorResponse;
            Assert.IsNotNull(
                asErrorResponse,
                $"Expected ErrorResponse for string 'true' recompile in play mode, got: {result?.GetType().Name ?? "null"}"
            );
            Assert.IsFalse(asErrorResponse.Success);
        }
    }
}
