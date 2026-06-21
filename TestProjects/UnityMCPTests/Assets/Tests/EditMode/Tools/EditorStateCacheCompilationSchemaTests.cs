using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Services;

namespace MCPForUnity.Tests.EditMode.Tools
{
    /// <summary>
    /// Schema coverage for EditorStateCache's compilation section.
    ///
    /// The `script_compilation_failed` field is the signal the Python side
    /// (manage_editor._check_compilation_failed) reads to detect a failed
    /// recompile after the deferred-refresh handshake. This test would fail
    /// if the field disappeared from the DTO or the snapshot builder stopped
    /// emitting it — without it, the Python side silently treats every
    /// failed recompile as a success.
    /// </summary>
    [TestFixture]
    public class EditorStateCacheCompilationSchemaTests
    {
        [Test]
        public void Snapshot_EmitsScriptCompilationFailedField()
        {
            var snapshot = EditorStateCache.GetSnapshot();

            Assert.IsNotNull(snapshot, "Snapshot must not be null.");
            var compilation = snapshot["compilation"] as JObject;
            Assert.IsNotNull(
                compilation,
                "Snapshot must include a compilation section."
            );

            // The field is emitted as a nullable bool. The actual value depends on
            // the editor's last compile (typically false in a clean test project);
            // we only assert the field is PRESENT so the schema contract holds.
            Assert.IsTrue(
                compilation.TryGetValue("script_compilation_failed", out _),
                "compilation.script_compilation_failed must be emitted by EditorStateCache."
            );
        }
    }
}
