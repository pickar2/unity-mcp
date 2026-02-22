using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("scene_object", AutoRegister = false)]
    public static class SceneObject
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action", "get").ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list" => HandleList(@params, p),
                    "get" => HandleGet(@params, p),
                    "set" => HandleSet(@params, p),
                    "create" => HandleCreate(@params, p),
                    "delete" => HandleDelete(@params, p),
                    _ => new ErrorResponse($"Unknown action: '{action}'. Valid actions: list, get, set, create, delete.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[SceneObject] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region List Action

        private static object HandleList(JObject @params, ToolParams p)
        {
            var scene = GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return new ErrorResponse("No valid and loaded scene is active.");

            string targetRegex = p.Get("target_regex");
            string tag = p.Get("tag");
            string parent = p.Get("parent");
            string component = p.Get("component");
            bool includeInactive = p.GetBool("include_inactive", true);
            int depth = p.GetInt("depth", 1) ?? 1;
            
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
            pagination.PageSize = Mathf.Clamp(pagination.PageSize, 1, 500);

            var allObjects = new List<GameObject>();
            GameObject parentGo = null;

            if (!string.IsNullOrEmpty(parent))
            {
                parentGo = ResolveTarget(parent, false);
                if (parentGo == null)
                    return new ErrorResponse($"Parent '{parent}' not found.");
            }

            if (parentGo != null)
            {
                CollectChildren(parentGo.transform, allObjects, includeInactive, depth);
            }
            else
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (depth == 0)
                    {
                        CollectAllDescendants(root.transform, allObjects, includeInactive);
                    }
                    else if (depth >= 1)
                    {
                        CollectToDepth(root.transform, allObjects, includeInactive, depth);
                    }
                }
            }

            var filtered = ApplyFilters(allObjects, targetRegex, tag, component);

            var resultObjects = filtered
                .Skip(pagination.Cursor)
                .Take(pagination.PageSize)
                .Select(go => BuildObjectSummary(go))
                .ToList();

            bool hasMore = pagination.Cursor + pagination.PageSize < filtered.Count;
            int? nextCursor = hasMore ? pagination.Cursor + pagination.PageSize : null;

            return new SuccessResponse("Listed scene objects.", new
            {
                objects = resultObjects,
                total = filtered.Count,
                cursor = pagination.Cursor,
                page_size = pagination.PageSize,
                next_cursor = nextCursor,
                has_more = hasMore
            });
        }

        private static void CollectChildren(Transform parent, List<GameObject> list, bool includeInactive, int depth)
        {
            foreach (Transform child in parent)
            {
                if (!includeInactive && !child.gameObject.activeInHierarchy)
                    continue;
                list.Add(child.gameObject);
                if (depth != 1)
                {
                    CollectChildren(child, list, includeInactive, depth == 0 ? 0 : depth - 1);
                }
            }
        }

        private static void CollectToDepth(Transform parent, List<GameObject> list, bool includeInactive, int remainingDepth)
        {
            foreach (Transform child in parent)
            {
                if (!includeInactive && !child.gameObject.activeInHierarchy)
                    continue;
                list.Add(child.gameObject);
                if (remainingDepth > 1)
                {
                    CollectToDepth(child, list, includeInactive, remainingDepth - 1);
                }
            }
        }

        private static void CollectAllDescendants(Transform parent, List<GameObject> list, bool includeInactive)
        {
            foreach (Transform child in parent)
            {
                if (!includeInactive && !child.gameObject.activeInHierarchy)
                    continue;
                list.Add(child.gameObject);
                CollectAllDescendants(child, list, includeInactive);
            }
        }

        private static List<GameObject> ApplyFilters(List<GameObject> objects, string targetRegex, string tag, string component)
        {
            var result = objects;

            if (!string.IsNullOrEmpty(targetRegex))
            {
                try
                {
                    var regex = new Regex(targetRegex, RegexOptions.IgnoreCase);
                    result = result.Where(go => regex.IsMatch(GetGameObjectPath(go))).ToList();
                }
                catch (ArgumentException e)
                {
                    throw new Exception($"Invalid target_regex pattern: {e.Message}");
                }
            }

            if (!string.IsNullOrEmpty(tag))
            {
                result = result.Where(go => go.CompareTag(tag)).ToList();
            }

            if (!string.IsNullOrEmpty(component))
            {
                var componentType = UnityTypeResolver.ResolveComponent(component);
                if (componentType == null)
                    throw new Exception($"Component type '{component}' not found.");
                result = result.Where(go => go.GetComponent(componentType) != null).ToList();
            }

            return result;
        }

        private static object BuildObjectSummary(GameObject go)
        {
            return new
            {
                path = GetGameObjectPath(go),
                name = go.name,
                instance_id = go.GetInstanceID(),
                active = go.activeSelf,
                active_in_hierarchy = go.activeInHierarchy,
                tag = go.tag,
                layer = go.layer,
                component_types = go.GetComponents<Component>().Select(c => c?.GetType().Name).Where(n => n != null).ToList()
            };
        }

        #endregion

        #region Get Action

        private static object HandleGet(JObject @params, ToolParams p)
        {
            var targetToken = p.GetRaw("target");
            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for 'get' action.");

            bool includeComponents = p.GetBool("components", false);

            var resolveResult = ResolveTargetWithAmbiguity(targetToken);
            if (resolveResult.Error != null)
                return resolveResult.Error;

            var go = resolveResult.GameObject;
            
            return new SuccessResponse($"Retrieved object '{go.name}'.", BuildObjectDetail(go, includeComponents));
        }

        private static object BuildObjectDetail(GameObject go, bool includeComponents)
        {
            var t = go.transform;
            
            var result = new Dictionary<string, object>
            {
                ["path"] = GetGameObjectPath(go),
                ["name"] = go.name,
                ["instance_id"] = go.GetInstanceID(),
                ["active"] = go.activeSelf,
                ["active_in_hierarchy"] = go.activeInHierarchy,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["layer_name"] = LayerMask.LayerToName(go.layer),
                ["is_static"] = go.isStatic,
                ["transform"] = new
                {
                    position = new { x = t.position.x, y = t.position.y, z = t.position.z },
                    local_position = new { x = t.localPosition.x, y = t.localPosition.y, z = t.localPosition.z },
                    rotation = new { x = t.rotation.eulerAngles.x, y = t.rotation.eulerAngles.y, z = t.rotation.eulerAngles.z },
                    local_rotation = new { x = t.localRotation.eulerAngles.x, y = t.localRotation.eulerAngles.y, z = t.localRotation.eulerAngles.z },
                    scale = new { x = t.localScale.x, y = t.localScale.y, z = t.localScale.z }
                },
                ["parent"] = t.parent != null ? GetGameObjectPath(t.parent.gameObject) : null,
                ["children"] = t.Cast<Transform>().Select(c => GetGameObjectPath(c.gameObject)).ToList(),
                ["component_types"] = go.GetComponents<Component>().Select(c => c?.GetType().Name).Where(n => n != null).ToList()
            };

            if (includeComponents)
            {
                result["components"] = SerializeComponents(go);
            }

            return result;
        }

        private static List<object> SerializeComponents(GameObject go)
        {
            var list = new List<object>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                list.Add(PropertySerializer.SerializeComponent(comp));
            }
            return list;
        }

        #endregion

        #region Set Action

        private static object HandleSet(JObject @params, ToolParams p)
        {
            var targetToken = p.GetRaw("target");
            string targetRegex = p.Get("target_regex");
            string batchTag = p.Get("tag");
            string batchParent = p.Get("parent");

            bool isBatch = !string.IsNullOrEmpty(targetRegex) || !string.IsNullOrEmpty(batchTag) || !string.IsNullOrEmpty(batchParent);

            if (isBatch)
            {
                return HandleSetBatch(@params, p, targetRegex, batchTag, batchParent);
            }

            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for 'set' action.");

            var resolveResult = ResolveTargetWithAmbiguity(targetToken);
            if (resolveResult.Error != null)
                return resolveResult.Error;

            var go = resolveResult.GameObject;
            var changes = ApplySetProperties(go, @params, p);

            EditorUtility.SetDirty(go);
            MarkOwningSceneDirty(go);

            return new SuccessResponse($"Updated object '{go.name}'.", new
            {
                path = GetGameObjectPath(go),
                instance_id = go.GetInstanceID(),
                changes
            });
        }

        private static object HandleSetBatch(JObject @params, ToolParams p, string targetRegex, string batchTag, string batchParent)
        {
            var scene = GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return new ErrorResponse("No valid and loaded scene is active.");

            var targets = new List<GameObject>();

            if (!string.IsNullOrEmpty(targetRegex))
            {
                Regex regex;
                try
                {
                    regex = new Regex(targetRegex, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException e)
                {
                    return new ErrorResponse($"Invalid target_regex pattern: {e.Message}");
                }

                foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
                {
                    if (regex.IsMatch(GetGameObjectPath(go)))
                        targets.Add(go);
                }
            }
            else if (!string.IsNullOrEmpty(batchTag))
            {
                foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
                {
                    if (go.CompareTag(batchTag))
                        targets.Add(go);
                }
            }
            else if (!string.IsNullOrEmpty(batchParent))
            {
                var parentGo = ResolveTarget(batchParent, false);
                if (parentGo == null)
                    return new ErrorResponse($"Parent '{batchParent}' not found.");

                foreach (Transform child in parentGo.transform)
                {
                    targets.Add(child.gameObject);
                }
            }

            if (targets.Count == 0)
                return new SuccessResponse("No objects matched the criteria.", new { affected = new List<object>(), count = 0 });

            var affected = new List<object>();
            foreach (var go in targets)
            {
                ApplySetProperties(go, @params, p);
                EditorUtility.SetDirty(go);
                MarkOwningSceneDirty(go);
                affected.Add(new { path = GetGameObjectPath(go), instance_id = go.GetInstanceID() });
            }

            return new SuccessResponse($"Updated {affected.Count} objects.", new { affected, count = affected.Count });
        }

        private static List<string> ApplySetProperties(GameObject go, JObject @params, ToolParams p)
        {
            var changes = new List<string>();

            if (@params["name"] != null)
            {
                string newName = p.Get("name");
                if (!string.IsNullOrEmpty(newName) && go.name != newName)
                {
                    Undo.RecordObject(go, "Rename GameObject");
                    go.name = newName;
                    changes.Add("name");
                }
            }

            if (@params["active"] != null)
            {
                bool active = p.GetBool("active", go.activeSelf);
                if (go.activeSelf != active)
                {
                    Undo.RecordObject(go, "Set Active State");
                    go.SetActive(active);
                    changes.Add("active");
                }
            }

            var transform = go.transform;
            bool transformChanged = false;

            if (@params["position"] != null)
            {
                var pos = ParseVector3(@params["position"]);
                if (pos.HasValue && transform.position != pos.Value)
                {
                    if (!transformChanged) Undo.RecordObject(transform, "Set Position");
                    transform.position = pos.Value;
                    transformChanged = true;
                    changes.Add("position");
                }
            }

            if (@params["rotation"] != null)
            {
                var rot = ParseVector3(@params["rotation"]);
                if (rot.HasValue)
                {
                    var euler = Quaternion.Euler(rot.Value);
                    if (transform.rotation != euler)
                    {
                        if (!transformChanged) Undo.RecordObject(transform, "Set Rotation");
                        transform.rotation = euler;
                        transformChanged = true;
                        changes.Add("rotation");
                    }
                }
            }

            if (@params["scale"] != null)
            {
                var scale = ParseVector3(@params["scale"]);
                if (scale.HasValue && transform.localScale != scale.Value)
                {
                    if (!transformChanged) Undo.RecordObject(transform, "Set Scale");
                    transform.localScale = scale.Value;
                    transformChanged = true;
                    changes.Add("scale");
                }
            }

            string parentPath = p.Get("parent");
            if (!string.IsNullOrEmpty(parentPath))
            {
                var newParent = ResolveTarget(parentPath, false);
                if (newParent != null && transform.parent != newParent.transform)
                {
                    Undo.RecordObject(transform, "Reparent GameObject");
                    transform.SetParent(newParent.transform, true);
                    changes.Add("parent");
                }
            }

            string tag = p.Get("tag");
            if (!string.IsNullOrEmpty(tag) && go.tag != tag)
            {
                try
                {
                    go.tag = tag;
                    changes.Add("tag");
                }
                catch (UnityException e)
                {
                    McpLog.Warn($"[SceneObject] Failed to set tag: {e.Message}");
                }
            }

            var layerToken = @params["layer"];
            if (layerToken != null)
            {
                int layer;
                if (layerToken.Type == JTokenType.Integer)
                {
                    layer = layerToken.Value<int>();
                }
                else
                {
                    string layerName = layerToken.ToString();
                    layer = LayerMask.NameToLayer(layerName);
                }

                if (layer >= 0 && layer <= 31 && go.layer != layer)
                {
                    go.layer = layer;
                    changes.Add("layer");
                }
            }

            string componentName = p.Get("component");
            JObject properties = p.GetRaw("properties") as JObject;

            if (!string.IsNullOrEmpty(componentName) && properties != null)
            {
                var componentType = UnityTypeResolver.ResolveComponent(componentName);
                if (componentType != null)
                {
                    var comp = go.GetComponent(componentType);
                    if (comp != null)
                    {
                        Undo.RecordObject(comp, "Set Component Properties");
                        foreach (var prop in properties.Properties())
                        {
                            ComponentOps.SetProperty(comp, prop.Name, prop.Value, out _);
                        }
                        changes.Add($"component:{componentName}");
                    }
                }
            }

            return changes;
        }

        private static Vector3? ParseVector3(JToken token)
        {
            if (token == null) return null;

            if (token.Type == JTokenType.Array)
            {
                var arr = token as JArray;
                if (arr != null && arr.Count >= 3)
                {
                    return new Vector3(
                        arr[0].Value<float>(),
                        arr[1].Value<float>(),
                        arr[2].Value<float>()
                    );
                }
            }
            else if (token.Type == JTokenType.Object)
            {
                var obj = token as JObject;
                if (obj != null)
                {
                    return new Vector3(
                        obj["x"]?.Value<float>() ?? 0,
                        obj["y"]?.Value<float>() ?? 0,
                        obj["z"]?.Value<float>() ?? 0
                    );
                }
            }

            return null;
        }

        #endregion

        #region Create Action

        private static object HandleCreate(JObject @params, ToolParams p)
        {
            string name = p.Get("name");
            if (string.IsNullOrEmpty(name))
                return new ErrorResponse("'name' parameter is required for 'create' action.");

            string parentPath = p.Get("parent");
            string primitive = p.Get("primitive");

            GameObject newGo;

            if (!string.IsNullOrEmpty(primitive))
            {
                PrimitiveType? primitiveType = ParsePrimitiveType(primitive);
                if (primitiveType == null)
                    return new ErrorResponse($"Invalid primitive type: '{primitive}'. Valid types: Cube, Sphere, Capsule, Cylinder, Plane, Quad.");

                newGo = GameObject.CreatePrimitive(primitiveType.Value);
                newGo.name = name;
            }
            else
            {
                newGo = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(newGo, "Create GameObject");

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = ResolveTarget(parentPath, false);
                if (parent != null)
                {
                    newGo.transform.SetParent(parent.transform, false);
                }
            }

            var transform = newGo.transform;

            var position = ParseVector3(@params["position"]);
            if (position.HasValue)
                transform.position = position.Value;

            var rotation = ParseVector3(@params["rotation"]);
            if (rotation.HasValue)
                transform.rotation = Quaternion.Euler(rotation.Value);

            var scale = ParseVector3(@params["scale"]);
            if (scale.HasValue)
                transform.localScale = scale.Value;

            if (@params["active"] != null)
                newGo.SetActive(p.GetBool("active", true));

            string tag = p.Get("tag");
            if (!string.IsNullOrEmpty(tag))
            {
                try { newGo.tag = tag; } catch { }
            }

            var layerToken = @params["layer"];
            if (layerToken != null)
            {
                int layer = layerToken.Type == JTokenType.Integer
                    ? layerToken.Value<int>()
                    : LayerMask.NameToLayer(layerToken.ToString());
                if (layer >= 0 && layer <= 31)
                    newGo.layer = layer;
            }

            var componentsToken = p.GetRaw("components");
            if (componentsToken is JArray componentsArray)
            {
                foreach (var compToken in componentsArray)
                {
                    string compName = compToken.ToString();
                    var compType = UnityTypeResolver.ResolveComponent(compName);
                    if (compType != null)
                    {
                        ComponentOps.AddComponent(newGo, compType, out _);
                    }
                }
            }

            EditorUtility.SetDirty(newGo);
            MarkOwningSceneDirty(newGo);

            return new SuccessResponse($"Created object '{name}'.", new
            {
                path = GetGameObjectPath(newGo),
                name = newGo.name,
                instance_id = newGo.GetInstanceID()
            });
        }

        private static PrimitiveType? ParsePrimitiveType(string name)
        {
            return name.ToLowerInvariant() switch
            {
                "cube" => PrimitiveType.Cube,
                "sphere" => PrimitiveType.Sphere,
                "capsule" => PrimitiveType.Capsule,
                "cylinder" => PrimitiveType.Cylinder,
                "plane" => PrimitiveType.Plane,
                "quad" => PrimitiveType.Quad,
                _ => null
            };
        }

        #endregion

        #region Delete Action

        private static object HandleDelete(JObject @params, ToolParams p)
        {
            var targetToken = p.GetRaw("target");
            string targetRegex = p.Get("target_regex");
            string batchTag = p.Get("tag");
            string batchParent = p.Get("parent");

            bool isBatch = !string.IsNullOrEmpty(targetRegex) || !string.IsNullOrEmpty(batchTag) || !string.IsNullOrEmpty(batchParent);

            if (isBatch)
            {
                return HandleDeleteBatch(targetRegex, batchTag, batchParent);
            }

            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for 'delete' action.");

            var resolveResult = ResolveTargetWithAmbiguity(targetToken);
            if (resolveResult.Error != null)
                return resolveResult.Error;

            var go = resolveResult.GameObject;
            var path = GetGameObjectPath(go);
            var instanceId = go.GetInstanceID();

            Undo.DestroyObjectImmediate(go);

            return new SuccessResponse($"Deleted object '{path}'.", new
            {
                deleted = new[] { new { path, instance_id = instanceId } },
                count = 1
            });
        }

        private static object HandleDeleteBatch(string targetRegex, string batchTag, string batchParent)
        {
            var scene = GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return new ErrorResponse("No valid and loaded scene is active.");

            var targets = new List<GameObject>();

            if (!string.IsNullOrEmpty(targetRegex))
            {
                Regex regex;
                try
                {
                    regex = new Regex(targetRegex, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException e)
                {
                    return new ErrorResponse($"Invalid target_regex pattern: {e.Message}");
                }

                foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
                {
                    if (regex.IsMatch(GetGameObjectPath(go)))
                        targets.Add(go);
                }
            }
            else if (!string.IsNullOrEmpty(batchTag))
            {
                foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
                {
                    if (go.CompareTag(batchTag))
                        targets.Add(go);
                }
            }
            else if (!string.IsNullOrEmpty(batchParent))
            {
                var parentGo = ResolveTarget(batchParent, false);
                if (parentGo == null)
                    return new ErrorResponse($"Parent '{batchParent}' not found.");

                foreach (Transform child in parentGo.transform)
                {
                    targets.Add(child.gameObject);
                }
            }

            if (targets.Count == 0)
                return new SuccessResponse("No objects matched the criteria.", new { deleted = new List<object>(), count = 0 });

            var deleted = new List<object>();
            foreach (var go in targets)
            {
                deleted.Add(new { path = GetGameObjectPath(go), instance_id = go.GetInstanceID() });
                Undo.DestroyObjectImmediate(go);
            }

            return new SuccessResponse($"Deleted {deleted.Count} objects.", new { deleted, count = deleted.Count });
        }

        #endregion

        #region Target Resolution

        private class ResolveResult
        {
            public GameObject GameObject;
            public object Error;
        }

        private static ResolveResult ResolveTargetWithAmbiguity(JToken targetToken)
        {
            var result = new ResolveResult();

            if (targetToken.Type == JTokenType.Integer || int.TryParse(targetToken.ToString(), out _))
            {
                int id = targetToken.Type == JTokenType.Integer
                    ? targetToken.Value<int>()
                    : int.Parse(targetToken.ToString());

                result.GameObject = GameObjectLookup.FindById(id);
                if (result.GameObject == null)
                    result.Error = new ErrorResponse($"GameObject with instance ID {id} not found.");
                return result;
            }

            string targetStr = targetToken.ToString();

            if (targetStr.Contains("/"))
            {
                result.GameObject = FindByPath(targetStr);
                if (result.GameObject == null)
                    result.Error = new ErrorResponse($"GameObject at path '{targetStr}' not found.");
                return result;
            }

            var matches = new List<GameObject>();
            foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
            {
                if (go.name == targetStr)
                    matches.Add(go);
            }

            if (matches.Count == 0)
            {
                result.Error = new ErrorResponse($"GameObject with name '{targetStr}' not found.");
            }
            else if (matches.Count == 1)
            {
                result.GameObject = matches[0];
            }
            else
            {
                result.Error = new ErrorResponse($"Multiple objects match name '{targetStr}'", new
                {
                    matches = matches.Select(go => new
                    {
                        path = GetGameObjectPath(go),
                        instance_id = go.GetInstanceID()
                    }).ToList(),
                    hint = "Use full path or instance_id to specify which object"
                });
            }

            return result;
        }

        private static GameObject ResolveTarget(string target, bool allowAmbiguity)
        {
            if (int.TryParse(target, out int id))
                return GameObjectLookup.FindById(id);

            if (target.Contains("/"))
                return FindByPath(target);

            return GameObject.Find(target);
        }

        private static GameObject FindByPath(string path)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
                {
                    if (GetGameObjectPath(go) == path)
                        return go;
                }
                return null;
            }

            foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
            {
                if (GetGameObjectPath(go) == path)
                    return go;
            }
            return null;
        }

        #endregion

        #region Helpers

        private static Scene GetActiveScene()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                return prefabStage.scene;

            return EditorSceneManager.GetActiveScene();
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return string.Empty;
            return GameObjectLookup.GetGameObjectPath(obj);
        }

        private static void MarkOwningSceneDirty(GameObject targetGo)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            }
            else if (targetGo.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(targetGo.scene);
            }
        }

        #endregion
    }
}
