using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Inspects ComputeBuffer contents via reflection-based discovery.
    /// Target syntax: "GameObject/Component.fieldName" or "instanceId:12345/Component.fieldName" or "*/Component.*" for discovery
    /// Format syntax: "name:type@offset,..." e.g. "position:float3@0,velocity:float3@16"
    /// </summary>
    [McpForUnityTool("inspect_buffer", AutoRegister = false)]
    public static class InspectBuffer
    {
        public class FieldSpec
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int Offset { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            string target = targetResult.Value;
            bool listOnly = p.GetBool("listOnly", false) || p.GetBool("list_only", false);
            int start = p.GetInt("start") ?? 0;
            int count = p.GetInt("count") ?? 8;
            string format = p.Get("format");

            try
            {
                // Discovery mode
                if (listOnly || target.Contains("*"))
                {
                    return DiscoverBuffers(target);
                }

                // Inspection mode
                return InspectBufferData(target, start, count, format);
            }
            catch (Exception e)
            {
                McpLog.Error($"[InspectBuffer] Error: {e}");
                return new ErrorResponse($"Error inspecting buffer: {e.Message}");
            }
        }

        private static object DiscoverBuffers(string pattern)
        {
            var matches = new List<object>();

            // Parse pattern: "*/Component.*" or "GameObjectName/Component.*"
            var parts = pattern.Split('/');
            string goPattern = parts.Length > 1 ? parts[0] : "*";
            string componentField = parts.Length > 1 ? parts[1] : parts[0];

            var cfParts = componentField.Split('.');
            string componentPattern = cfParts[0];
            string fieldPattern = cfParts.Length > 1 ? cfParts[1] : "*";

            // Find all GameObjects
            GameObject[] gameObjects;
            if (goPattern == "*")
            {
                gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            }
            else
            {
                gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                    .Where(go => MatchesPattern(go.name, goPattern))
                    .ToArray();
            }

            foreach (var go in gameObjects)
            {
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var compType = component.GetType();

                    if (!MatchesPattern(compType.Name, componentPattern))
                        continue;

                    var fields = compType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var field in fields)
                    {
                        if (field.FieldType != typeof(ComputeBuffer) && field.FieldType != typeof(GraphicsBuffer))
                            continue;

                        if (!MatchesPattern(field.Name, fieldPattern))
                            continue;

                        var buffer = field.GetValue(component);
                        if (buffer == null) continue;

                        int bufferCount = 0;
                        int stride = 0;

                        if (buffer is ComputeBuffer cb)
                        {
                            bufferCount = cb.count;
                            stride = cb.stride;
                        }
                        else if (buffer is GraphicsBuffer gb)
                        {
                            bufferCount = gb.count;
                            stride = gb.stride;
                        }

                        matches.Add(new
                        {
                            path = $"{go.name}/{compType.Name}.{field.Name}",
                            instanceId = go.GetInstanceID(),
                            count = bufferCount,
                            stride = stride
                        });
                    }
                }
            }

            return new SuccessResponse($"Found {matches.Count} buffer(s).", new { matches });
        }

        private static object InspectBufferData(string target, int start, int count, string format)
        {
            // Parse target: "GameObject/Component.field" or "instanceId:12345/Component.field"
            var (buffer, stride, bufferCount, path) = ResolveBuffer(target);

            if (buffer == null)
                return new ErrorResponse($"Buffer not found: {target}");

            if (start < 0 || start >= bufferCount)
                return new ErrorResponse($"start ({start}) out of range [0, {bufferCount - 1}]");

            int actualCount = Math.Min(count, bufferCount - start);

            // Read raw bytes
            byte[] rawData = new byte[actualCount * stride];

            if (buffer is ComputeBuffer cb)
                cb.GetData(rawData, 0, start * stride, actualCount * stride);
            else if (buffer is GraphicsBuffer gb)
                gb.GetData(rawData, 0, start * stride, actualCount * stride);

            // Parse format and decode
            object elements;
            if (!string.IsNullOrEmpty(format))
            {
                var fields = ParseFormatString(format);
                if (fields == null)
                    return new ErrorResponse($"Invalid format string: {format}. Expected: 'name:type@offset,...'");

                elements = DecodeElements(rawData, stride, actualCount, fields);
            }
            else
            {
                // Return raw bytes as base64 per element
                elements = DecodeRawElements(rawData, stride, actualCount);
            }

            return new SuccessResponse($"Read {actualCount} elements from {path}.", new
            {
                path,
                start,
                count = actualCount,
                stride,
                total = bufferCount,
                elements
            });
        }

        private static (object buffer, int stride, int count, string path) ResolveBuffer(string target)
        {
            // Handle instanceId:12345/Component.field or instanceId:12345.field
            if (target.StartsWith("instanceId:"))
            {
                var rest = target.Substring("instanceId:".Length);
                var slashIdx = rest.IndexOf('/');
                var dotIdx = rest.IndexOf('.');

                int instanceId;
                string componentField;

                if (slashIdx > 0)
                {
                    instanceId = int.Parse(rest.Substring(0, slashIdx));
                    componentField = rest.Substring(slashIdx + 1);
                }
                else if (dotIdx > 0)
                {
                    instanceId = int.Parse(rest.Substring(0, dotIdx));
                    componentField = rest.Substring(dotIdx + 1);
                }
                else
                {
                    return (null, 0, 0, null);
                }

                var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (go == null)
                    return (null, 0, 0, null);

                return ResolveBufferFromGameObject(go, componentField);
            }

            // Handle GameObject/Component.field
            var parts = target.Split('/');
            if (parts.Length < 2)
            {
                // Try Component.field on all objects
                return (null, 0, 0, null);
            }

            string goName = parts[0];
            string componentField2 = parts[1];

            var gameObject = GameObject.Find(goName);
            if (gameObject == null)
            {
                // Try finding by path
                gameObject = GameObject.Find("/" + goName);
            }

            if (gameObject == null)
                return (null, 0, 0, null);

            return ResolveBufferFromGameObject(gameObject, componentField2);
        }

        private static (object buffer, int stride, int count, string path) ResolveBufferFromGameObject(GameObject go, string componentField)
        {
            var cfParts = componentField.Split('.');
            if (cfParts.Length != 2)
                return (null, 0, 0, null);

            string componentName = cfParts[0];
            string fieldName = cfParts[1];

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var compType = component.GetType();

                if (compType.Name != componentName && !compType.Name.EndsWith(componentName))
                    continue;

                // Walk inheritance chain for field
                var type = compType;
                while (type != null)
                {
                    var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && (field.FieldType == typeof(ComputeBuffer) || field.FieldType == typeof(GraphicsBuffer)))
                    {
                        var buffer = field.GetValue(component);
                        if (buffer == null)
                            return (null, 0, 0, null);

                        int bufferCount = 0, bufferStride = 0;
                        if (buffer is ComputeBuffer computeBuffer)
                        {
                            bufferCount = computeBuffer.count;
                            bufferStride = computeBuffer.stride;
                        }
                        else if (buffer is GraphicsBuffer graphicsBuffer)
                        {
                            bufferCount = graphicsBuffer.count;
                            bufferStride = graphicsBuffer.stride;
                        }

                        return (buffer, bufferStride, bufferCount, $"{go.name}/{compType.Name}.{fieldName}");
                    }
                    type = type.BaseType;
                }
            }

            return (null, 0, 0, null);
        }

        public static List<FieldSpec> ParseFormatString(string format)
        {
            // Format: "name:type@offset,name:type@offset,..."
            if (string.IsNullOrEmpty(format))
                return null;

            var result = new List<FieldSpec>();
            var regex = new Regex(@"(\w+):(\w+)@(\d+)");

            foreach (var part in format.Split(','))
            {
                var match = regex.Match(part.Trim());
                if (!match.Success)
                    return null;

                result.Add(new FieldSpec
                {
                    Name = match.Groups[1].Value,
                    Type = match.Groups[2].Value.ToLower(),
                    Offset = int.Parse(match.Groups[3].Value)
                });
            }

            return result.Count > 0 ? result : null;
        }

        private static List<object> DecodeElements(byte[] data, int stride, int count, List<FieldSpec> fields)
        {
            var elements = new List<object>();

            for (int i = 0; i < count; i++)
            {
                int baseOffset = i * stride;
                var element = new Dictionary<string, object>();

                foreach (var field in fields)
                {
                    int offset = baseOffset + field.Offset;
                    element[field.Name] = DecodeValue(data, offset, field.Type);
                }

                elements.Add(element);
            }

            return elements;
        }

        private static object DecodeValue(byte[] data, int offset, string type)
        {
            switch (type)
            {
                case "float":
                    return BitConverter.ToSingle(data, offset);
                case "float2":
                    return new[] { BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4) };
                case "float3":
                    return new[] { BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8) };
                case "float4":
                    return new[] { BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8), BitConverter.ToSingle(data, offset + 12) };
                case "int":
                    return BitConverter.ToInt32(data, offset);
                case "int2":
                    return new[] { BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4) };
                case "int3":
                    return new[] { BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4), BitConverter.ToInt32(data, offset + 8) };
                case "int4":
                    return new[] { BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4), BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 12) };
                case "uint":
                    return BitConverter.ToUInt32(data, offset);
                case "half":
                    return HalfToFloat(BitConverter.ToUInt16(data, offset));
                default:
                    return $"<unknown type: {type}>";
            }
        }

        private static float HalfToFloat(ushort half)
        {
            // IEEE 754 half-precision to single-precision
            int sign = (half >> 15) & 1;
            int exp = (half >> 10) & 0x1F;
            int mant = half & 0x3FF;

            if (exp == 0)
            {
                if (mant == 0) return sign == 0 ? 0f : -0f;
                // Denormalized
                float m = mant / 1024f;
                return (sign == 0 ? 1 : -1) * m * (float)Math.Pow(2, -14);
            }
            if (exp == 31)
            {
                return mant == 0 ? (sign == 0 ? float.PositiveInfinity : float.NegativeInfinity) : float.NaN;
            }

            float mantissa = 1f + mant / 1024f;
            return (sign == 0 ? 1 : -1) * mantissa * (float)Math.Pow(2, exp - 15);
        }

        private static List<object> DecodeRawElements(byte[] data, int stride, int count)
        {
            var elements = new List<object>();
            for (int i = 0; i < count; i++)
            {
                byte[] elementData = new byte[stride];
                Array.Copy(data, i * stride, elementData, 0, stride);
                elements.Add(new { raw = Convert.ToBase64String(elementData) });
            }
            return elements;
        }

        private static bool MatchesPattern(string value, string pattern)
        {
            if (pattern == "*") return true;
            if (pattern.Contains("*"))
            {
                var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
            }
            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
