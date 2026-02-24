using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.GameObjects;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("inspect_component", AutoRegister = false)]
    public static class InspectComponent
    {
        private const string LogTag = "[InspectComponent]";

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            string action = p.Get("action", "list");

            switch (action)
            {
                case "list": return ListComponentTypes(p);
                case "schema": return GetComponentSchema(p);
                default:
                    return new ErrorResponse($"Unknown action '{action}'. Use 'list' or 'schema'.");
            }
        }

        private static object ListComponentTypes(ToolParams p)
        {
            string search = p.Get("search");
            string category = p.Get("category");
            int pageSize = p.GetInt("pageSize") ?? 50;
            int page = p.GetInt("page") ?? 0;

            var allTypes = TypeCache.GetTypesDerivedFrom<Component>();
            var filtered = new List<object>();

            foreach (var type in allTypes)
            {
                if (type.IsAbstract || type.IsGenericType) continue;
                if (type.GetCustomAttribute<ObsoleteAttribute>() != null) continue;

                string typeName = type.Name;
                string fullName = type.FullName ?? typeName;
                string ns = type.Namespace ?? "";
                string assemblyName = type.Assembly.GetName().Name;

                // Skip Unity internal/editor types that aren't useful for components
                if (ns.Contains(".Internal") || ns.Contains(".Experimental")) continue;

                // Search filter
                if (!string.IsNullOrEmpty(search))
                {
                    bool match = typeName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                              || fullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) continue;
                }

                // Category filter (derived from namespace and type hierarchy)
                string derivedCategory = DeriveCategory(ns, type);
                if (!string.IsNullOrEmpty(category))
                {
                    if (derivedCategory.IndexOf(category, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                // Check for AddComponentMenu attribute
                string menuPath = null;
                var menuAttr = type.GetCustomAttribute<AddComponentMenu>();
                if (menuAttr != null && !string.IsNullOrEmpty(menuAttr.componentMenu))
                    menuPath = menuAttr.componentMenu;

                filtered.Add(new Dictionary<string, object>
                {
                    ["typeName"] = typeName,
                    ["fullName"] = fullName,
                    ["category"] = derivedCategory,
                    ["assembly"] = assemblyName,
                    ["menuPath"] = menuPath
                });
            }

            // Sort by typeName
            filtered.Sort((a, b) =>
                string.Compare(
                    ((Dictionary<string, object>)a)["typeName"] as string,
                    ((Dictionary<string, object>)b)["typeName"] as string,
                    StringComparison.OrdinalIgnoreCase));

            int total = filtered.Count;
            var paged = filtered.Skip(page * pageSize).Take(pageSize).ToList();

            var data = new Dictionary<string, object>
            {
                ["total"] = total,
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["types"] = paged
            };

            if ((page + 1) * pageSize < total)
                data["nextPage"] = page + 1;

            return new SuccessResponse($"Found {total} component types.", data);
        }

        private static object GetComponentSchema(ToolParams p)
        {
            string typeName = p.Get("typeName");
            if (string.IsNullOrEmpty(typeName))
                return new ErrorResponse("Required parameter 'type_name' is missing.");

            bool includeEnums = p.GetBool("includeEnumValues", true);
            bool includeDefaults = p.GetBool("includeDefaults", true);

            if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string resolveError))
                return new ErrorResponse($"Component type '{typeName}' not found: {resolveError}");

            // Create temp GameObject to get defaults and SerializedProperty info
            var tempGo = new GameObject("__InspectComponentTemp__");
            tempGo.hideFlags = HideFlags.HideAndDontSave;
            Component instance = null;

            try
            {
                // Some components need special handling (can't AddComponent Camera if one exists, etc.)
                instance = tempGo.AddComponent(componentType);
                if (instance == null)
                    return new ErrorResponse($"Could not add component '{typeName}' to temp GameObject. It may require specific conditions.");

                var so = new SerializedObject(instance);
                var properties = new List<object>();

                var iter = so.GetIterator();
                bool enterChildren = true;
                while (iter.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    // Skip the m_Script field (internal Unity reference)
                    if (iter.propertyPath == "m_Script") continue;
                    // Skip internal object fields
                    if (iter.propertyPath == "m_ObjectHideFlags") continue;

                    var propInfo = BuildPropertyInfo(iter, includeEnums, includeDefaults, componentType);
                    properties.Add(propInfo);
                }

                string assemblyName = componentType.Assembly.GetName().Name;
                string menuPath = null;
                var menuAttr = componentType.GetCustomAttribute<AddComponentMenu>();
                if (menuAttr != null && !string.IsNullOrEmpty(menuAttr.componentMenu))
                    menuPath = menuAttr.componentMenu;

                bool includeMethods = p.GetBool("includeMethods", false);

                var result = new Dictionary<string, object>
                {
                    ["typeName"] = componentType.Name,
                    ["fullName"] = componentType.FullName,
                    ["assembly"] = assemblyName,
                    ["menuPath"] = menuPath,
                    ["propertyCount"] = properties.Count,
                    ["properties"] = properties
                };

                if (includeMethods)
                {
                    result["methods"] = GetPublicMethods(componentType);
                }

                return new SuccessResponse($"Schema for {componentType.Name}.", result);
            }
            catch (Exception ex)
            {
                McpLog.Error($"{LogTag} Error inspecting {typeName}: {ex}");
                return new ErrorResponse($"Error inspecting component '{typeName}': {ex.Message}");
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(tempGo);
            }
        }

        private static Dictionary<string, object> BuildPropertyInfo(
            SerializedProperty prop, bool includeEnums, bool includeDefaults, Type componentType)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = prop.propertyPath,
                ["displayName"] = prop.displayName,
                ["type"] = prop.propertyType.ToString(),
                ["editable"] = prop.editable
            };

            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                info["isArray"] = true;
                info["arraySize"] = prop.arraySize;
            }

            // Enum values
            if (prop.propertyType == SerializedPropertyType.Enum && includeEnums)
            {
                var enumNames = prop.enumDisplayNames;
                var values = new Dictionary<string, object>();
                for (int i = 0; i < enumNames.Length; i++)
                    values[i.ToString()] = enumNames[i];
                info["enumValues"] = values;

                if (includeDefaults)
                    info["default"] = prop.enumValueIndex;
            }
            else if (includeDefaults)
            {
                info["default"] = GetDefaultValue(prop);
            }

            // Check for Range attribute on the backing field
            var rangeAttr = FindRangeAttribute(componentType, prop.propertyPath);
            if (rangeAttr != null)
            {
                info["range"] = new float[] { rangeAttr.min, rangeAttr.max };
            }

            // Tooltip
            var tooltipAttr = FindTooltipAttribute(componentType, prop.propertyPath);
            if (tooltipAttr != null)
                info["tooltip"] = tooltipAttr.tooltip;

            return info;
        }

        private static object GetDefaultValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return new { r = c.r, g = c.g, b = c.b, a = c.a };
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    return new float[] { v2.x, v2.y };
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    return new float[] { v3.x, v3.y, v3.z };
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return new float[] { v4.x, v4.y, v4.z, v4.w };
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null;
                default:
                    return null;
            }
        }

        private static RangeAttribute FindRangeAttribute(Type type, string propertyPath)
        {
            // propertyPath may be nested (e.g. "m_Color.r"), use only the first segment
            string fieldName = propertyPath.Contains('.') ? propertyPath.Split('.')[0] : propertyPath;
            return FindFieldAttribute<RangeAttribute>(type, fieldName);
        }

        private static TooltipAttribute FindTooltipAttribute(Type type, string propertyPath)
        {
            string fieldName = propertyPath.Contains('.') ? propertyPath.Split('.')[0] : propertyPath;
            return FindFieldAttribute<TooltipAttribute>(type, fieldName);
        }

        private static T FindFieldAttribute<T>(Type type, string fieldName) where T : Attribute
        {
            var current = type;
            while (current != null && current != typeof(UnityEngine.Object))
            {
                var field = current.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field.GetCustomAttribute<T>();
                current = current.BaseType;
            }
            return null;
        }

        private static string DeriveCategory(string ns, Type type = null)
        {
            if (string.IsNullOrEmpty(ns)) return "Scripts";

            if (ns.StartsWith("UnityEngine.UI")) return "UI";
            if (ns.Contains("Physics2D") || ns.Contains(".Physics2D")) return "Physics 2D";
            if (ns.Contains("Physics") || ns.Contains(".Physics")) return "Physics";
            if (ns.Contains("Rendering") || ns.Contains(".Rendering")) return "Rendering";
            if (ns.Contains("Audio")) return "Audio";
            if (ns.Contains("AI") || ns.Contains("NavMesh")) return "AI / Navigation";
            if (ns.Contains("Animation") || ns.Contains("Animations")) return "Animation";
            if (ns.Contains("Tilemaps") || ns.Contains("Tilemap")) return "Tilemap";
            if (ns.Contains("Video")) return "Video";
            if (ns.Contains("Cloth") || ns.Contains("ParticleSystem")) return "Effects";
            if (ns.Contains("TextMeshPro") || ns.Contains("TMPro")) return "TextMeshPro";
            if (ns.Contains("EventSystems")) return "Event System";

            // Core physics types live in UnityEngine namespace directly, not sub-namespaces
            if (type != null && ns == "UnityEngine")
            {
                if (typeof(Collider2D).IsAssignableFrom(type) || typeof(Rigidbody2D).IsAssignableFrom(type) || typeof(Joint2D).IsAssignableFrom(type))
                    return "Physics 2D";
                if (typeof(Collider).IsAssignableFrom(type) || typeof(Rigidbody).IsAssignableFrom(type) || typeof(Joint).IsAssignableFrom(type) || type == typeof(ConstantForce))
                    return "Physics";
            }

            if (ns.StartsWith("UnityEngine")) return "Engine";
            if (ns.StartsWith("UnityEditor")) return "Editor";

            return "Scripts";
        }

        private static readonly HashSet<string> SkipMethodNames = new(StringComparer.Ordinal)
        {
            "Equals", "GetHashCode", "GetType", "ToString", "GetInstanceID",
            "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren",
            "GetComponentInParent", "GetComponentsInParent", "TryGetComponent",
            "CompareTag", "SendMessage", "SendMessageUpwards", "BroadcastMessage",
        };

        private static List<object> GetPublicMethods(Type componentType)
        {
            var methods = componentType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !SkipMethodNames.Contains(m.Name))
                .OrderBy(m => m.Name)
                .Select(m =>
                {
                    var ps = m.GetParameters();
                    return (object)new Dictionary<string, object>
                    {
                        ["name"] = m.Name,
                        ["returnType"] = m.ReturnType == typeof(void) ? "void" : m.ReturnType.Name,
                        ["parameters"] = ps.Select(param => (object)new Dictionary<string, object>
                        {
                            ["name"] = param.Name,
                            ["type"] = param.ParameterType.Name,
                            ["optional"] = param.HasDefaultValue,
                        }).ToList(),
                    };
                })
                .ToList();

            return methods;
        }
    }
}
