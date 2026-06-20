using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Reads the optional per-project MCP configuration that overrides the
    /// global EditorPref for the local HTTP base URL. The file lives at
    /// <c>&lt;projectRoot&gt;/ProjectSettings/mcp-for-unity.json</c>.
    ///
    /// When the file is absent, unreadable, or malformed, no override is
    /// applied and callers fall back to the global EditorPref, preserving the
    /// pre-change single-project behavior.
    /// </summary>
    internal static class ProjectMcpConfig
    {
        private const string ConfigFileName = "mcp-for-unity.json";

        /// <summary>
        /// Absolute path to the per-project config file
        /// (<c>&lt;projectRoot&gt;/ProjectSettings/mcp-for-unity.json</c>).
        /// </summary>
        public static string GetConfigFilePath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, "ProjectSettings", ConfigFileName);
        }

        /// <summary>
        /// Tries to read the local HTTP base URL override from the per-project
        /// config file. Returns <c>false</c> (no override) when the file is
        /// missing, unreadable, malformed, or lacks a usable <c>httpUrl</c>.
        /// </summary>
        public static bool TryGetLocalBaseUrl(out string httpUrl)
        {
            return TryReadLocalBaseUrl(GetConfigFilePath(), out httpUrl);
        }

        /// <summary>
        /// Path-injected reader backing <see cref="TryGetLocalBaseUrl"/> and tests.
        /// </summary>
        internal static bool TryReadLocalBaseUrl(string configFilePath, out string httpUrl)
        {
            httpUrl = null;
            if (string.IsNullOrEmpty(configFilePath) || !File.Exists(configFilePath))
            {
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(configFilePath);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Project MCP config '{configFilePath}' could not be read and will be ignored: {ex.Message}");
                return false;
            }

            ProjectMcpConfigModel model;
            try
            {
                model = JsonConvert.DeserializeObject<ProjectMcpConfigModel>(json);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Project MCP config '{configFilePath}' is invalid JSON and will be ignored: {ex.Message}");
                return false;
            }

            string raw = model?.HttpUrl;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string candidate = raw.Trim();
            if (!candidate.Contains("://"))
            {
                // Mirror the HTTP Local scheme defaulting so a bare host:port is accepted.
                candidate = $"http://{candidate}";
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out _))
            {
                McpLog.Warn($"Project MCP config '{configFilePath}' has an invalid httpUrl '{raw}'; ignoring override.");
                return false;
            }

            httpUrl = raw.Trim();
            return true;
        }

        [Serializable]
        private class ProjectMcpConfigModel
        {
            [JsonProperty("httpUrl")]
            public string HttpUrl;
        }
    }
}
