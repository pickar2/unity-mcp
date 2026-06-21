using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Tools;

namespace MCPForUnity.Tests.EditMode.Tools
{
    [TestFixture]
    public class ManageEditorPlayTests
    {
        private Action _originalRefresh;

        [SetUp]
        public void SetUp()
        {
            // Substitute a no-op for the real AssetDatabase.Refresh so the callback
            // queued on EditorApplication.delayCall does not fire a real refresh
            // mid-suite (which triggers a domain reload and kills the Test Runner).
            _originalRefresh = ManageEditor.RecompileRefreshAction;
            ManageEditor.RecompileRefreshAction = () => { };
        }

        [TearDown]
        public void TearDown()
        {
            ManageEditor.RecompileRefreshAction = _originalRefresh;
            // Belt-and-suspenders: clear any queued delayCall callbacks so the next
            // editor frame cannot trigger a refresh even if a future code path
            // bypasses the seam.
            EditorApplication.delayCall = null;
        }

        private static JObject ToJObject(object result)
        {
            if (result == null) return new JObject();
            return result as JObject ?? JObject.FromObject(result);
        }

        /// <summary>
        /// In EditMode, EditorApplication.isPlaying is false, so a recompile=true
        /// play request must NOT hit the play-mode guard. It should return a
        /// deferred "pending=recompile" response so the Python side can wait for
        /// the domain reload and verify compilation before entering play mode.
        /// </summary>
        [Test]
        public void Play_WithRecompile_WhenNotPlaying_ReturnsPendingRecompile()
        {
            var p = new JObject
            {
                ["action"] = "play",
                ["recompile"] = true,
            };

            var result = ManageEditor.HandleCommand(p);
            var r = ToJObject(result);

            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            var data = r["data"] as JObject;
            Assert.IsNotNull(data, "Response should include data.");
            Assert.AreEqual("recompile", data.Value<string>("pending"));
            Assert.IsTrue(data.Value<bool>("enterPlayMode"), "enterPlayMode should be true when not already playing.");
        }

        /// <summary>
        /// The paused flag must be echoed back in the pending recompile response
        /// so the Python side can forward it to the follow-up play call.
        /// </summary>
        [Test]
        public void Play_WithRecompileAndPaused_ForwardsPausedInPendingResponse()
        {
            var p = new JObject
            {
                ["action"] = "play",
                ["recompile"] = true,
                ["paused"] = true,
            };

            var result = ManageEditor.HandleCommand(p);
            var r = ToJObject(result);

            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            var data = r["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.IsTrue(data.Value<bool>("paused"), "paused should be forwarded in pending response.");
        }

        /// <summary>
        /// Recompile accepts both native bool and string representations
        /// ("true"/"false") via ToolParams.GetBool coercion.
        /// </summary>
        [Test]
        public void Play_WithRecompileAsString_True_ReturnsPendingRecompile()
        {
            var p = new JObject
            {
                ["action"] = "play",
                ["recompile"] = "true",
            };

            var result = ManageEditor.HandleCommand(p);
            var r = ToJObject(result);

            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
            var data = r["data"] as JObject;
            Assert.AreEqual("recompile", data?.Value<string>("pending"));
        }
    }
}
