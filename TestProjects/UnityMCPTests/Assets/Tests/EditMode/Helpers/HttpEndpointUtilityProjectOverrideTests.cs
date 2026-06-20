using System.IO;
using NUnit.Framework;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Integration tests for the <see cref="HttpEndpointUtility.GetLocalBaseUrl"/>
    /// resolution chain: project-config override, then global EditorPref, then
    /// the hardcoded default. The override path writes to the real per-project
    /// config location (<c>ProjectSettings/mcp-for-unity.json</c>) and removes
    /// it in teardown; the global EditorPref is saved and restored around each
    /// test.
    /// </summary>
    [TestFixture]
    public class HttpEndpointUtilityProjectOverrideTests
    {
        private string _savedHttpUrl;
        private bool _httpUrlKeyExisted;
        private string _configPath;
        private bool _createdConfig;

        [SetUp]
        public void SetUp()
        {
            _httpUrlKeyExisted = EditorPrefs.HasKey(EditorPrefKeys.HttpBaseUrl);
            _savedHttpUrl = EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl, string.Empty);
            _configPath = ProjectMcpConfig.GetConfigFilePath();
            // Fail loud rather than clobber an unexpected real override file.
            Assert.IsFalse(File.Exists(_configPath),
                $"Pre-existing project MCP config found at {_configPath}; aborting to avoid clobbering it.");
            _createdConfig = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_createdConfig && File.Exists(_configPath))
            {
                try { File.Delete(_configPath); } catch { }
            }

            if (_httpUrlKeyExisted)
            {
                EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, _savedHttpUrl);
            }
            else
            {
                EditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);
            }
        }

        private void WriteConfig(string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, contents);
            _createdConfig = true;
        }

        [Test]
        public void GetLocalBaseUrl_NoConfigNoPref_ReturnsHardcodedDefault()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);

            string result = HttpEndpointUtility.GetLocalBaseUrl();

            Assert.AreEqual("http://127.0.0.1:8080", result);
        }

        [Test]
        public void GetLocalBaseUrl_NoConfigUsesPref_ReturnsPrefValue()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8888");

            string result = HttpEndpointUtility.GetLocalBaseUrl();

            Assert.AreEqual("http://127.0.0.1:8888", result);
        }

        [Test]
        public void GetLocalBaseUrl_ConfigOverrideWinsOverPref()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8888");
            WriteConfig("{ \"httpUrl\": \"http://127.0.0.1:9999\" }");

            string result = HttpEndpointUtility.GetLocalBaseUrl();

            Assert.AreEqual("http://127.0.0.1:9999", result);
        }

        [Test]
        public void GetLocalBaseUrl_ConfigOverrideWinsOverDefault()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);
            WriteConfig("{ \"httpUrl\": \"http://127.0.0.1:9999\" }");

            string result = HttpEndpointUtility.GetLocalBaseUrl();

            Assert.AreEqual("http://127.0.0.1:9999", result);
        }

        [Test]
        public void GetLocalBaseUrl_MalformedConfigFallsBackToPref()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:8888");
            WriteConfig("{ not valid json");

            string result = HttpEndpointUtility.GetLocalBaseUrl();

            Assert.AreEqual("http://127.0.0.1:8888", result);
        }

        [Test]
        public void GetLocalBaseUrl_ConfigBareHostPort_NormalizedToLoopback()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.HttpBaseUrl);
            WriteConfig("{ \"httpUrl\": \"localhost:9090\" }");

            string result = HttpEndpointUtility.GetLocalBaseUrl();

            // NormalizeBaseUrl forces 127.0.0.1 over localhost for local scope.
            Assert.AreEqual("http://127.0.0.1:9090", result);
        }
    }
}
