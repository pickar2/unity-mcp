using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MCPForUnity.Runtime.Serialization; // For Converters
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Handles serialization of GameObjects and Components for MCP responses.
    /// Includes reflection helpers and caching for performance.
    /// </summary> 
    public static class GameObjectSerializer
    {
        // --- Internal property filtering (includeInternal=false) ---
        //
        // Four layers filter noise from built-in component output for LLM consumption:
        //   1. [Obsolete] attribute   — auto-skips deprecated properties (legacy shortcuts, etc.)
        //   2. Read-only (no setter)  — auto-skips computed/derived values (bounds, velocity, etc.)
        //   3. Base class declaring   — auto-skips inherited Object/Component noise (tag, name, etc.)
        //   4. Curated skip list      — manually maintained for writable-but-internal properties
        //
        // Layers 1-3 only apply to Unity built-in types (never user MonoBehaviours).
        // Layer 4 is the HashSet below.

        // Base types whose declared properties are infrastructure noise, not component-specific.
        // Properties from these types (tag, name, gameObject, hideFlags) are already on the outer object.
        // Behaviour is intentionally excluded so 'enabled' still comes through.
        private static readonly HashSet<Type> NoiseBaseTypes = new()
        {
            typeof(UnityEngine.Object),
            typeof(Component),
        };

        // Writable, non-deprecated engine properties too low-level for typical LLM use.
        // Read-only noise (bounds, isVisible, etc.) is handled automatically by the CanWrite check.
        // Deprecated properties (rigidbody, camera shortcuts) are handled by the Obsolete check.
        // Base class noise (tag, name, gameObject, hideFlags) is handled by the DeclaringType check.
        private static readonly HashSet<string> InternalPropertyNames = new(StringComparer.Ordinal)
        {
            // Renderer: lightmapping
            "lightmapScaleOffset", "realtimeLightmapScaleOffset",
            "lightmapTilingOffset", "realtimeLightmapTilingOffset",
            "lightmapIndex", "realtimeLightmapIndex",
            "scaleInLightmap", "stitchLightmapSeams", "globalIlluminationMeshLod",
            // Renderer: probes
            "lightProbeUsage", "reflectionProbeUsage", "lightProbeProxyVolumeOverride",
            "lightProbeAnchor", "probeAnchor",
            // Renderer: raytracing
            "rayTracingMode", "rayTracingAccelerationStructureBuildFlags",
            "rayTracingAccelerationStructureBuildFlagsOverride",
            // Renderer: shadows/motion (castShadows, receiveShadows, shadowCastingMode kept — useful for game dev)
            "motionVectorGenerationMode", "staticShadowCaster",
            "motionVectors", "useLightProbes",
            // Renderer: batching, LOD, misc
            "forceMeshLod", "meshLodSelectionBias", "allowOcclusionWhenDynamic",
            "rendererPriority", "renderingLayerMask",
            "forceRenderingOff", "sortingLayerID",
            // Renderer: GI/vertex streams
            "enlightenVertexStream", "additionalVertexStreams", "subMeshStartIndex", "receiveGI",
            // Renderer: bounds (writable in Unity 6+ but computed noise for LLMs)
            "bounds", "localBounds",
            // Collider/Rigidbody layer masks
            "excludeLayers", "includeLayers", "forceSendLayers", "forceReceiveLayers",
            "contactCaptureLayers", "callbackLayers",
            // Physics: solver internals
            "solverIterations", "solverVelocityIterations", "sleepThreshold",
            "maxDepenetrationVelocity", "maxAngularVelocity", "maxLinearVelocity",
            "contactOffset", "layerOverridePriority",
            // Physics: deprecated aliases
            "drag", "angularDrag",
            // Physics: advanced inertia
            "automaticCenterOfMass", "automaticInertiaTensor",
            "inertiaTensorRotation", "inertiaTensor",
            // Collider: advanced
            "hasModifiableContacts", "providesContacts",
        };

        /// <summary>
        /// Maximum time (ms) allowed for reading all properties/fields of a single component.
        /// If exceeded, remaining properties are skipped and a warning is logged.
        /// </summary>
        private const int ComponentBudgetMs = 2000;

        /// <summary>
        /// Maximum time (ms) allowed for a single property/field getter.
        /// Uses Task.Run + Wait to interrupt getters that hang indefinitely.
        /// Only applied after a per-component Stopwatch shows the component is already slow.
        /// </summary>
        private const int PropertyTimeoutMs = 500;

        /// <summary>
        /// Threshold (ms) for a single property read. If any read exceeds this,
        /// subsequent reads for the same component switch to guarded (threaded timeout) mode.
        /// </summary>
        private const int SlowPropertyThresholdMs = 100;

        // --- Data Serialization ---

        /// <summary>
        /// Creates a serializable representation of a GameObject.
        /// </summary>
        public static object GetGameObjectData(GameObject go)
        {
            if (go == null)
                return null;
            return new
            {
                name = go.name,
                instanceID = go.GetInstanceIDCompat(),
                tag = go.tag,
                layer = go.layer,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                isStatic = go.isStatic,
                scenePath = go.scene.path, // Identify which scene it belongs to
                transform = new // Serialize transform components carefully to avoid JSON issues
                {
                    // Serialize Vector3 components individually to prevent self-referencing loops.
                    // The default serializer can struggle with properties like Vector3.normalized.
                    position = new
                    {
                        x = go.transform.position.x,
                        y = go.transform.position.y,
                        z = go.transform.position.z,
                    },
                    localPosition = new
                    {
                        x = go.transform.localPosition.x,
                        y = go.transform.localPosition.y,
                        z = go.transform.localPosition.z,
                    },
                    rotation = new
                    {
                        x = go.transform.rotation.eulerAngles.x,
                        y = go.transform.rotation.eulerAngles.y,
                        z = go.transform.rotation.eulerAngles.z,
                    },
                    localRotation = new
                    {
                        x = go.transform.localRotation.eulerAngles.x,
                        y = go.transform.localRotation.eulerAngles.y,
                        z = go.transform.localRotation.eulerAngles.z,
                    },
                    scale = new
                    {
                        x = go.transform.localScale.x,
                        y = go.transform.localScale.y,
                        z = go.transform.localScale.z,
                    },
                    forward = new
                    {
                        x = go.transform.forward.x,
                        y = go.transform.forward.y,
                        z = go.transform.forward.z,
                    },
                    up = new
                    {
                        x = go.transform.up.x,
                        y = go.transform.up.y,
                        z = go.transform.up.z,
                    },
                    right = new
                    {
                        x = go.transform.right.x,
                        y = go.transform.right.y,
                        z = go.transform.right.z,
                    },
                },
                parentInstanceID = go.transform.parent?.gameObject.GetInstanceIDCompat() ?? 0, // 0 if no parent
                // Optionally include components, but can be large
                // components = go.GetComponents<Component>().Select(c => GetComponentData(c)).ToList()
                // Or just component names:
                componentNames = go.GetComponents<Component>()
                    .Select(c => c.GetType().FullName)
                    .ToList(),
            };
        }

        // --- Metadata Caching for Reflection ---
        private class CachedMetadata
        {
            public readonly List<PropertyInfo> SerializableProperties;
            public readonly List<FieldInfo> SerializableFields;

            public CachedMetadata(List<PropertyInfo> properties, List<FieldInfo> fields)
            {
                SerializableProperties = properties;
                SerializableFields = fields;
            }
        }
        // Key becomes Tuple<Type, bool>
        private static readonly Dictionary<Tuple<Type, bool>, CachedMetadata> _metadataCache = new Dictionary<Tuple<Type, bool>, CachedMetadata>();
        // --- End Metadata Caching ---

        /// <summary>
        /// Checks if a type is or derives from a type with the specified full name.
        /// Used to detect special-case components including their subclasses.
        /// </summary>
        private static bool IsOrDerivedFrom(Type type, string baseTypeFullName)
        {
            Type current = type;
            while (current != null)
            {
                if (current.FullName == baseTypeFullName)
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        // Type full names that are known to crash the Editor when accessed via reflection.
        // Photon Fusion uses IL weaving to inject fields with these types into NetworkBehaviour
        // subclasses. They contain native/unmanaged memory and cannot be safely serialized.
        private static readonly HashSet<string> _crashingTypeNames = new HashSet<string>
        {
            "Fusion.NetworkBehaviourBuffer",
            "Fusion.NetworkBehaviourCallbackBuffer",
            "Fusion.Networked+Internals",
            "Fusion.Changed`1",
        };
        private static readonly PropertyInfo _isByRefLikeProperty = typeof(Type).GetProperty("IsByRefLike");

        /// <summary>
        /// Checks if a type is unsafe to access via reflection or serialize.
        /// Returns true for ref structs (Span, ReadOnlySpan), pointer types,
        /// by-ref types, and known IL-weaved types that crash the Editor.
        /// </summary>
        private static bool IsUnsafeType(Type type)
        {
            return IsUnsafeType(type, new HashSet<Type>());
        }

        private static bool IsUnsafeType(Type type, HashSet<Type> visitedTypes)
        {
            if (type == null) return false;
            if (!visitedTypes.Add(type)) return false;

            // Pointer and by-ref types cannot be serialized
            if (type.IsPointer || type.IsByRef)
                return true;

            // Ref structs (Span<>, ReadOnlySpan<>, etc.) cannot be boxed. Use reflection
            // so Unity versions without Type.IsByRefLike still compile.
            if (type.IsValueType && _isByRefLikeProperty != null && (bool)_isByRefLikeProperty.GetValue(type, null))
                return true;

            // Check the type and its generic definition against the blacklist
            string fullName = type.FullName;
            if (fullName != null && _crashingTypeNames.Contains(fullName))
                return true;

            if (type.IsGenericType)
            {
                string genericFullName = type.GetGenericTypeDefinition()?.FullName;
                if (genericFullName != null && _crashingTypeNames.Contains(genericFullName))
                    return true;
            }

            // Catch-all for Fusion buffer types injected by IL weaving
            if (fullName != null && fullName.StartsWith("Fusion.") && fullName.Contains("Buffer"))
                return true;

            // Arrays and generic containers can wrap unsafe Fusion/ref-like types.
            // Newtonsoft.Json would still recurse into those values during serialization.
            Type elementType = type.GetElementType();
            if (elementType != null && IsUnsafeType(elementType, visitedTypes))
                return true;

            foreach (Type genericArgument in type.GetGenericArguments())
            {
                if (IsUnsafeType(genericArgument, visitedTypes))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Serializes a UnityEngine.Object reference to a dictionary with name, instanceID, and assetPath.
        /// Used for consistent serialization of asset references in special-case component handlers.
        /// </summary>
        /// <param name="obj">The Unity object to serialize</param>
        /// <param name="includeAssetPath">Whether to include the asset path (default true)</param>
        /// <returns>A dictionary with the object's reference info, or null if obj is null</returns>
        private static Dictionary<string, object> SerializeAssetReference(UnityEngine.Object obj, bool includeAssetPath = true)
        {
            if (obj == null) return null;
            
            var result = new Dictionary<string, object>
            {
                { "name", obj.name },
                { "instanceID", obj.GetInstanceIDCompat() }
            };
            
            if (includeAssetPath)
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                result["assetPath"] = string.IsNullOrEmpty(assetPath) ? null : assetPath;
            }
            
            return result;
        }

        /// <summary>
        /// Creates a serializable representation of a Component, attempting to serialize
        /// public properties and fields using reflection, with caching and control over non-public fields.
        /// </summary>
        // Add the flag parameter here
        public static object GetComponentData(Component c, bool includeNonPublicSerializedFields = true)
        {
            // --- Add Early Logging --- 
            // McpLog.Info($"[GetComponentData] Starting for component: {c?.GetType()?.FullName ?? "null"} (ID: {c?.GetInstanceIDCompat() ?? 0})");
            // --- End Early Logging ---

            if (c == null) return null;
            Type componentType = c.GetType();

            // Only apply internal filtering to Unity built-in types, never to user MonoBehaviours.
            // When the caller asks for everything (includeNonPublicSerializedFields=true) we skip filtering.
            bool shouldFilterInternal = !includeNonPublicSerializedFields && IsUnityBuiltInType(componentType);

            // --- Special handling for Transform to avoid reflection crashes and problematic properties --- 
            if (componentType == typeof(Transform))
            {
                Transform tr = c as Transform;
                // McpLog.Info($"[GetComponentData] Manually serializing Transform (ID: {tr.GetInstanceIDCompat()})");
                return new Dictionary<string, object>
                {
                    { "typeName", componentType.FullName },
                    { "instanceID", tr.GetInstanceIDCompat() },
                    { "properties", new Dictionary<string, object>
                        {
                            // Manually extract known-safe properties. Avoid Quaternion 'rotation' and 'lossyScale'.
                            { "position", CreateTokenFromValue(tr.position, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "localPosition", CreateTokenFromValue(tr.localPosition, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "eulerAngles", CreateTokenFromValue(tr.eulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "localEulerAngles", CreateTokenFromValue(tr.localEulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "localScale", CreateTokenFromValue(tr.localScale, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "right", CreateTokenFromValue(tr.right, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "up", CreateTokenFromValue(tr.up, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "forward", CreateTokenFromValue(tr.forward, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "parentInstanceID", tr.parent?.gameObject.GetInstanceIDCompat() ?? 0 },
                            { "rootInstanceID", tr.root?.gameObject.GetInstanceIDCompat() ?? 0 },
                            { "childCount", tr.childCount },
                        }
                    }
                };
            }
            // --- End Special handling for Transform --- 

            // --- Special handling for RectTransform (extends Transform with UI-specific properties) ---
            if (componentType == typeof(RectTransform))
            {
                RectTransform rt = c as RectTransform;
                return new Dictionary<string, object>
                {
                    { "typeName", componentType.FullName },
                    { "instanceID", rt.GetInstanceIDCompat() },
                    { "properties", new Dictionary<string, object>
                        {
                            { "anchoredPosition", CreateTokenFromValue(rt.anchoredPosition, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "sizeDelta", CreateTokenFromValue(rt.sizeDelta, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "anchorMin", CreateTokenFromValue(rt.anchorMin, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "anchorMax", CreateTokenFromValue(rt.anchorMax, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "pivot", CreateTokenFromValue(rt.pivot, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "offsetMin", CreateTokenFromValue(rt.offsetMin, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "offsetMax", CreateTokenFromValue(rt.offsetMax, typeof(Vector2))?.ToObject<object>() ?? new JObject() },
                            { "localPosition", CreateTokenFromValue(rt.localPosition, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "localEulerAngles", CreateTokenFromValue(rt.localEulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "localScale", CreateTokenFromValue(rt.localScale, typeof(Vector3))?.ToObject<object>() ?? new JObject() },
                            { "parentInstanceID", rt.parent?.gameObject.GetInstanceIDCompat() ?? 0 },
                            { "childCount", rt.childCount },
                        }
                    }
                };
            }
            // --- End Special handling for RectTransform --- 

            // --- Special handling for Camera to avoid matrix-related crashes ---
            if (componentType == typeof(Camera))
            {
                Camera cam = c as Camera;
                var cameraProperties = new Dictionary<string, object>();

                // List of safe properties to serialize
                var safeProperties = new Dictionary<string, Func<object>>
                {
                    { "nearClipPlane", () => cam.nearClipPlane },
                    { "farClipPlane", () => cam.farClipPlane },
                    { "fieldOfView", () => cam.fieldOfView },
                    { "renderingPath", () => (int)cam.renderingPath },
                    { "actualRenderingPath", () => (int)cam.actualRenderingPath },
                    { "allowHDR", () => cam.allowHDR },
                    { "allowMSAA", () => cam.allowMSAA },
                    { "allowDynamicResolution", () => cam.allowDynamicResolution },
                    { "forceIntoRenderTexture", () => cam.forceIntoRenderTexture },
                    { "orthographicSize", () => cam.orthographicSize },
                    { "orthographic", () => cam.orthographic },
                    { "opaqueSortMode", () => (int)cam.opaqueSortMode },
                    { "transparencySortMode", () => (int)cam.transparencySortMode },
                    { "depth", () => cam.depth },
                    { "aspect", () => cam.aspect },
                    { "cullingMask", () => cam.cullingMask },
                    { "eventMask", () => cam.eventMask },
                    { "backgroundColor", () => cam.backgroundColor },
                    { "clearFlags", () => (int)cam.clearFlags },
                    { "stereoEnabled", () => cam.stereoEnabled },
                    { "stereoSeparation", () => cam.stereoSeparation },
                    { "stereoConvergence", () => cam.stereoConvergence },
                    { "enabled", () => cam.enabled },
                    { "name", () => cam.name },
                    { "tag", () => cam.tag },
                    { "gameObject", () => new { name = cam.gameObject.name, instanceID = cam.gameObject.GetInstanceIDCompat() } }
                };

                foreach (var prop in safeProperties)
                {
                    try
                    {
                        var value = prop.Value();
                        if (value != null)
                        {
                            AddSerializableValue(cameraProperties, prop.Key, value.GetType(), value);
                        }
                    }
                    catch (Exception)
                    {
                        // Silently skip any property that fails
                        continue;
                    }
                }

                return new Dictionary<string, object>
                {
                    { "typeName", componentType.FullName },
                    { "instanceID", cam.GetInstanceIDCompat() },
                    { "properties", cameraProperties }
                };
            }
            // --- End Special handling for Camera ---

            // --- Special handling for UIDocument to avoid infinite loops from VisualElement hierarchy (Issue #585) ---
            // UIDocument.rootVisualElement contains circular parent/child references that cause infinite serialization loops.
            // Use IsOrDerivedFrom to also catch subclasses of UIDocument.
            if (IsOrDerivedFrom(componentType, "UnityEngine.UIElements.UIDocument"))
            {
                var uiDocProperties = new Dictionary<string, object>();

                try
                {
                    // Get panelSettings reference safely
                    var panelSettingsProp = componentType.GetProperty("panelSettings");
                    if (panelSettingsProp != null)
                    {
                        var panelSettings = panelSettingsProp.GetValue(c) as UnityEngine.Object;
                        uiDocProperties["panelSettings"] = SerializeAssetReference(panelSettings);
                    }

                    // Get visualTreeAsset reference safely (the UXML file)
                    var visualTreeAssetProp = componentType.GetProperty("visualTreeAsset");
                    if (visualTreeAssetProp != null)
                    {
                        var visualTreeAsset = visualTreeAssetProp.GetValue(c) as UnityEngine.Object;
                        uiDocProperties["visualTreeAsset"] = SerializeAssetReference(visualTreeAsset);
                    }

                    // Get sortingOrder safely
                    var sortingOrderProp = componentType.GetProperty("sortingOrder");
                    if (sortingOrderProp != null)
                    {
                        uiDocProperties["sortingOrder"] = sortingOrderProp.GetValue(c);
                    }

                    // Get enabled state (from Behaviour base class)
                    var enabledProp = componentType.GetProperty("enabled");
                    if (enabledProp != null)
                    {
                        uiDocProperties["enabled"] = enabledProp.GetValue(c);
                    }

                    // Get parentUI reference safely (no asset path needed - it's a scene reference)
                    var parentUIProp = componentType.GetProperty("parentUI");
                    if (parentUIProp != null)
                    {
                        var parentUI = parentUIProp.GetValue(c) as UnityEngine.Object;
                        uiDocProperties["parentUI"] = SerializeAssetReference(parentUI, includeAssetPath: false);
                    }

                    // NOTE: rootVisualElement is intentionally skipped - it contains circular
                    // parent/child references that cause infinite serialization loops
                    uiDocProperties["_note"] = "rootVisualElement skipped to prevent circular reference loops";
                }
                catch (Exception e)
                {
                    McpLog.Warn($"[GetComponentData] Error reading UIDocument properties: {e.Message}");
                }

                // Return structure matches Camera special handling (typeName, instanceID, properties)
                return new Dictionary<string, object>
                {
                    { "typeName", componentType.FullName },
                    { "instanceID", c.GetInstanceIDCompat() },
                    { "properties", uiDocProperties }
                };
            }
            // --- End Special handling for UIDocument ---

            var data = new Dictionary<string, object>
            {
                { "typeName", componentType.FullName },
                { "instanceID", c.GetInstanceIDCompat() }
            };

            // --- Get Cached or Generate Metadata (using new cache key) ---
            Tuple<Type, bool> cacheKey = new Tuple<Type, bool>(componentType, includeNonPublicSerializedFields);
            if (!_metadataCache.TryGetValue(cacheKey, out CachedMetadata cachedData))
            {
                var propertiesToCache = new List<PropertyInfo>();
                var fieldsToCache = new List<FieldInfo>();

                // Traverse the hierarchy from the component type up to MonoBehaviour
                Type currentType = componentType;
                while (currentType != null && currentType != typeof(MonoBehaviour) && currentType != typeof(object))
                {
                    // Get properties declared only at the current type level
                    BindingFlags propFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    foreach (var propInfo in currentType.GetProperties(propFlags))
                    {
                        // Basic filtering (readable, not indexer, not transform which is handled elsewhere)
                        if (!propInfo.CanRead || propInfo.GetIndexParameters().Length > 0 || propInfo.Name == "transform") continue;
                        // Layer 1: Skip deprecated properties (replaces hardcoded obsolete shortcut list)
                        if (propInfo.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
                        // Skip properties whose return type would crash when accessed via reflection
                        // (e.g. Fusion IL-weaved types, Span<>, ReadOnlySpan<>, pointers)
                        if (IsUnsafeType(propInfo.PropertyType)) continue;
                        // Add if not already added (handles overrides - keep the most derived version)
                        if (!propertiesToCache.Any(p => p.Name == propInfo.Name))
                        {
                            propertiesToCache.Add(propInfo);
                        }
                    }

                    // Get fields declared only at the current type level (both public and non-public)
                    BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    var declaredFields = currentType.GetFields(fieldFlags);

                    // Process the declared Fields for caching
                    foreach (var fieldInfo in declaredFields)
                    {
                        if (fieldInfo.Name.EndsWith("k__BackingField")) continue; // Skip backing fields
                        // Skip fields whose type would crash when accessed via reflection
                        // (e.g. Fusion IL-weaved types, Span<>, ReadOnlySpan<>, pointers)
                        if (IsUnsafeType(fieldInfo.FieldType)) continue;

                        // Add if not already added (handles hiding - keep the most derived version)
                        if (fieldsToCache.Any(f => f.Name == fieldInfo.Name)) continue;

                        bool shouldInclude = false;
                        if (includeNonPublicSerializedFields)
                        {
                            // If TRUE, include Public OR any NonPublic with [SerializeField] (private/protected/internal)
                            var hasSerializeField = fieldInfo.IsDefined(typeof(SerializeField), inherit: true);
                            shouldInclude = fieldInfo.IsPublic || (!fieldInfo.IsPublic && hasSerializeField);
                        }
                        else // includeNonPublicSerializedFields is FALSE
                        {
                            // If FALSE, include ONLY if it is explicitly Public.
                            shouldInclude = fieldInfo.IsPublic;
                        }

                        if (shouldInclude)
                        {
                            fieldsToCache.Add(fieldInfo);
                        }
                    }

                    // Move to the base type
                    currentType = currentType.BaseType;
                }
                // --- End Hierarchy Traversal ---

                cachedData = new CachedMetadata(propertiesToCache, fieldsToCache);
                _metadataCache[cacheKey] = cachedData; // Add to cache with combined key
            }
            // --- End Get Cached or Generate Metadata ---

            // --- Use cached metadata ---
            var serializablePropertiesOutput = new Dictionary<string, object>();
            var componentSw = Stopwatch.StartNew();
            bool useGuardedRead = false; // Escalate to threaded timeout if any read is slow
            int skippedCount = 0;

            // Use cached properties
            foreach (var propInfo in cachedData.SerializableProperties)
            {
                // Budget check: stop serializing this component if over time
                if (componentSw.ElapsedMilliseconds > ComponentBudgetMs)
                {
                    skippedCount = cachedData.SerializableProperties.Count
                                 + cachedData.SerializableFields.Count
                                 - serializablePropertiesOutput.Count;
                    McpLog.Warn($"[GetComponentData] Time budget exceeded for {componentType.Name} " +
                                $"({componentSw.ElapsedMilliseconds}ms). Skipped ~{skippedCount} remaining members.");
                    break;
                }

                string propName = propInfo.Name;
                bool skipProperty = false;

                // --- Safety skips: properties that crash serialization regardless of filtering ---
                if (componentType == typeof(Camera) &&
                    (propName == "pixelRect" || propName == "rect" ||
                     propName == "cullingMatrix" || propName == "useOcclusionCulling" ||
                     propName == "worldToCameraMatrix" || propName == "projectionMatrix" ||
                     propName == "nonJitteredProjectionMatrix" || propName == "previousViewProjectionMatrix" ||
                     propName == "cameraToWorldMatrix"))
                    skipProperty = true;

                // Use IsAssignableFrom so RectTransform (a Transform subclass) is also covered.
                if (typeof(Transform).IsAssignableFrom(componentType) &&
                    (propName == "lossyScale" || propName == "rotation" ||
                     propName == "worldToLocalMatrix" || propName == "localToWorldMatrix"))
                    skipProperty = true;

                if (typeof(Collider).IsAssignableFrom(componentType) && propName == "GeometryHolder")
                    skipProperty = true;

                // --- Internal filtering layers (only for built-in types when includeInternal=false) ---
                if (shouldFilterInternal)
                {
                    // Layer 2: Skip read-only computed/derived properties (bounds, velocity, isVisible, etc.)
                    if (!propInfo.CanWrite)
                        skipProperty = true;
                    // Layer 3: Skip base class noise (tag, name from Object/Component — already on outer object)
                    else if (NoiseBaseTypes.Contains(propInfo.DeclaringType))
                        skipProperty = true;
                    // Layer 4: Curated skip list for writable-but-internal properties
                    else if (InternalPropertyNames.Contains(propName))
                        skipProperty = true;
                }

                if (skipProperty)
                    continue;

                try
                {
                    // --- Special handling for material/mesh properties in edit mode ---
                    object value;
                    if (!Application.isPlaying && (propName == "material" || propName == "materials" || propName == "mesh"))
                    {
                        // In edit mode, use sharedMaterial/sharedMesh to avoid instantiation warnings
                        if ((propName == "material" || propName == "materials") && c is Renderer renderer)
                            value = propName == "material" ? (object)renderer.sharedMaterial : renderer.sharedMaterials;
                        else if (propName == "mesh" && c is MeshFilter meshFilter)
                            value = meshFilter.sharedMesh;
                        else
                            value = ReadPropertyValue(propInfo, c, componentType, ref useGuardedRead);
                    }
                    else
                    {
                        value = ReadPropertyValue(propInfo, c, componentType, ref useGuardedRead);
                    }
                    // --- End special handling ---

                    Type propType = propInfo.PropertyType;
                    AddSerializableValue(serializablePropertiesOutput, propName, propType, value);
                }
                catch (TimeoutException)
                {
                    McpLog.Warn($"[GetComponentData] Property '{propName}' on {componentType.Name} timed out. Skipping.");
                }
                catch (Exception)
                {
                    // Silently skip unreadable properties
                }
            }

            // Use cached fields (only if budget not exhausted)
            if (componentSw.ElapsedMilliseconds <= ComponentBudgetMs)
            {
                foreach (var fieldInfo in cachedData.SerializableFields)
                {
                    if (componentSw.ElapsedMilliseconds > ComponentBudgetMs)
                    {
                        McpLog.Warn($"[GetComponentData] Time budget exceeded for {componentType.Name} during field reads.");
                        break;
                    }

                    // Internal filtering for fields (built-in types only)
                    if (shouldFilterInternal &&
                        (InternalPropertyNames.Contains(fieldInfo.Name) ||
                         NoiseBaseTypes.Contains(fieldInfo.DeclaringType)))
                        continue;

                    // Skip backing fields (m_Foo) when the corresponding property (foo/Foo)
                    // is already serialized — avoids noisy duplication like minWidth + m_MinWidth.
                    if (shouldFilterInternal && fieldInfo.Name.StartsWith("m_"))
                    {
                        string stripped = fieldInfo.Name.Substring(2);
                        // Check camelCase (m_MinWidth → minWidth) and PascalCase (m_MinWidth → MinWidth)
                        string camel = char.ToLowerInvariant(stripped[0]) + stripped.Substring(1);
                        if (serializablePropertiesOutput.ContainsKey(camel) || serializablePropertiesOutput.ContainsKey(stripped))
                            continue;
                    }

                    try
                    {
                        object value = fieldInfo.GetValue(c);
                        string fieldName = fieldInfo.Name;
                        Type fieldType = fieldInfo.FieldType;
                        AddSerializableValue(serializablePropertiesOutput, fieldName, fieldType, value);
                    }
                    catch (Exception)
                    {
                        // Silently skip unreadable fields
                    }
                }
            }
            // --- End Use cached metadata ---

            if (serializablePropertiesOutput.Count > 0)
            {
                data["properties"] = serializablePropertiesOutput;
            }

            return data;
        }

        /// <summary>
        /// Reads a property value with optional guarded (threaded timeout) mode.
        /// When useGuardedRead is true, the getter runs on a thread pool thread with a timeout.
        /// If any non-guarded read takes longer than SlowPropertyThresholdMs, escalates to guarded mode.
        /// Throws TimeoutException if the getter hangs.
        /// </summary>
        private static object ReadPropertyValue(PropertyInfo propInfo, Component c, Type componentType, ref bool useGuardedRead)
        {
            if (useGuardedRead)
            {
                // Already know this component is slow — use threaded timeout
                object result = null;
                Exception caught = null;
                var task = Task.Run(() =>
                {
                    try { result = propInfo.GetValue(c); }
                    catch (Exception ex) { caught = ex; }
                });
                if (!task.Wait(PropertyTimeoutMs))
                    throw new TimeoutException($"{componentType.Name}.{propInfo.Name}");
                if (caught != null)
                    throw caught;
                return result;
            }

            // Normal read with timing
            long before = Stopwatch.GetTimestamp();
            object value = propInfo.GetValue(c);
            long elapsed = (Stopwatch.GetTimestamp() - before) * 1000 / Stopwatch.Frequency;
            if (elapsed > SlowPropertyThresholdMs)
            {
                McpLog.Warn($"[GetComponentData] Slow property: {componentType.Name}.{propInfo.Name} took {elapsed}ms. Switching to guarded reads.");
                useGuardedRead = true;
            }
            return value;
        }

        // Helper function to decide how to serialize different types
        private static void AddSerializableValue(Dictionary<string, object> dict, string name, Type type, object value)
        {
            // Simplified: Directly use CreateTokenFromValue which uses the serializer
            if (value == null)
            {
                dict[name] = null;
                return;
            }

            try
            {
                // Use the helper that employs our custom serializer settings
                JToken token = CreateTokenFromValue(value, type);
                if (token != null) // Check if serialization succeeded in the helper
                {
                    // Convert JToken back to a basic object structure for the dictionary
                    dict[name] = ConvertJTokenToPlainObject(token);
                }
                // If token is null, it means serialization failed and a warning was logged.
            }
            catch (Exception e)
            {
                // Catch potential errors during JToken conversion or addition to dictionary
                McpLog.Warn($"[AddSerializableValue] Error processing value for '{name}' (Type: {type.FullName}): {e.Message}. Skipping.");
            }
        }

        // Helper to convert JToken back to basic object structure
        public static object ConvertJTokenToPlainObject(JToken token)
        {
            if (token == null) return null;

            switch (token.Type)
            {
                case JTokenType.Object:
                    var objDict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        objDict[prop.Name] = ConvertJTokenToPlainObject(prop.Value);
                    }
                    return objDict;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                    {
                        list.Add(ConvertJTokenToPlainObject(item));
                    }
                    return list;

                case JTokenType.Integer:
                    return token.ToObject<long>(); // Use long for safety
                case JTokenType.Float:
                    return token.ToObject<double>(); // Use double for safety
                case JTokenType.String:
                    return token.ToObject<string>();
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                case JTokenType.Date:
                    return token.ToObject<DateTime>();
                case JTokenType.Guid:
                    return token.ToObject<Guid>();
                case JTokenType.Uri:
                    return token.ToObject<Uri>();
                case JTokenType.TimeSpan:
                    return token.ToObject<TimeSpan>();
                case JTokenType.Bytes:
                    return token.ToObject<byte[]>();
                case JTokenType.Null:
                    return null;
                case JTokenType.Undefined:
                    return null; // Treat undefined as null

                default:
                    // Fallback for simple value types not explicitly listed
                    if (token is JValue jValue && jValue.Value != null)
                    {
                        return jValue.Value;
                    }
                    // McpLog.Warn($"Unsupported JTokenType encountered: {token.Type}. Returning null.");
                    return null;
            }
        }

        // --- Define custom JsonSerializerSettings for OUTPUT ---
        private static readonly JsonSerializerSettings _outputSerializerSettings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Newtonsoft.Json.Converters.StringEnumConverter(), // Serialize enums as string names for agent readability
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new Matrix4x4Converter(), // Fix #478: Safe Matrix4x4 serialization for Cinemachine
                new UnityEngineObjectConverter() // Handles serialization of references
            },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            // ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } // Example if needed
        };
        public static readonly JsonSerializer OutputSerializer = JsonSerializer.Create(_outputSerializerSettings);
        // --- End Define custom JsonSerializerSettings ---

        // Helper to create JToken using the output serializer
        private static JToken CreateTokenFromValue(object value, Type type)
        {
            if (value == null) return JValue.CreateNull();

            // Skip types that crash Newtonsoft.Json serialization (Unity 6+ TransformHandle
            // implements IEnumerable but throws NullReferenceException when enumerated)
            string typeName = type.Name;
            if (typeName == "TransformHandle" || typeName == "TransformAccessArray")
                return null;

            try
            {
                // Use the pre-configured OUTPUT serializer instance
                return JToken.FromObject(value, OutputSerializer);
            }
            catch (JsonSerializationException e)
            {
                McpLog.Warn($"[GameObjectSerializer] Newtonsoft.Json Error serializing value of type {type.FullName}: {e.Message}. Skipping property/field.");
                return null; // Indicate serialization failure
            }
            catch (Exception e) // Catch other unexpected errors
            {
                McpLog.Warn($"[GameObjectSerializer] Unexpected error serializing value of type {type.FullName}: {e}. Skipping property/field.");
                return null; // Indicate serialization failure
            }
        }

        private static bool IsUnityBuiltInType(Type type)
        {
            if (type == null) return false;
            string ns = type.Namespace;
            if (string.IsNullOrEmpty(ns)) return false;
            return ns == "UnityEngine" || ns == "UnityEditor"
                || ns.StartsWith("UnityEngine.") || ns.StartsWith("UnityEditor.");
        }
    }
}
