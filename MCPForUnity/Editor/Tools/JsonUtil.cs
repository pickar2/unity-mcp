using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    internal static class JsonUtil
    {
        /// <summary>
        /// If @params[paramName] is a JSON string, parse it to a JObject in-place.
        /// Logs a warning on parse failure and leaves the original value.
        /// </summary>
        internal static void CoerceJsonStringParameter(JObject @params, string paramName)
        {
            if (@params == null || string.IsNullOrEmpty(paramName)) return;
            var token = @params[paramName];
            if (token != null && token.Type == JTokenType.String)
            {
                try
                {
                    var parsed = JObject.Parse(token.ToString());
                    @params[paramName] = parsed;
                }
                catch (Newtonsoft.Json.JsonReaderException e)
                {
                    McpLog.Warn($"[MCP] Could not parse '{paramName}' JSON string: {e.Message}");
                }
            }
        }

        /// <summary>
        /// If @params[paramName] is a stringified JSON token (object or array), parse it in-place.
        /// Unlike CoerceJsonStringParameter which only handles objects, this handles arrays too.
        /// Use for parameters that may be either a JArray or JObject (e.g., "components", "properties").
        /// </summary>
        internal static void CoerceJsonStringToken(JObject @params, string paramName)
        {
            if (@params == null || string.IsNullOrEmpty(paramName)) return;
            var token = @params[paramName];
            if (token != null && token.Type == JTokenType.String)
            {
                try
                {
                    var parsed = JToken.Parse(token.ToString());
                    @params[paramName] = parsed;
                }
                catch (Newtonsoft.Json.JsonReaderException e)
                {
                    McpLog.Warn($"[MCP] Could not parse '{paramName}' JSON string: {e.Message}");
                }
            }
        }
    }
}
