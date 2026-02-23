using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Unified property conversion from JSON to Unity types.
    /// Uses UnityJsonSerializer for consistent type handling.
    /// </summary>
    public static class PropertyConversion
    {
        /// <summary>
        /// Converts a JToken to the specified target type using Unity type converters.
        /// </summary>
        /// <param name="token">The JSON token to convert</param>
        /// <param name="targetType">The target type to convert to</param>
        /// <returns>The converted object, or null if conversion fails</returns>
        public static object ConvertToType(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    McpLog.Warn($"[PropertyConversion] Cannot assign null to non-nullable value type {targetType.Name}. Returning default value.");
                    return Activator.CreateInstance(targetType);
                }
                return null;
            }

            // For UnityEngine.Object-derived types, try loading from asset path/GUID/instruction
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                var asset = TryLoadUnityObject(token, targetType);
                if (asset != null) return asset;
                // null token explicitly means "clear reference"
                if (token.Type == JTokenType.Null) return null;
            }

            try
            {
                // Use the shared Unity serializer with custom converters
                return token.ToObject(targetType, UnityJsonSerializer.Instance);
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error converting token to {targetType.FullName}: {ex.Message}\nToken: {token.ToString(Formatting.None)}");
                throw;
            }
        }

        private static UnityEngine.Object TryLoadUnityObject(JToken token, Type targetType)
        {
            // String value: treat as asset path or GUID
            if (token.Type == JTokenType.String)
            {
                string str = token.ToString();
                if (string.IsNullOrWhiteSpace(str)) return null;

                // GUID (32 hex chars, no path separators)
                if (str.Length == 32 && !str.Contains("/") && !str.Contains("\\") && !str.Contains("."))
                {
                    string guidPath = AssetDatabase.GUIDToAssetPath(str);
                    if (!string.IsNullOrEmpty(guidPath))
                    {
                        var byGuid = AssetDatabase.LoadAssetAtPath(guidPath, targetType);
                        if (byGuid != null) return byGuid;
                    }
                }

                // Asset path
                string sanitized = AssetPathUtility.SanitizeAssetPath(str);
                if (!string.IsNullOrEmpty(sanitized))
                {
                    var byPath = AssetDatabase.LoadAssetAtPath(sanitized, targetType);
                    if (byPath != null) return byPath;
                }

                return null;
            }

            // Object with ref instruction: { "path": "...", "guid": "..." }
            if (token is JObject obj)
            {
                return ObjectResolver.Resolve(obj, targetType);
            }

            return null;
        }

        /// <summary>
        /// Tries to convert a JToken to the specified target type.
        /// Returns null and logs warning on failure (does not throw).
        /// </summary>
        public static object TryConvertToType(JToken token, Type targetType)
        {
            try
            {
                return ConvertToType(token, targetType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generic version of ConvertToType.
        /// </summary>
        public static T ConvertTo<T>(JToken token)
        {
            return (T)ConvertToType(token, typeof(T));
        }

        /// <summary>
        /// Converts a JToken to a Unity asset by loading from path.
        /// </summary>
        /// <param name="token">JToken containing asset path</param>
        /// <param name="targetType">Expected asset type</param>
        /// <returns>The loaded asset, or null if not found</returns>
        public static UnityEngine.Object LoadAssetFromToken(JToken token, Type targetType)
        {
            if (token == null || token.Type != JTokenType.String)
                return null;

            string assetPath = AssetPathUtility.SanitizeAssetPath(token.ToString());
            UnityEngine.Object loadedAsset = AssetDatabase.LoadAssetAtPath(assetPath, targetType);
            
            if (loadedAsset == null)
            {
                McpLog.Warn($"[PropertyConversion] Could not load asset of type {targetType.Name} from path: {assetPath}");
            }
            
            return loadedAsset;
        }
    }
}

