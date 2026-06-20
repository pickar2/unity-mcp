using System.IO;
using NUnit.Framework;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Unit tests for the per-project MCP config reader. Uses a temp directory
    /// and the path-injected <see cref="ProjectMcpConfig.TryReadLocalBaseUrl"/>
    /// so the real project config location is never touched.
    /// </summary>
    [TestFixture]
    public class ProjectMcpConfigTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "McpProjectConfigTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        private string ConfigPath() => Path.Combine(_tempDir, "mcp-for-unity.json");

        private static void WriteFile(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        [Test]
        public void TryReadLocalBaseUrl_NoFile_ReturnsFalse()
        {
            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_NullPath_ReturnsFalse()
        {
            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(null, out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_EmptyPath_ReturnsFalse()
        {
            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(string.Empty, out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_ValidHttpUrl_ReturnsValue()
        {
            WriteFile(ConfigPath(), "{ \"httpUrl\": \"http://127.0.0.1:9090\" }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsTrue(result);
            Assert.AreEqual("http://127.0.0.1:9090", httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_BareHostPort_AcceptedAsValid()
        {
            WriteFile(ConfigPath(), "{ \"httpUrl\": \"127.0.0.1:9091\" }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsTrue(result);
            Assert.AreEqual("127.0.0.1:9091", httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_MissingHttpUrlField_ReturnsFalse()
        {
            WriteFile(ConfigPath(), "{ \"other\": 42 }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_EmptyHttpUrl_ReturnsFalse()
        {
            WriteFile(ConfigPath(), "{ \"httpUrl\": \"\" }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_WhitespaceHttpUrl_ReturnsFalse()
        {
            WriteFile(ConfigPath(), "{ \"httpUrl\": \"   \" }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_MalformedJson_ReturnsFalse()
        {
            WriteFile(ConfigPath(), "{ not valid json");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_InvalidSchemeUrl_ReturnsFalse()
        {
            // Schemes must start with a letter (RFC 3986); .NET Uri rejects this.
            WriteFile(ConfigPath(), "{ \"httpUrl\": \"1bad://host\" }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsFalse(result);
            Assert.IsNull(httpUrl);
        }

        [Test]
        public void TryReadLocalBaseUrl_UnrelatedFieldsIgnored_ReturnsHttpUrl()
        {
            WriteFile(ConfigPath(), "{ \"httpUrl\": \"http://127.0.0.1:9092\", \"other\": 42 }");

            bool result = ProjectMcpConfig.TryReadLocalBaseUrl(ConfigPath(), out string httpUrl);

            Assert.IsTrue(result);
            Assert.AreEqual("http://127.0.0.1:9092", httpUrl);
        }

        [Test]
        public void GetConfigFilePath_PointsAtProjectSettingsConfigFile()
        {
            string path = ProjectMcpConfig.GetConfigFilePath();

            Assert.That(path, Does.Contain("ProjectSettings"));
            Assert.That(path, Does.EndWith("mcp-for-unity.json"));
        }
    }
}
