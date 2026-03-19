using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools.Prefabs
{
    [McpForUnityTool("manage_prefabs", AutoRegister = false)]
    /// <summary>
    /// Tool to manage Unity Prefabs: create, inspect, and modify prefab assets.
    /// Uses headless editing (no UI, no dialogs) for reliable automated workflows.
    /// </summary>
    public static class ManagePrefabs
    {
        // Action constants
        private const string ACTION_CREATE_FROM_GAMEOBJECT = "create_from_gameobject";
        private const string ACTION_GET_INFO = "get_info";
        private const string ACTION_GET_HIERARCHY = "get_hierarchy";
        private const string ACTION_MODIFY_CONTENTS = "modify_contents";
        private const string SupportedActions = ACTION_CREATE_FROM_GAMEOBJECT + ", " + ACTION_GET_INFO + ", " + ACTION_GET_HIERARCHY + ", " + ACTION_MODIFY_CONTENTS;

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse($"Action parameter is required. Valid actions are: {SupportedActions}.");
            }

            // Block asset-modifying actions in play mode (triggers asset pipeline / domain reload)
            if (EditorApplication.isPlaying &&
                (action == ACTION_CREATE_FROM_GAMEOBJECT || action == ACTION_MODIFY_CONTENTS))
            {
                return new ErrorResponse(
                    $"Cannot {action} prefabs in play mode. Exit play mode first.");
            }

            try
            {
                switch (action)
                {
                    case ACTION_CREATE_FROM_GAMEOBJECT:
                        return CreatePrefabFromGameObject(@params);
                    case ACTION_GET_INFO:
                        return GetInfo(@params);
                    case ACTION_GET_HIERARCHY:
                        return GetHierarchy(@params);
                    case ACTION_MODIFY_CONTENTS:
                        return ModifyContents(@params);
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'. Valid actions are: {SupportedActions}.");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePrefabs] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Create Prefab from GameObject

        /// <summary>
        /// Creates a prefab asset from a GameObject in the scene.
        /// </summary>
        private static object CreatePrefabFromGameObject(JObject @params)
        {
            // 1. Validate and parse parameters
            var validation = ValidateCreatePrefabParams(@params);
            if (!validation.isValid)
            {
                return new ErrorResponse(validation.errorMessage);
            }

            string targetName = validation.targetName;
            string finalPath = validation.finalPath;
            bool includeInactive = validation.includeInactive;
            bool replaceExisting = validation.replaceExisting;
            bool unlinkIfInstance = validation.unlinkIfInstance;

            // 2. Find the source object
            GameObject sourceObject = FindSceneObjectByName(targetName, includeInactive);
            if (sourceObject == null)
            {
                return new ErrorResponse($"GameObject '{targetName}' not found in the active scene or prefab stage{(includeInactive ? " (including inactive objects)" : "")}.");
            }

            // 3. Validate source object state
            var objectValidation = ValidateSourceObjectForPrefab(sourceObject, unlinkIfInstance);
            if (!objectValidation.isValid)
            {
                return new ErrorResponse(objectValidation.errorMessage);
            }

            // 4. Check for path conflicts and track if file will be replaced
            bool fileExistedAtPath = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPath) != null;

            if (!replaceExisting && fileExistedAtPath)
            {
                finalPath = AssetDatabase.GenerateUniqueAssetPath(finalPath);
                McpLog.Info($"[ManagePrefabs] Generated unique path: {finalPath}");
            }

            // 5. Ensure directory exists
            EnsureAssetDirectoryExists(finalPath);

            // 6. Unlink from existing prefab if needed
            if (unlinkIfInstance && objectValidation.shouldUnlink)
            {
                try
                {
                    // UnpackPrefabInstance requires the prefab instance root, not a child object
                    GameObject rootToUnlink = PrefabUtility.GetOutermostPrefabInstanceRoot(sourceObject);
                    if (rootToUnlink != null)
                    {
                        PrefabUtility.UnpackPrefabInstance(rootToUnlink, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                        McpLog.Info($"[ManagePrefabs] Unpacked prefab instance '{rootToUnlink.name}' before creating new prefab.");
                    }
                }
                catch (Exception e)
                {
                    return new ErrorResponse($"Failed to unlink prefab instance: {e.Message}");
                }
            }

            // 7. Create the prefab
            try
            {
                GameObject result = CreatePrefabAsset(sourceObject, finalPath, replaceExisting);

                if (result == null)
                {
                    return new ErrorResponse($"Failed to create prefab asset at '{finalPath}'.");
                }

                // 8. Select the newly created instance
                Selection.activeGameObject = result;

                return new SuccessResponse(
                    $"Prefab created at '{finalPath}' and instance linked.",
                    new
                    {
                        prefabPath = finalPath,
                        instanceId = result.GetInstanceID(),
                        instanceName = result.name,
                        wasUnlinked = unlinkIfInstance && objectValidation.shouldUnlink,
                        wasReplaced = replaceExisting && fileExistedAtPath,
                        componentCount = result.GetComponents<Component>().Length,
                        childCount = result.transform.childCount
                    }
                );
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePrefabs] Error creating prefab at '{finalPath}': {e}");
                return new ErrorResponse($"Error saving prefab asset: {e.Message}");
            }
        }

        /// <summary>
        /// Validates parameters for creating a prefab from GameObject.
        /// </summary>
        private static (bool isValid, string errorMessage, string targetName, string finalPath, bool includeInactive, bool replaceExisting, bool unlinkIfInstance)
        ValidateCreatePrefabParams(JObject @params)
        {
            string targetName = @params["target"]?.ToString() ?? @params["name"]?.ToString();
            if (string.IsNullOrEmpty(targetName))
            {
                return (false, "'target' parameter is required for create_from_gameobject.", null, null, false, false, false);
            }

            string requestedPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return (false, "'prefabPath' parameter is required for create_from_gameobject.", targetName, null, false, false, false);
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(requestedPath);
            if (sanitizedPath == null)
            {
                return (false, $"Invalid prefab path (path traversal detected): '{requestedPath}'", targetName, null, false, false, false);
            }
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return (false, $"Invalid prefab path '{requestedPath}'. Path cannot be empty.", targetName, null, false, false, false);
            }
            if (!sanitizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedPath += ".prefab";
            }

            // Validate path is within Assets folder
            if (!sanitizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Prefab path must be within the Assets folder. Got: '{sanitizedPath}'", targetName, null, false, false, false);
            }

            bool includeInactive = @params["searchInactive"]?.ToObject<bool>() ?? false;
            bool replaceExisting = @params["allowOverwrite"]?.ToObject<bool>() ?? false;
            bool unlinkIfInstance = @params["unlinkIfInstance"]?.ToObject<bool>() ?? false;

            return (true, null, targetName, sanitizedPath, includeInactive, replaceExisting, unlinkIfInstance);
        }

        /// <summary>
        /// Validates source object can be converted to prefab.
        /// </summary>
        private static (bool isValid, string errorMessage, bool shouldUnlink, string existingPrefabPath)
            ValidateSourceObjectForPrefab(GameObject sourceObject, bool unlinkIfInstance)
        {
            // Check if this is a Prefab Asset (the .prefab file itself in the editor)
            if (PrefabUtility.IsPartOfPrefabAsset(sourceObject))
            {
                return (false,
                    $"GameObject '{sourceObject.name}' is part of a prefab asset. " +
                    "Open the prefab stage to save changes instead.",
                    false, null);
            }

            // Check if this is already a Prefab Instance
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(sourceObject);
            if (status != PrefabInstanceStatus.NotAPrefab)
            {
                string existingPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceObject);

                if (!unlinkIfInstance)
                {
                    return (false,
                        $"GameObject '{sourceObject.name}' is already linked to prefab '{existingPath}'. " +
                        "Set 'unlinkIfInstance' to true to unlink it first, or modify the existing prefab instead.",
                        false, existingPath);
                }

                // Needs to be unlinked
                return (true, null, true, existingPath);
            }

            return (true, null, false, null);
        }

        /// <summary>
        /// Creates a prefab asset from a GameObject.
        /// </summary>
        private static GameObject CreatePrefabAsset(GameObject sourceObject, string path, bool replaceExisting)
        {
            GameObject result = PrefabUtility.SaveAsPrefabAssetAndConnect(
                sourceObject,
                path,
                InteractionMode.AutomatedAction
            );

            string action = replaceExisting ? "Replaced existing" : "Created new";
            McpLog.Info($"[ManagePrefabs] {action} prefab at '{path}'.");

            if (result != null)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return result;
        }

        #endregion

        /// <summary>
        /// Ensures the directory for an asset path exists, creating it if necessary.
        /// </summary>
        private static void EnsureAssetDirectoryExists(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            // Use Application.dataPath for more reliable path resolution
            // Application.dataPath points to the Assets folder (e.g., ".../ProjectName/Assets")
            string assetsPath = Application.dataPath;
            string projectRoot = Path.GetDirectoryName(assetsPath);
            string fullDirectory = Path.Combine(projectRoot, directory);

            if (!Directory.Exists(fullDirectory))
            {
                Directory.CreateDirectory(fullDirectory);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                McpLog.Info($"[ManagePrefabs] Created directory: {directory}");
            }
        }

        /// <summary>
        /// Finds a GameObject by name in the active scene or current prefab stage.
        /// </summary>
        private static GameObject FindSceneObjectByName(string name, bool includeInactive)
        {
            // First check if we're in Prefab Stage
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage?.prefabContentsRoot != null)
            {
                foreach (Transform transform in stage.prefabContentsRoot.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (transform.name == name && (includeInactive || transform.gameObject.activeSelf))
                    {
                        return transform.gameObject;
                    }
                }
            }

            // Search in the active scene
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                // Check the root object itself
                if (root.name == name && (includeInactive || root.activeSelf))
                {
                    return root;
                }

                // Check children
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (transform.name == name && (includeInactive || transform.gameObject.activeSelf))
                    {
                        return transform.gameObject;
                    }
                }
            }

            return null;
        }

        #region Read Operations

        /// <summary>
        /// Gets basic metadata information about a prefab asset.
        /// </summary>
        private static object GetInfo(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for get_info.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return new ErrorResponse($"Invalid prefab path: '{prefabPath}'.");
            }
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sanitizedPath);
            if (prefabAsset == null)
            {
                return new ErrorResponse($"No prefab asset found at path '{sanitizedPath}'.");
            }

            string guid = PrefabUtilityHelper.GetPrefabGUID(sanitizedPath);
            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
            string prefabTypeString = assetType.ToString();
            int childCount = PrefabUtilityHelper.CountChildrenRecursive(prefabAsset.transform);
            var (isVariant, parentPrefab, _) = PrefabUtilityHelper.GetVariantInfo(prefabAsset);

            // Resolve target for component reading (defaults to root)
            string target = @params["target"]?.ToString();
            GameObject targetGo = prefabAsset;
            if (!string.IsNullOrEmpty(target))
            {
                Transform found = FindChildByNameOrPath(prefabAsset.transform, target);
                if (found == null)
                    return new ErrorResponse($"Target '{target}' not found in prefab at '{sanitizedPath}'.");
                targetGo = found.gameObject;
            }

            var componentTypes = PrefabUtilityHelper.GetComponentTypeNames(targetGo);

            var data = new Dictionary<string, object>
            {
                ["assetPath"] = sanitizedPath,
                ["guid"] = guid,
                ["prefabType"] = prefabTypeString,
                ["rootObjectName"] = prefabAsset.name,
                ["rootComponentTypes"] = componentTypes,
                ["childCount"] = childCount,
                ["isVariant"] = isVariant,
                ["parentPrefab"] = parentPrefab
            };

            // Include component serialized data if requested
            var componentFilter = ParseComponentsParam(@params);
            if (componentFilter != null)
            {
                var p = new ToolParams(@params);
                bool includeInternal = p.GetBool("includeInternal", false);
                data["components"] = SerializeComponentData(targetGo, componentFilter, includeInternal);
                if (!string.IsNullOrEmpty(target))
                    data["target"] = target;
            }

            return new SuccessResponse("Successfully retrieved prefab info.", data);
        }

        /// <summary>
        /// Gets the hierarchical structure of a prefab asset with pagination.
        /// Supports page_size (default 200), cursor, and max_depth (default 50) parameters.
        /// Response includes next_cursor when more items are available.
        /// </summary>
        private static object GetHierarchy(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for get_hierarchy.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return new ErrorResponse($"Invalid prefab path '{prefabPath}'. Path traversal sequences are not allowed.");
            }

            // Load prefab contents in background (without opening stage UI)
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(sanitizedPath);
            if (prefabContents == null)
            {
                return new ErrorResponse($"Failed to load prefab contents from '{sanitizedPath}'.");
            }

            try
            {
                var componentFilter = ParseComponentsParam(@params);
                var p = new ToolParams(@params);
                bool includeInternal = p.GetBool("includeInternal", false);
                int pageSize = Mathf.Clamp(p.GetInt("page_size") ?? 200, 1, 1000);
                int cursor = Mathf.Max(p.GetInt("cursor") ?? 0, 0);
                int maxDepth = Mathf.Max(p.GetInt("max_depth") ?? 50, 0);

                // Parse optional properties filter — same semantics as scene_object's properties param:
                // a list of property names to include per component (e.g. ["sizeDelta", "anchoredPosition"]).
                HashSet<string> propertiesFilter = null;
                var propsToken = @params["properties"];
                if (propsToken != null && propsToken.Type == JTokenType.Array)
                {
                    propertiesFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in propsToken)
                    {
                        string val = item?.ToString();
                        if (!string.IsNullOrEmpty(val))
                            propertiesFilter.Add(val);
                    }
                }
                else if (propsToken != null && propsToken.Type == JTokenType.String)
                {
                    // Handle JSON-string form: '["sizeDelta","anchoredPosition"]'
                    try
                    {
                        var parsed = JArray.Parse(propsToken.ToString());
                        propertiesFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in parsed)
                        {
                            string val = item?.ToString();
                            if (!string.IsNullOrEmpty(val))
                                propertiesFilter.Add(val);
                        }
                    }
                    catch { /* not valid JSON array — ignore */ }
                }
                // If properties filter is specified, implicitly enable component data
                if (propertiesFilter != null && propertiesFilter.Count > 0 && componentFilter == null)
                    componentFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Build hierarchy items with depth limit
                var allItems = BuildHierarchyItems(prefabContents.transform, sanitizedPath, componentFilter, includeInternal, maxDepth, propertiesFilter);
                int total = allItems.Count;

                // Apply pagination
                var pagedItems = allItems.Skip(cursor).Take(pageSize).ToList();
                int? nextCursor = (cursor + pageSize < total) ? cursor + pageSize : (int?)null;

                var result = new Dictionary<string, object>
                {
                    ["prefabPath"] = sanitizedPath,
                    ["total"] = total,
                    ["cursor"] = cursor,
                    ["page_size"] = pageSize,
                    ["items"] = pagedItems
                };
                if (nextCursor.HasValue)
                    result["next_cursor"] = nextCursor.Value;
                if (maxDepth < 50)
                    result["max_depth"] = maxDepth;

                string message = nextCursor.HasValue
                    ? $"Prefab hierarchy: showing {pagedItems.Count} of {total} objects (cursor={cursor}). More items available — use next_cursor={nextCursor.Value} to continue."
                    : $"Prefab hierarchy: {total} objects total.";

                return new SuccessResponse(message, result);
            }
            finally
            {
                // Always unload prefab contents to free memory
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        #endregion

        #region Headless Prefab Editing

        /// <summary>
        /// Modifies a prefab's contents directly without opening the prefab stage.
        /// This is ideal for automated/agentic workflows as it avoids UI, dirty flags, and dialogs.
        /// </summary>
        private static object ModifyContents(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for modify_contents.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return new ErrorResponse($"Invalid prefab path '{prefabPath}'. Path traversal sequences are not allowed.");
            }

            // Load prefab contents in isolated context (no UI)
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(sanitizedPath);
            if (prefabContents == null)
            {
                return new ErrorResponse($"Failed to load prefab contents from '{sanitizedPath}'.");
            }

            try
            {
                // Find target object within the prefab (defaults to root)
                string targetName = @params["target"]?.ToString();
                GameObject targetGo = FindInPrefabContents(prefabContents, targetName);

                if (targetGo == null)
                {
                    string searchedFor = string.IsNullOrEmpty(targetName) ? "root" : $"'{targetName}'";
                    return new ErrorResponse($"Target {searchedFor} not found in prefab '{sanitizedPath}'.");
                }

                // Apply modifications
                var modifyResult = ApplyModificationsToPrefabObject(targetGo, @params, prefabContents);
                if (modifyResult.error != null)
                {
                    return modifyResult.error;
                }

                // Skip saving when no modifications were made to avoid unnecessary asset writes
                if (!modifyResult.modified)
                {
                    return new SuccessResponse(
                        $"Prefab '{sanitizedPath}' is already up to date; no changes were applied.",
                        new
                        {
                            prefabPath = sanitizedPath,
                            targetName = targetGo.name,
                            modified = false
                        }
                    );
                }

                // Save the prefab
                bool success;
                PrefabUtility.SaveAsPrefabAsset(prefabContents, sanitizedPath, out success);

                if (!success)
                {
                    return new ErrorResponse($"Failed to save prefab asset at '{sanitizedPath}'.");
                }

                AssetDatabase.Refresh();

                McpLog.Info($"[ManagePrefabs] Successfully modified and saved prefab '{sanitizedPath}' (headless).");

                return new SuccessResponse(
                    $"Prefab '{sanitizedPath}' modified and saved successfully.",
                    new
                    {
                        prefabPath = sanitizedPath,
                        targetName = targetGo.name,
                        modified = modifyResult.modified,
                        transform = new
                        {
                            position = new { x = targetGo.transform.localPosition.x, y = targetGo.transform.localPosition.y, z = targetGo.transform.localPosition.z },
                            rotation = new { x = targetGo.transform.localEulerAngles.x, y = targetGo.transform.localEulerAngles.y, z = targetGo.transform.localEulerAngles.z },
                            scale = new { x = targetGo.transform.localScale.x, y = targetGo.transform.localScale.y, z = targetGo.transform.localScale.z }
                        },
                        componentTypes = PrefabUtilityHelper.GetComponentTypeNames(targetGo)
                    }
                );
            }
            finally
            {
                // Always unload prefab contents to free memory
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        /// <summary>
        /// Finds a GameObject within loaded prefab contents by name or path.
        /// </summary>
        private static GameObject FindInPrefabContents(GameObject prefabContents, string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                // Return root if no target specified
                return prefabContents;
            }

            // Try to find by path first (e.g., "Parent/Child/Target")
            if (target.Contains("/"))
            {
                Transform found = prefabContents.transform.Find(target);
                if (found != null)
                {
                    return found.gameObject;
                }

                // If path starts with root name, try without it
                if (target.StartsWith(prefabContents.name + "/"))
                {
                    string relativePath = target.Substring(prefabContents.name.Length + 1);
                    found = prefabContents.transform.Find(relativePath);
                    if (found != null)
                    {
                        return found.gameObject;
                    }
                }
            }

            // Check if target matches root name
            if (prefabContents.name == target)
            {
                return prefabContents;
            }

            // Search by name in hierarchy
            foreach (Transform t in prefabContents.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.name == target)
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies modifications to a GameObject within loaded prefab contents.
        /// Returns (modified: bool, error: ErrorResponse or null).
        /// </summary>
        private static (bool modified, ErrorResponse error) ApplyModificationsToPrefabObject(GameObject targetGo, JObject @params, GameObject prefabRoot)
        {
            bool modified = false;

            // Name change
            string newName = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(newName) && targetGo.name != newName)
            {
                // If renaming the root, this will affect the prefab asset name on save
                targetGo.name = newName;
                modified = true;
            }

            // Active state
            bool? setActive = @params["setActive"]?.ToObject<bool?>();
            if (setActive.HasValue && targetGo.activeSelf != setActive.Value)
            {
                targetGo.SetActive(setActive.Value);
                modified = true;
            }

            // Tag
            string tag = @params["tag"]?.ToString();
            if (tag != null && targetGo.tag != tag)
            {
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;
                try
                {
                    targetGo.tag = tagToSet;
                    modified = true;
                }
                catch (Exception ex)
                {
                    return (false, new ErrorResponse($"Failed to set tag to '{tagToSet}': {ex.Message}"));
                }
            }

            // Layer
            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1)
                {
                    return (false, new ErrorResponse($"Invalid layer specified: '{layerName}'. Use a valid layer name."));
                }
                if (targetGo.layer != layerId)
                {
                    targetGo.layer = layerId;
                    modified = true;
                }
            }

            // Transform: position, rotation, scale
            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? rotation = VectorParsing.ParseVector3(@params["rotation"]);
            Vector3? scale = VectorParsing.ParseVector3(@params["scale"]);

            if (position.HasValue && targetGo.transform.localPosition != position.Value)
            {
                targetGo.transform.localPosition = position.Value;
                modified = true;
            }
            if (rotation.HasValue && targetGo.transform.localEulerAngles != rotation.Value)
            {
                targetGo.transform.localEulerAngles = rotation.Value;
                modified = true;
            }
            if (scale.HasValue && targetGo.transform.localScale != scale.Value)
            {
                targetGo.transform.localScale = scale.Value;
                modified = true;
            }

            // Parent change (within prefab hierarchy)
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                string parentTarget = parentToken.ToString();
                Transform newParent = null;

                if (!string.IsNullOrEmpty(parentTarget))
                {
                    GameObject parentGo = FindInPrefabContents(prefabRoot, parentTarget);
                    if (parentGo == null)
                    {
                        return (false, new ErrorResponse($"Parent '{parentTarget}' not found in prefab."));
                    }
                    if (parentGo.transform.IsChildOf(targetGo.transform))
                    {
                        return (false, new ErrorResponse($"Cannot parent '{targetGo.name}' to '{parentGo.name}' as it would create a hierarchy loop."));
                    }
                    newParent = parentGo.transform;
                }

                if (targetGo.transform.parent != newParent)
                {
                    targetGo.transform.SetParent(newParent, true);
                    modified = true;
                }
            }

            // Components to add
            if (@params["componentsToAdd"] is JArray componentsToAdd)
            {
                foreach (var compToken in componentsToAdd)
                {
                    string typeName = compToken.Type == JTokenType.String
                        ? compToken.ToString()
                        : (compToken as JObject)?["typeName"]?.ToString();

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string error))
                        {
                            return (false, new ErrorResponse($"Component type '{typeName}' not found: {error}"));
                        }
                        targetGo.AddComponent(componentType);
                        modified = true;
                    }
                }
            }

            // Components to remove
            if (@params["componentsToRemove"] is JArray componentsToRemove)
            {
                foreach (var compToken in componentsToRemove)
                {
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string error))
                        {
                            return (false, new ErrorResponse($"Component type '{typeName}' not found: {error}"));
                        }
                        Component comp = targetGo.GetComponent(componentType);
                        if (comp != null)
                        {
                            UnityEngine.Object.DestroyImmediate(comp);
                            modified = true;
                        }
                    }
                }
            }

            // Create child GameObjects (supports single object or array)
            JToken createChildToken = @params["createChild"] ?? @params["create_child"];
            if (createChildToken != null)
            {
                // Handle array of children
                if (createChildToken is JArray childArray)
                {
                    foreach (var childToken in childArray)
                    {
                        var childResult = CreateSingleChildInPrefab(childToken, targetGo, prefabRoot);
                        if (childResult.error != null)
                        {
                            return (false, childResult.error);
                        }
                        if (childResult.created)
                        {
                            modified = true;
                        }
                    }
                }
                else
                {
                    // Handle single child object
                    var childResult = CreateSingleChildInPrefab(createChildToken, targetGo, prefabRoot);
                    if (childResult.error != null)
                    {
                        return (false, childResult.error);
                    }
                    if (childResult.created)
                    {
                        modified = true;
                    }
                }
            }

            // Set properties on a single component (component + properties shorthand)
            string singleComponentName = @params["component"]?.ToString();
            JObject singleProperties = @params["properties"] as JObject;
            if (!string.IsNullOrEmpty(singleComponentName) && singleProperties != null && singleProperties.HasValues)
            {
                if (!ComponentResolver.TryResolve(singleComponentName, out Type singleComponentType, out string singleResolveError))
                    return (false, new ErrorResponse($"Component type '{singleComponentName}' not found — {singleResolveError}"));

                Component singleComponent = targetGo.GetComponent(singleComponentType);
                if (singleComponent == null)
                    return (false, new ErrorResponse($"Component '{singleComponentName}' not found on '{targetGo.name}'"));

                var singleErrors = new List<string>();
                foreach (var prop in singleProperties.Properties())
                {
                    if (!ComponentOps.SetProperty(singleComponent, prop.Name, prop.Value, out string setError))
                        singleErrors.Add($"{singleComponentName}.{prop.Name}: {setError}");
                    else
                        modified = true;
                }
                if (singleErrors.Count > 0)
                    return (false, new ErrorResponse($"Failed to set component properties (no changes saved): {string.Join("; ", singleErrors)}"));
            }

            // Set properties on existing components
            JObject componentProperties = @params["componentProperties"] as JObject ?? @params["component_properties"] as JObject;
            if (componentProperties != null && componentProperties.Count > 0)
            {
                var errors = new List<string>();

                foreach (var entry in componentProperties.Properties())
                {
                    string typeName = entry.Name;
                    if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string resolveError))
                    {
                        errors.Add($"{typeName}: type not found — {resolveError}");
                        continue;
                    }

                    Component component = targetGo.GetComponent(componentType);
                    if (component == null)
                    {
                        errors.Add($"{typeName}: not found on '{targetGo.name}'");
                        continue;
                    }

                    if (entry.Value is not JObject props || !props.HasValues)
                    {
                        continue;
                    }

                    foreach (var prop in props.Properties())
                    {
                        if (!ComponentOps.SetProperty(component, prop.Name, prop.Value, out string setError))
                        {
                            errors.Add($"{typeName}.{prop.Name}: {setError}");
                        }
                        else
                        {
                            modified = true;
                        }
                    }
                }

                if (errors.Count > 0)
                {
                    return (false, new ErrorResponse($"Failed to set component properties (no changes saved): {string.Join("; ", errors)}"));
                }
            }

            return (modified, null);
        }

        /// <summary>
        /// Creates a single child GameObject within the prefab contents.
        /// </summary>
        private static (bool created, ErrorResponse error) CreateSingleChildInPrefab(JToken createChildToken, GameObject defaultParent, GameObject prefabRoot)
        {
            JObject childParams;
            if (createChildToken is JObject obj)
            {
                childParams = obj;
            }
            else
            {
                return (false, new ErrorResponse("'create_child' must be an object with child properties."));
            }

            // Required: name
            string childName = childParams["name"]?.ToString();
            if (string.IsNullOrEmpty(childName))
            {
                return (false, new ErrorResponse("'create_child.name' is required."));
            }

            // Optional: parent (defaults to the target object)
            string parentName = childParams["parent"]?.ToString();
            Transform parentTransform = defaultParent.transform;
            if (!string.IsNullOrEmpty(parentName))
            {
                GameObject parentGo = FindInPrefabContents(prefabRoot, parentName);
                if (parentGo == null)
                {
                    return (false, new ErrorResponse($"Parent '{parentName}' not found in prefab for create_child."));
                }
                parentTransform = parentGo.transform;
            }

            // Create the GameObject
            GameObject newChild;
            string primitiveType = childParams["primitiveType"]?.ToString() ?? childParams["primitive_type"]?.ToString();
            if (!string.IsNullOrEmpty(primitiveType))
            {
                try
                {
                    PrimitiveType type = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), primitiveType, true);
                    newChild = GameObject.CreatePrimitive(type);
                    newChild.name = childName;
                }
                catch (ArgumentException)
                {
                    return (false, new ErrorResponse($"Invalid primitive type: '{primitiveType}'. Valid types: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}"));
                }
            }
            else
            {
                newChild = new GameObject(childName);
            }

            // Set parent
            newChild.transform.SetParent(parentTransform, false);

            // Apply transform properties
            Vector3? position = VectorParsing.ParseVector3(childParams["position"]);
            Vector3? rotation = VectorParsing.ParseVector3(childParams["rotation"]);
            Vector3? scale = VectorParsing.ParseVector3(childParams["scale"]);

            if (position.HasValue)
            {
                newChild.transform.localPosition = position.Value;
            }
            if (rotation.HasValue)
            {
                newChild.transform.localEulerAngles = rotation.Value;
            }
            if (scale.HasValue)
            {
                newChild.transform.localScale = scale.Value;
            }

            // Add components
            JArray componentsToAdd = childParams["componentsToAdd"] as JArray ?? childParams["components_to_add"] as JArray;
            if (componentsToAdd != null)
            {
                for (int i = 0; i < componentsToAdd.Count; i++)
                {
                    var compToken = componentsToAdd[i];
                    string typeName = compToken.Type == JTokenType.String
                        ? compToken.ToString()
                        : (compToken as JObject)?["typeName"]?.ToString();

                    if (string.IsNullOrEmpty(typeName))
                    {
                        // Clean up partially created child
                        UnityEngine.Object.DestroyImmediate(newChild);
                        return (false, new ErrorResponse($"create_child.components_to_add[{i}] must be a string or object with 'typeName' field, got {compToken.Type}"));
                    }

                    if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string error))
                    {
                        // Clean up partially created child
                        UnityEngine.Object.DestroyImmediate(newChild);
                        return (false, new ErrorResponse($"Component type '{typeName}' not found for create_child: {error}"));
                    }
                    newChild.AddComponent(componentType);
                }
            }

            // Set tag if specified
            string tag = childParams["tag"]?.ToString();
            if (!string.IsNullOrEmpty(tag))
            {
                try
                {
                    newChild.tag = tag;
                }
                catch (Exception ex)
                {
                    UnityEngine.Object.DestroyImmediate(newChild);
                    return (false, new ErrorResponse($"Failed to set tag '{tag}' on child '{childName}': {ex.Message}"));
                }
            }

            // Set layer if specified
            string layerName = childParams["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1)
                {
                    UnityEngine.Object.DestroyImmediate(newChild);
                    return (false, new ErrorResponse($"Invalid layer '{layerName}' for child '{childName}'. Use a valid layer name."));
                }
                newChild.layer = layerId;
            }

            // Set active state
            bool? setActive = childParams["setActive"]?.ToObject<bool?>() ?? childParams["set_active"]?.ToObject<bool?>();
            if (setActive.HasValue)
            {
                newChild.SetActive(setActive.Value);
            }

            McpLog.Info($"[ManagePrefabs] Created child '{childName}' under '{parentTransform.name}' in prefab.");
            return (true, null);
        }

        #endregion

        #region Hierarchy Builder

        /// <summary>
        /// Builds a flat list of hierarchy items from a transform root.
        /// </summary>
        /// <param name="root">The root transform of the prefab.</param>
        /// <param name="mainPrefabPath">Asset path of the main prefab.</param>
        /// <returns>List of hierarchy items with prefab information.</returns>
        private static List<object> BuildHierarchyItems(Transform root, string mainPrefabPath, HashSet<string> componentFilter = null, bool includeInternal = false, int maxDepth = 50, HashSet<string> propertiesFilter = null)
        {
            var items = new List<object>();
            BuildHierarchyItemsRecursive(root, root, mainPrefabPath, "", items, componentFilter, includeInternal, 0, maxDepth, propertiesFilter);
            return items;
        }

        /// <summary>
        /// Recursively builds hierarchy items.
        /// </summary>
        /// <param name="transform">Current transform being processed.</param>
        /// <param name="mainPrefabRoot">Root transform of the main prefab asset.</param>
        /// <param name="mainPrefabPath">Asset path of the main prefab.</param>
        /// <param name="parentPath">Parent path for building full hierarchy path.</param>
        /// <param name="items">List to accumulate hierarchy items.</param>
        private static void BuildHierarchyItemsRecursive(Transform transform, Transform mainPrefabRoot, string mainPrefabPath, string parentPath, List<object> items, HashSet<string> componentFilter, bool includeInternal, int currentDepth, int maxDepth, HashSet<string> propertiesFilter = null)
        {
            if (transform == null) return;
            if (maxDepth > 0 && currentDepth > maxDepth) return;

            GameObject go = transform.gameObject;
            string name = go.name;
            string path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";
            int instanceId = go.GetInstanceID();
            bool activeSelf = go.activeSelf;
            int childCount = transform.childCount;
            var componentTypes = PrefabUtilityHelper.GetComponentTypeNames(go);

            // Prefab information
            bool isNestedPrefab = PrefabUtility.IsAnyPrefabInstanceRoot(go);
            bool isPrefabRoot = transform == mainPrefabRoot;
            int nestingDepth = isPrefabRoot ? 0 : PrefabUtilityHelper.GetPrefabNestingDepth(go, mainPrefabRoot);
            string parentPrefabPath = isNestedPrefab && !isPrefabRoot
                ? PrefabUtilityHelper.GetParentPrefabPath(go, mainPrefabRoot)
                : null;
            string nestedPrefabPath = isNestedPrefab ? PrefabUtilityHelper.GetNestedPrefabPath(go) : null;

            var item = new Dictionary<string, object>
            {
                ["name"] = name,
                ["instanceId"] = instanceId,
                ["path"] = path,
                ["activeSelf"] = activeSelf,
                ["childCount"] = childCount,
                ["componentTypes"] = componentTypes,
                ["prefab"] = new
                {
                    isRoot = isPrefabRoot,
                    isNestedRoot = isNestedPrefab,
                    nestingDepth = nestingDepth,
                    assetPath = isNestedPrefab ? nestedPrefabPath : mainPrefabPath,
                    parentPath = parentPrefabPath
                }
            };

            if (componentFilter != null)
            {
                item["components"] = SerializeComponentData(go, componentFilter, includeInternal, propertiesFilter);
            }

            items.Add(item);

            // Recursively process children
            foreach (Transform child in transform)
            {
                BuildHierarchyItemsRecursive(child, mainPrefabRoot, mainPrefabPath, path, items, componentFilter, includeInternal, currentDepth + 1, maxDepth, propertiesFilter);
            }
        }

        /// <summary>
        /// Parses the 'components' parameter: true/bool → empty HashSet (= all), list of strings → HashSet of those names, null → null (= off).
        /// Returns null if the parameter is absent/false, an empty HashSet for "all", or a populated HashSet for a filter list.
        /// </summary>
        private static HashSet<string> ParseComponentsParam(JObject @params)
        {
            var token = @params["components"];
            if (token == null) return null;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>() ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;

            if (token.Type == JTokenType.String)
            {
                string s = token.Value<string>();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
                    return null;
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { s };
            }

            if (token is JArray arr)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in arr)
                    if (item.Type == JTokenType.String)
                        set.Add(item.Value<string>());
                return set.Count > 0 ? set : null;
            }

            return null;
        }

        /// <summary>
        /// Serializes component data for a GameObject, optionally filtered by type names and properties.
        /// Empty filter = all components. Populated filter = only matching types.
        /// propertiesFilter narrows which properties are included per component (same as scene_object).
        /// </summary>
        private static List<object> SerializeComponentData(GameObject go, HashSet<string> filter, bool includeInternal = false, HashSet<string> propertiesFilter = null)
        {
            var list = new List<object>();
            bool filterAll = filter.Count == 0;

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                if (!filterAll && !filter.Contains(typeName))
                    continue;

                // If the caller explicitly requested specific properties, bypass internal filtering
                // so the agent gets exactly what it asked for. The propertiesFilter narrows the output anyway.
                bool effectiveIncludeInternal = (propertiesFilter != null && propertiesFilter.Count > 0) || includeInternal;
                var data = GameObjectSerializer.GetComponentData(comp, includeInternal: effectiveIncludeInternal);

                // Apply property-level filtering (same logic as SceneObject.SerializeComponents)
                if (propertiesFilter != null && propertiesFilter.Count > 0 && data is Dictionary<string, object> dataDict)
                {
                    if (dataDict.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> propsDict)
                    {
                        var filtered = new Dictionary<string, object>();
                        foreach (var key in propertiesFilter)
                        {
                            if (propsDict.TryGetValue(key, out var val))
                            {
                                filtered[key] = val;
                            }
                            else
                            {
                                var match = propsDict.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
                                if (match.Key != null)
                                    filtered[match.Key] = match.Value;
                            }
                        }
                        dataDict["properties"] = filtered;
                    }
                }

                list.Add(data);
            }
            return list;
        }

        /// <summary>
        /// Finds a child by name or slash-separated path within a loaded prefab asset (not prefab contents).
        /// </summary>
        private static Transform FindChildByNameOrPath(Transform root, string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return root;

            // Try path first
            Transform found = root.Find(nameOrPath);
            if (found != null) return found;

            // Fall back to recursive name search
            return FindChildRecursive(root, nameOrPath);
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        #endregion
    }
}
