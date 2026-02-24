using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers; // For Response class
using MCPForUnity.Runtime.Helpers; // For ScreenshotUtility
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles scene management operations like loading, saving, creating, and querying hierarchy.
    /// </summary>
    [McpForUnityTool("manage_scene", AutoRegister = false)]
    public static class ManageScene
    {
        private sealed class SceneCommand
        {
            public string action { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public string path { get; set; } = string.Empty;
            public int? buildIndex { get; set; }
            public string fileName { get; set; } = string.Empty;
            public int? superSize { get; set; }
            public bool synchronous { get; set; }

            // get_hierarchy paging + safety (summary-first)
            public JToken parent { get; set; }
            public int? pageSize { get; set; }
            public int? cursor { get; set; }
            public int? maxNodes { get; set; }
            public int? maxDepth { get; set; }
            public int? maxChildrenPerNode { get; set; }
            public bool? includeTransform { get; set; }
        }

        private static SceneCommand ToSceneCommand(JObject p)
        {
            if (p == null) return new SceneCommand();
            return new SceneCommand
            {
                action = (p["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant(),
                name = p["name"]?.ToString() ?? string.Empty,
                path = p["path"]?.ToString() ?? string.Empty,
                buildIndex = ParamCoercion.CoerceIntNullable(p["buildIndex"] ?? p["build_index"]),
                fileName = (p["fileName"] ?? p["filename"])?.ToString() ?? string.Empty,
                superSize = ParamCoercion.CoerceIntNullable(p["superSize"] ?? p["super_size"] ?? p["supersize"]),
                synchronous = ParamCoercion.CoerceBool(p["synchronous"] ?? p["sync"], false),

                // get_hierarchy paging + safety
                parent = p["parent"],
                pageSize = ParamCoercion.CoerceIntNullable(p["pageSize"] ?? p["page_size"]),
                cursor = ParamCoercion.CoerceIntNullable(p["cursor"]),
                maxNodes = ParamCoercion.CoerceIntNullable(p["maxNodes"] ?? p["max_nodes"]),
                maxDepth = ParamCoercion.CoerceIntNullable(p["maxDepth"] ?? p["max_depth"]),
                maxChildrenPerNode = ParamCoercion.CoerceIntNullable(p["maxChildrenPerNode"] ?? p["max_children_per_node"]),
                includeTransform = ParamCoercion.CoerceBoolNullable(p["includeTransform"] ?? p["include_transform"]),
            };
        }

        /// <summary>
        /// Main handler for scene management actions.
        /// </summary>
        public static async Task<object> HandleCommand(JObject @params)
        {
            try { McpLog.Info("[ManageScene] HandleCommand: start", always: false); } catch { }
            var cmd = ToSceneCommand(@params);
            string action = cmd.action;
            string name = string.IsNullOrEmpty(cmd.name) ? null : cmd.name;
            string path = string.IsNullOrEmpty(cmd.path) ? null : cmd.path; // Relative to Assets/
            int? buildIndex = cmd.buildIndex;
            // bool loadAdditive = @params["loadAdditive"]?.ToObject<bool>() ?? false; // Example for future extension

            // Ensure path is relative to Assets/, removing any leading "Assets/"
            string relativeDir = path ?? string.Empty;
            if (!string.IsNullOrEmpty(relativeDir))
            {
                relativeDir = AssetPathUtility.NormalizeSeparators(relativeDir).Trim('/');
                if (relativeDir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = relativeDir.Substring("Assets/".Length).TrimStart('/');
                }
            }

            // Apply default *after* sanitizing, using the original path variable for the check
            if (string.IsNullOrEmpty(path) && action == "create") // Check original path for emptiness
            {
                relativeDir = "Scenes"; // Default relative directory
            }

            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required.");
            }

            string sceneFileName = string.IsNullOrEmpty(name) ? null : $"{name}.unity";
            // Construct full system path correctly: ProjectRoot/Assets/relativeDir/sceneFileName
            string fullPathDir = Path.Combine(Application.dataPath, relativeDir); // Combine with Assets path (Application.dataPath ends in Assets)
            string fullPath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : Path.Combine(fullPathDir, sceneFileName);
            // Ensure relativePath always starts with "Assets/" and uses forward slashes
            string relativePath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : AssetPathUtility.NormalizeSeparators(Path.Combine("Assets", relativeDir, sceneFileName));

            // Ensure directory exists for 'create'
            if (action == "create" && !string.IsNullOrEmpty(fullPathDir))
            {
                try
                {
                    Directory.CreateDirectory(fullPathDir);
                }
                catch (Exception e)
                {
                    return new ErrorResponse(
                        $"Could not create directory '{fullPathDir}': {e.Message}"
                    );
                }
            }

            // Block actions that don't work in play mode
            if (EditorApplication.isPlaying &&
                (action == "create" || action == "load" || action == "save"))
            {
                return new ErrorResponse(
                    $"Cannot {action} scenes in play mode. Exit play mode first.");
            }

            // Route action
            try { McpLog.Info($"[ManageScene] Route action='{action}' name='{name}' path='{path}' buildIndex={(buildIndex.HasValue ? buildIndex.Value.ToString() : "null")}", always: false); } catch { }
            switch (action)
            {
                case "create":
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relativePath))
                        return new ErrorResponse(
                            "'name' and 'path' parameters are required for 'create' action."
                        );
                    return CreateScene(fullPath, relativePath);
                case "load":
                    // Loading can be done by path/name or build index
                    if (!string.IsNullOrEmpty(relativePath))
                        return LoadScene(relativePath);
                    else if (buildIndex.HasValue)
                        return LoadScene(buildIndex.Value);
                    else
                        return new ErrorResponse(
                            "Either 'name'/'path' or 'buildIndex' must be provided for 'load' action."
                        );
                case "save":
                    // Save current scene, optionally to a new path
                    return SaveScene(fullPath, relativePath);
                case "get_hierarchy":
                    try { McpLog.Info("[ManageScene] get_hierarchy: entering", always: false); } catch { }
                    var gh = GetSceneHierarchyPaged(cmd);
                    try { McpLog.Info("[ManageScene] get_hierarchy: exiting", always: false); } catch { }
                    return gh;
                case "get_active":
                    try { McpLog.Info("[ManageScene] get_active: entering", always: false); } catch { }
                    var ga = GetActiveSceneInfo();
                    try { McpLog.Info("[ManageScene] get_active: exiting", always: false); } catch { }
                    return ga;
                case "get_build_settings":
                    return GetBuildSettingsScenes();
                case "screenshot":
                    return await CaptureScreenshotAsync(cmd.fileName, cmd.superSize, cmd.synchronous);
                // Add cases for modifying build settings, additive loading, unloading etc.
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions: create, load, save, get_hierarchy, get_active, get_build_settings, screenshot."
                    );
            }
        }

        /// <summary>
        /// Captures a screenshot to Assets/Screenshots and returns a response payload.
        /// Public so the tools UI can reuse the same logic without duplicating parameters.
        /// Available in both Edit Mode and Play Mode.
        /// </summary>
        public static Task<object> ExecuteScreenshot(string fileName = null, int? superSize = null)
        {
            return CaptureScreenshotAsync(fileName, superSize);
        }

        private static object CreateScene(string fullPath, string relativePath)
        {
            if (File.Exists(fullPath))
            {
                return new ErrorResponse($"Scene already exists at '{relativePath}'.");
            }

            try
            {
                // Create a new empty scene
                Scene newScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
                // Save it to the specified path
                bool saved = EditorSceneManager.SaveScene(newScene, relativePath);

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); // Ensure Unity sees the new scene file
                    return new SuccessResponse(
                        $"Scene '{Path.GetFileName(relativePath)}' created successfully at '{relativePath}'.",
                        new { path = relativePath }
                    );
                }
                else
                {
                    // If SaveScene fails, it might leave an untitled scene open.
                    // Optionally try to close it, but be cautious.
                    return new ErrorResponse($"Failed to save new scene to '{relativePath}'.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error creating scene '{relativePath}': {e.Message}");
            }
        }

        private static object LoadScene(string relativePath)
        {
            if (
                !File.Exists(
                    Path.Combine(
                        Application.dataPath.Substring(
                            0,
                            Application.dataPath.Length - "Assets".Length
                        ),
                        relativePath
                    )
                )
            )
            {
                return new ErrorResponse($"Scene file not found at '{relativePath}'.");
            }

            // Check for unsaved changes in the current scene
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                // Optionally prompt the user or save automatically before loading
                return new ErrorResponse(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                );
                // Example: bool saveOK = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                // if (!saveOK) return new ErrorResponse("Load cancelled by user.");
            }

            try
            {
                EditorSceneManager.OpenScene(relativePath, OpenSceneMode.Single);
                return new SuccessResponse(
                    $"Scene '{relativePath}' loaded successfully.",
                    new
                    {
                        path = relativePath,
                        name = Path.GetFileNameWithoutExtension(relativePath),
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error loading scene '{relativePath}': {e.Message}");
            }
        }

        private static object LoadScene(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return new ErrorResponse(
                    $"Invalid build index: {buildIndex}. Must be between 0 and {SceneManager.sceneCountInBuildSettings - 1}."
                );
            }

            // Check for unsaved changes
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return new ErrorResponse(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                );
            }

            try
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return new SuccessResponse(
                    $"Scene at build index {buildIndex} ('{scenePath}') loaded successfully.",
                    new
                    {
                        path = scenePath,
                        name = Path.GetFileNameWithoutExtension(scenePath),
                        buildIndex = buildIndex,
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Error loading scene with build index {buildIndex}: {e.Message}"
                );
            }
        }

        private static object SaveScene(string fullPath, string relativePath)
        {
            try
            {
                Scene currentScene = EditorSceneManager.GetActiveScene();
                if (!currentScene.IsValid())
                {
                    return new ErrorResponse("No valid scene is currently active to save.");
                }

                bool saved;
                string finalPath = currentScene.path; // Path where it was last saved or will be saved

                if (!string.IsNullOrEmpty(relativePath) && currentScene.path != relativePath)
                {
                    // Save As...
                    // Ensure directory exists
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    saved = EditorSceneManager.SaveScene(currentScene, relativePath);
                    finalPath = relativePath;
                }
                else
                {
                    // Save (overwrite existing or save untitled)
                    if (string.IsNullOrEmpty(currentScene.path))
                    {
                        // Scene is untitled, needs a path
                        return new ErrorResponse(
                            "Cannot save an untitled scene without providing a 'name' and 'path'. Use Save As functionality."
                        );
                    }
                    saved = EditorSceneManager.SaveScene(currentScene);
                }

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return new SuccessResponse(
                        $"Scene '{currentScene.name}' saved successfully to '{finalPath}'.",
                        new { path = finalPath, name = currentScene.name }
                    );
                }
                else
                {
                    return new ErrorResponse($"Failed to save scene '{currentScene.name}'.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error saving scene: {e.Message}");
            }
        }

        private static async Task<object> CaptureScreenshotAsync(string fileName, int? superSize, bool synchronous = false)
        {
            try
            {
                int resolvedSuperSize = (superSize.HasValue && superSize.Value > 0) ? superSize.Value : 1;

                // Batch mode warning
                if (Application.isBatchMode)
                {
                    McpLog.Warn("[ManageScene] Screenshot capture in batch mode uses camera-based fallback. Results may vary.");
                }

                // Check Screen Capture module availability and warn if not available
                bool screenCaptureAvailable = ScreenshotUtility.IsScreenCaptureModuleAvailable;
                bool hasCameraFallback = Camera.main != null || UnityEngine.Object.FindObjectsByType<Camera>(UnityEngine.FindObjectsSortMode.None).Length > 0;

                // Use camera fallback only when ScreenCapture module is unavailable
                bool useCameraFallback = !screenCaptureAvailable;

#if UNITY_2022_1_OR_NEWER
                if (useCameraFallback && !hasCameraFallback)
                {
                    return new ErrorResponse(
                        "Cannot capture screenshot. The Screen Capture module is not enabled and no Camera was found in the scene. " +
                        "Please either: (1) Enable the Screen Capture module: Window > Package Manager > Built-in > Screen Capture > Enable, " +
                        "or (2) Add a Camera to your scene for camera-based fallback capture."
                    );
                }
                
                if (useCameraFallback)
                {
                    McpLog.Warn("[ManageScene] Screen Capture module not enabled. Using camera-based fallback. " +
                        "UI overlays (Screen Space - Overlay) will not appear. " +
                        "For best results, enable it: Window > Package Manager > Built-in > Screen Capture > Enable.");
                }
#else
                if (!hasCameraFallback)
                {
                    return new ErrorResponse(
                        "No camera found in the scene. Screenshot capture on Unity versions before 2022.1 requires a Camera in the scene. " +
                        "Please add a Camera to your scene or upgrade to Unity 2022.1+ for ScreenCapture API support."
                    );
                }
                useCameraFallback = true;
#endif

                // Best-effort: ensure Game View exists and repaints before capture.
                if (!useCameraFallback && !Application.isBatchMode)
                {
                    EnsureGameView();
                }

                ScreenshotCaptureResult result;
                if (useCameraFallback)
                {
                    result = ScreenshotUtility.CaptureWithCameraToAssetsFolder(fileName, resolvedSuperSize, ensureUniqueFileName: true);
                }
                else
                {
                    result = ScreenshotUtility.CaptureToAssetsFolder(fileName, resolvedSuperSize, ensureUniqueFileName: true);
                }

                if (result.IsAsync)
                {
                    // ScreenCapture.CaptureScreenshot writes at end of frame.
                    // Defer the MCP response until the file appears on disk.
                    await WaitForFileAsync(result.FullPath, TimeSpan.FromSeconds(10));
                    AssetDatabase.ImportAsset(result.AssetsRelativePath, ImportAssetOptions.ForceSynchronousImport);
                }
                else
                {
                    AssetDatabase.ImportAsset(result.AssetsRelativePath, ImportAssetOptions.ForceSynchronousImport);
                }

                return new SuccessResponse(
                    $"Screenshot captured to '{result.AssetsRelativePath}' (full: {result.FullPath}).",
                    new
                    {
                        path = result.AssetsRelativePath,
                        fullPath = result.FullPath,
                        superSize = result.SuperSize,
                        isAsync = false,
                    }
                );
            }
            catch (TimeoutException)
            {
                return new ErrorResponse("Screenshot capture timed out waiting for file to be written. The Game View may not have rendered a frame.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing screenshot: {e.Message}");
            }
        }

        private static void EnsureGameView()
        {
            // Repaint any existing Game View windows without stealing focus.
            // GetWindow was removed previously because it steals focus even with focus:false.
            // FindObjectsOfTypeAll finds existing windows without creating or focusing them.
            var gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType != null)
            {
                foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll(gameViewType))
                {
                    if (view is EditorWindow window)
                        window.Repaint();
                }
            }

            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        /// <summary>
        /// Waits for a file to appear on disk using EditorApplication.update polling.
        /// Defers the MCP response until the file is ready (used for ScreenCapture async writes).
        /// </summary>
        private static Task WaitForFileAsync(string fullPath, TimeSpan timeout)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var start = DateTime.UtcNow;

            void Tick()
            {
                try
                {
                    if (tcs.Task.IsCompleted)
                    {
                        EditorApplication.update -= Tick;
                        return;
                    }

                    if (File.Exists(fullPath))
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(true);
                        return;
                    }

                    if ((DateTime.UtcNow - start) > timeout)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetException(new TimeoutException(
                            $"Timed out after {timeout.TotalSeconds:F0}s waiting for screenshot file '{fullPath}'."));
                    }
                }
                catch (Exception ex)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetException(ex);
                }
            }

            EditorApplication.update += Tick;
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
            return tcs.Task;
        }

        private static object GetActiveSceneInfo()
        {
            try
            {
                try { McpLog.Info("[ManageScene] get_active: querying EditorSceneManager.GetActiveScene", always: false); } catch { }
                Scene activeScene = EditorSceneManager.GetActiveScene();
                try { McpLog.Info($"[ManageScene] get_active: got scene valid={activeScene.IsValid()} loaded={activeScene.isLoaded} name='{activeScene.name}'", always: false); } catch { }
                if (!activeScene.IsValid())
                {
                    return new ErrorResponse("No active scene found.");
                }

                var sceneInfo = new
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    buildIndex = activeScene.buildIndex, // -1 if not in build settings
                    isDirty = activeScene.isDirty,
                    isLoaded = activeScene.isLoaded,
                    rootCount = activeScene.rootCount,
                };

                return new SuccessResponse("Retrieved active scene information.", sceneInfo);
            }
            catch (Exception e)
            {
                try { McpLog.Error($"[ManageScene] get_active: exception {e.Message}"); } catch { }
                return new ErrorResponse($"Error getting active scene info: {e.Message}");
            }
        }

        private static object GetBuildSettingsScenes()
        {
            try
            {
                var scenes = new List<object>();
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    var scene = EditorBuildSettings.scenes[i];
                    scenes.Add(
                        new
                        {
                            path = scene.path,
                            guid = scene.guid.ToString(),
                            enabled = scene.enabled,
                            buildIndex = i, // Actual build index considering only enabled scenes might differ
                        }
                    );
                }
                return new SuccessResponse("Retrieved scenes from Build Settings.", scenes);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting scenes from Build Settings: {e.Message}");
            }
        }

        private static object GetSceneHierarchyPaged(SceneCommand cmd)
        {
            try
            {
                // Check Prefab Stage first
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                Scene activeScene;
                
                if (prefabStage != null)
                {
                    activeScene = prefabStage.scene;
                    try { McpLog.Info("[ManageScene] get_hierarchy: using Prefab Stage scene", always: false); } catch { }
                }
                else
                {
                    try { McpLog.Info("[ManageScene] get_hierarchy: querying EditorSceneManager.GetActiveScene", always: false); } catch { }
                    activeScene = EditorSceneManager.GetActiveScene();
                }
                
                try { McpLog.Info($"[ManageScene] get_hierarchy: got scene valid={activeScene.IsValid()} loaded={activeScene.isLoaded} name='{activeScene.name}'", always: false); } catch { }
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new ErrorResponse(
                        "No valid and loaded scene is active to get hierarchy from."
                    );
                }

                // Defaults tuned for safety; callers can override but we clamp to sane maxes.
                // NOTE: pageSize is "items per page", not "number of pages".
                // Keep this conservative to reduce peak response sizes when callers omit page_size.
                int resolvedPageSize = Mathf.Clamp(cmd.pageSize ?? 50, 1, 500);
                int resolvedCursor = Mathf.Max(0, cmd.cursor ?? 0);
                int resolvedMaxNodes = Mathf.Clamp(cmd.maxNodes ?? 1000, 1, 5000);
                int effectiveTake = Mathf.Min(resolvedPageSize, resolvedMaxNodes);
                int resolvedMaxChildrenPerNode = Mathf.Clamp(cmd.maxChildrenPerNode ?? 200, 0, 2000);
                bool includeTransform = cmd.includeTransform ?? false;

                // NOTE: maxDepth is accepted for forward-compatibility, but current paging mode
                // returns a single level (roots or direct children). This keeps payloads bounded.

                List<GameObject> nodes;
                string scope;

                GameObject parentGo = ResolveGameObject(cmd.parent, activeScene);
                if (cmd.parent == null || cmd.parent.Type == JTokenType.Null)
                {
                    try { McpLog.Info("[ManageScene] get_hierarchy: listing root objects (paged summary)", always: false); } catch { }
                    nodes = activeScene.GetRootGameObjects().Where(go => go != null).ToList();
                    scope = "roots";
                }
                else
                {
                    if (parentGo == null)
                    {
                        return new ErrorResponse($"Parent GameObject ('{cmd.parent}') not found.");
                    }
                    try { McpLog.Info($"[ManageScene] get_hierarchy: listing children of '{parentGo.name}' (paged summary)", always: false); } catch { }
                    nodes = new List<GameObject>(parentGo.transform.childCount);
                    foreach (Transform child in parentGo.transform)
                    {
                        if (child != null) nodes.Add(child.gameObject);
                    }
                    scope = "children";
                }

                int total = nodes.Count;
                if (resolvedCursor > total) resolvedCursor = total;
                int end = Mathf.Min(total, resolvedCursor + effectiveTake);

                var items = new List<object>(Mathf.Max(0, end - resolvedCursor));
                for (int i = resolvedCursor; i < end; i++)
                {
                    var go = nodes[i];
                    if (go == null) continue;
                    items.Add(BuildGameObjectSummary(go, includeTransform, resolvedMaxChildrenPerNode));
                }

                bool truncated = end < total;
                string nextCursor = truncated ? end.ToString() : null;

                var payload = new
                {
                    scope = scope,
                    cursor = resolvedCursor,
                    pageSize = effectiveTake,
                    next_cursor = nextCursor,
                    truncated = truncated,
                    total = total,
                    items = items,
                };

                var resp = new SuccessResponse($"Retrieved hierarchy page for scene '{activeScene.name}'.", payload);
                try { McpLog.Info("[ManageScene] get_hierarchy: success", always: false); } catch { }
                return resp;
            }
            catch (Exception e)
            {
                try { McpLog.Error($"[ManageScene] get_hierarchy: exception {e.Message}"); } catch { }
                return new ErrorResponse($"Error getting scene hierarchy: {e.Message}");
            }
        }

        private static GameObject ResolveGameObject(JToken targetToken, Scene activeScene)
        {
            if (targetToken == null || targetToken.Type == JTokenType.Null) return null;

            try
            {
                if (targetToken.Type == JTokenType.Integer || int.TryParse(targetToken.ToString(), out _))
                {
                    if (int.TryParse(targetToken.ToString(), out int id))
                    {
                        var obj = EditorUtility.EntityIdToObject(id);
                        if (obj is GameObject go) return go;
                        if (obj is Component c) return c.gameObject;
                    }
                }
            }
            catch { }

            string s = targetToken.ToString();
            if (string.IsNullOrEmpty(s)) return null;

            // Path-based find (e.g., "Root/Child/GrandChild")
            if (s.Contains("/"))
            {
                try
                {
                    var ids = GameObjectLookup.SearchGameObjects("by_path", s, includeInactive: true, maxResults: 1);
                    if (ids.Count > 0)
                    {
                        var byPath = GameObjectLookup.FindById(ids[0]);
                        if (byPath != null) return byPath;
                    }
                }
                catch { }
            }

            // Name-based find (first match, includes inactive)
            try
            {
                var all = activeScene.GetRootGameObjects();
                foreach (var root in all)
                {
                    if (root == null) continue;
                    if (root.name == s) return root;
                    var trs = root.GetComponentsInChildren<Transform>(includeInactive: true);
                    foreach (var t in trs)
                    {
                        if (t != null && t.gameObject != null && t.gameObject.name == s) return t.gameObject;
                    }
                }
            }
            catch { }

            return null;
        }

        private static object BuildGameObjectSummary(GameObject go, bool includeTransform, int maxChildrenPerNode)
        {
            if (go == null) return null;

            int childCount = 0;
            try { childCount = go.transform != null ? go.transform.childCount : 0; } catch { }
            bool childrenTruncated = childCount > 0; // We do not inline children in summary mode.

            // Get component type names (lightweight - no full serialization)
            var componentTypes = new List<string>();
            try
            {
                var components = go.GetComponents<Component>();
                if (components != null)
                {
                    foreach (var c in components)
                    {
                        if (c != null)
                        {
                            componentTypes.Add(c.GetType().Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[ManageScene] Failed to enumerate components for '{go.name}': {ex.Message}");
            }

            var d = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceID", go.GetInstanceID() },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "tag", go.tag },
                { "layer", go.layer },
                { "isStatic", go.isStatic },
                { "path", GetGameObjectPath(go) },
                { "childCount", childCount },
                { "childrenTruncated", childrenTruncated },
                { "childrenCursor", childCount > 0 ? "0" : null },
                { "childrenPageSizeDefault", maxChildrenPerNode },
                { "componentTypes", componentTypes },
            };

            if (includeTransform && go.transform != null)
            {
                var t = go.transform;
                d["transform"] = new
                {
                    position = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                    rotation = new[] { t.localRotation.eulerAngles.x, t.localRotation.eulerAngles.y, t.localRotation.eulerAngles.z },
                    scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z },
                };
            }

            return d;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;
            try
            {
                var names = new Stack<string>();
                Transform t = go.transform;
                while (t != null)
                {
                    names.Push(t.name);
                    t = t.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return go.name;
            }
        }

    }
}
