using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.GameObjects;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
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
                    "duplicate" => HandleDuplicate(@params, p),
                    "move_relative" => HandleMoveRelative(@params, p),
                    _ => new ErrorResponse($"Unknown action: '{action}'. Valid actions: list, get, set, create, delete, duplicate, move_relative.")
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
            string layer = p.Get("layer");
            bool includeInactive = p.GetBool("include_inactive", true);
            int depth = p.GetInt("depth", 1) ?? 1;

            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
            pagination.PageSize = Mathf.Clamp(pagination.PageSize, 1, 500);

            var allObjects = new List<GameObject>();
            GameObject parentGo = null;

            if (!string.IsNullOrEmpty(parent))
            {
                parentGo = ResolveTarget(parent);
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
                    if (!includeInactive && !root.activeInHierarchy)
                        continue;
                    allObjects.Add(root);
                    if (depth == 0)
                    {
                        CollectAllDescendants(root.transform, allObjects, includeInactive);
                    }
                    else if (depth > 1)
                    {
                        CollectToDepth(root.transform, allObjects, includeInactive, depth - 1);
                    }
                }
            }

            var filtered = ApplyFilters(allObjects, targetRegex, tag, component, layer);

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

        private static List<GameObject> ApplyFilters(List<GameObject> objects, string targetRegex, string tag, string component, string layer)
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
                result = result.Where(go =>
                {
                    try { return go.CompareTag(tag); }
                    catch { return false; }
                }).ToList();
            }

            if (!string.IsNullOrEmpty(component))
            {
                var componentType = UnityTypeResolver.ResolveComponent(component);
                if (componentType == null)
                    throw new Exception($"Component type '{component}' not found.");
                result = result.Where(go => go.GetComponent(componentType) != null).ToList();
            }

            if (!string.IsNullOrEmpty(layer))
            {
                int layerId;
                if (int.TryParse(layer, out layerId))
                {
                    // Numeric layer
                }
                else
                {
                    layerId = LayerMask.NameToLayer(layer);
                    if (layerId == -1)
                        throw new Exception($"Layer '{layer}' not found.");
                }
                result = result.Where(go => go.layer == layerId).ToList();
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
                list.Add(GameObjectSerializer.GetComponentData(comp));
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

            bool isBatch = targetToken == null && (!string.IsNullOrEmpty(targetRegex) || !string.IsNullOrEmpty(batchTag) || !string.IsNullOrEmpty(batchParent));

            if (isBatch)
            {
                return HandleSetBatch(@params, p, targetRegex, batchTag, batchParent);
            }

            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for 'set' action.");

            // When setting active=true, search inactive objects too
            var resolveResult = ResolveTargetWithAmbiguity(targetToken);
            if (resolveResult.Error != null)
                return resolveResult.Error;

            var go = resolveResult.GameObject;
            var setResult = ApplySetProperties(go, @params, p);
            if (setResult.Error != null)
                return setResult.Error;

            EditorUtility.SetDirty(go);
            MarkOwningSceneDirty(go);

            return new SuccessResponse($"Updated object '{go.name}'.", new
            {
                path = GetGameObjectPath(go),
                instance_id = go.GetInstanceID(),
                changes = setResult.Changes
            });
        }

        private static object HandleSetBatch(JObject @params, ToolParams p, string targetRegex, string batchTag, string batchParent)
        {
            var scene = GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return new ErrorResponse("No valid and loaded scene is active.");

            var targets = CollectBatchTargets(targetRegex, batchTag, batchParent);
            if (targets is ErrorResponse err)
                return err;

            var targetList = (List<GameObject>)targets;

            if (targetList.Count == 0)
                return new SuccessResponse("No objects matched the criteria.", new { affected = new List<object>(), count = 0 });

            var affected = new List<object>();
            var errors = new List<object>();
            foreach (var go in targetList)
            {
                var setResult = ApplySetProperties(go, @params, p);
                EditorUtility.SetDirty(go);
                MarkOwningSceneDirty(go);
                affected.Add(new { path = GetGameObjectPath(go), instance_id = go.GetInstanceID(), changes = setResult.Changes });
                if (setResult.Error is ErrorResponse err)
                    errors.Add(new { path = GetGameObjectPath(go), error = err.Error });
            }

            if (errors.Count > 0)
                return new SuccessResponse($"Updated {affected.Count} objects with {errors.Count} error(s).", new { affected, count = affected.Count, errors });
            return new SuccessResponse($"Updated {affected.Count} objects.", new { affected, count = affected.Count });
        }

        private class SetResult
        {
            public List<string> Changes = new List<string>();
            public object Error;
        }

        private static SetResult ApplySetProperties(GameObject go, JObject @params, ToolParams p)
        {
            var result = new SetResult();

            if (@params["name"] != null)
            {
                string newName = p.Get("name");
                if (!string.IsNullOrEmpty(newName) && go.name != newName)
                {
                    Undo.RecordObject(go, "Rename GameObject");
                    go.name = newName;
                    result.Changes.Add("name");
                }
            }

            if (@params["active"] != null)
            {
                bool active = p.GetBool("active", go.activeSelf);
                if (go.activeSelf != active)
                {
                    Undo.RecordObject(go, "Set Active State");
                    go.SetActive(active);
                    result.Changes.Add("active");
                }
            }

            var transform = go.transform;
            bool transformChanged = false;

            if (@params["position"] != null)
            {
                var pos = VectorParsing.ParseVector3(@params["position"]);
                if (pos.HasValue && transform.localPosition != pos.Value)
                {
                    if (!transformChanged) Undo.RecordObject(transform, "Set Transform");
                    transform.localPosition = pos.Value;
                    transformChanged = true;
                    result.Changes.Add("position");
                }
            }

            if (@params["rotation"] != null)
            {
                var rot = VectorParsing.ParseVector3(@params["rotation"]);
                if (rot.HasValue && transform.localEulerAngles != rot.Value)
                {
                    if (!transformChanged) Undo.RecordObject(transform, "Set Transform");
                    transform.localEulerAngles = rot.Value;
                    transformChanged = true;
                    result.Changes.Add("rotation");
                }
            }

            if (@params["scale"] != null)
            {
                var scale = VectorParsing.ParseVector3(@params["scale"]);
                if (scale.HasValue && transform.localScale != scale.Value)
                {
                    if (!transformChanged) Undo.RecordObject(transform, "Set Transform");
                    transform.localScale = scale.Value;
                    transformChanged = true;
                    result.Changes.Add("scale");
                }
            }

            // Reparent with circular parenting guard
            string parentPath = p.Get("parent");
            if (!string.IsNullOrEmpty(parentPath))
            {
                var newParent = ResolveTarget(parentPath);
                if (newParent != null)
                {
                    if (newParent.transform.IsChildOf(go.transform))
                    {
                        result.Error = new ErrorResponse(
                            $"Cannot parent '{go.name}' to '{newParent.name}', as it would create a hierarchy loop.");
                        return result;
                    }
                    if (transform.parent != newParent.transform)
                    {
                        Undo.RecordObject(transform, "Reparent GameObject");
                        transform.SetParent(newParent.transform, true);
                        result.Changes.Add("parent");
                    }
                }
            }

            // Tag with auto-creation
            string tag = p.Get("tag");
            if (!string.IsNullOrEmpty(tag) && go.tag != tag)
            {
                string tagToSet = tag;
                if (tagToSet != "Untagged" && !InternalEditorUtility.tags.Contains(tagToSet))
                {
                    try
                    {
                        InternalEditorUtility.AddTag(tagToSet);
                    }
                    catch (Exception ex)
                    {
                        result.Error = new ErrorResponse($"Failed to create tag '{tagToSet}': {ex.Message}");
                        return result;
                    }
                }

                try
                {
                    Undo.RecordObject(go, "Set Tag");
                    go.tag = tagToSet;
                    result.Changes.Add("tag");
                }
                catch (Exception ex)
                {
                    result.Error = new ErrorResponse($"Failed to set tag '{tagToSet}': {ex.Message}");
                    return result;
                }
            }

            var layerToken = @params["layer"];
            if (layerToken != null)
            {
                int layerId;
                if (layerToken.Type == JTokenType.Integer)
                {
                    layerId = layerToken.Value<int>();
                }
                else
                {
                    layerId = LayerMask.NameToLayer(layerToken.ToString());
                    if (layerId == -1)
                    {
                        result.Error = new ErrorResponse($"Invalid layer: '{layerToken}'. Use a valid layer name.");
                        return result;
                    }
                }

                if (layerId >= 0 && layerId <= 31 && go.layer != layerId)
                {
                    Undo.RecordObject(go, "Set Layer");
                    go.layer = layerId;
                    result.Changes.Add("layer");
                }
            }

            // Add components
            var addComponentsToken = p.GetRaw("add_components");
            if (addComponentsToken is JArray addArray)
            {
                foreach (var compToken in addArray)
                {
                    string typeName = null;
                    JObject compProps = null;

                    if (compToken.Type == JTokenType.String)
                    {
                        typeName = compToken.ToString();
                    }
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString() ?? compObj["type"]?.ToString();
                        compProps = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = GameObjectComponentHelpers.AddComponentInternal(go, typeName, compProps);
                        if (addResult != null)
                        {
                            result.Error = addResult;
                            return result;
                        }
                        result.Changes.Add($"add_component:{typeName}");
                    }
                }
            }

            // Remove components
            var removeComponentsToken = p.GetRaw("remove_components");
            if (removeComponentsToken is JArray removeArray)
            {
                foreach (var compToken in removeArray)
                {
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var removeResult = GameObjectComponentHelpers.RemoveComponentInternal(go, typeName);
                        if (removeResult != null)
                        {
                            result.Error = removeResult;
                            return result;
                        }
                        result.Changes.Add($"remove_component:{typeName}");
                    }
                }
            }

            // Set properties on a single component
            string componentName = p.Get("component");
            JObject properties = p.GetRaw("properties") as JObject;

            if (!string.IsNullOrEmpty(componentName) && properties != null)
            {
                var setResult = GameObjectComponentHelpers.SetComponentPropertiesInternal(go, componentName, properties);
                if (setResult != null)
                {
                    result.Error = setResult;
                    return result;
                }
                result.Changes.Add($"component:{componentName}");
            }

            // Set properties on multiple components via component_properties dict
            var componentPropertiesToken = p.GetRaw("component_properties");
            if (componentPropertiesToken is JObject componentPropsObj)
            {
                foreach (var prop in componentPropsObj.Properties())
                {
                    if (prop.Value is JObject propValues)
                    {
                        var setResult = GameObjectComponentHelpers.SetComponentPropertiesInternal(go, prop.Name, propValues);
                        if (setResult != null)
                        {
                            result.Error = setResult;
                            return result;
                        }
                        result.Changes.Add($"component:{prop.Name}");
                    }
                }
            }

            return result;
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
                var parent = ResolveTarget(parentPath);
                if (parent == null)
                {
                    Undo.DestroyObjectImmediate(newGo);
                    return new ErrorResponse($"Parent '{parentPath}' not found.");
                }
                newGo.transform.SetParent(parent.transform, false);
            }

            var transform = newGo.transform;

            var position = VectorParsing.ParseVector3(@params["position"]);
            if (position.HasValue)
                transform.localPosition = position.Value;

            var rotation = VectorParsing.ParseVector3(@params["rotation"]);
            if (rotation.HasValue)
                transform.localEulerAngles = rotation.Value;

            var scale = VectorParsing.ParseVector3(@params["scale"]);
            if (scale.HasValue)
                transform.localScale = scale.Value;

            if (@params["active"] != null)
                newGo.SetActive(p.GetBool("active", true));

            // Tag with auto-creation
            string tag = p.Get("tag");
            if (!string.IsNullOrEmpty(tag))
            {
                if (tag != "Untagged" && !InternalEditorUtility.tags.Contains(tag))
                {
                    try { InternalEditorUtility.AddTag(tag); }
                    catch (Exception ex)
                    {
                        Undo.DestroyObjectImmediate(newGo);
                        return new ErrorResponse($"Failed to create tag '{tag}': {ex.Message}");
                    }
                }
                try { newGo.tag = tag; }
                catch (Exception ex)
                {
                    Undo.DestroyObjectImmediate(newGo);
                    return new ErrorResponse($"Failed to set tag '{tag}': {ex.Message}");
                }
            }

            var layerToken = @params["layer"];
            if (layerToken != null)
            {
                int layerId = layerToken.Type == JTokenType.Integer
                    ? layerToken.Value<int>()
                    : LayerMask.NameToLayer(layerToken.ToString());
                if (layerId < 0 || layerId > 31)
                {
                    Undo.DestroyObjectImmediate(newGo);
                    return new ErrorResponse($"Invalid layer: '{layerToken}'. Use a valid layer name or number 0-31.");
                }
                newGo.layer = layerId;
            }

            // Components: accept strings or {typeName, properties} objects
            var componentsToken = p.GetRaw("components");
            if (componentsToken is JArray componentsArray)
            {
                foreach (var compToken in componentsArray)
                {
                    string typeName = null;
                    JObject compProps = null;

                    if (compToken.Type == JTokenType.String)
                    {
                        typeName = compToken.ToString();
                    }
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString() ?? compObj["type"]?.ToString();
                        compProps = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = GameObjectComponentHelpers.AddComponentInternal(newGo, typeName, compProps);
                        if (addResult != null)
                        {
                            Undo.DestroyObjectImmediate(newGo);
                            return addResult;
                        }
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

            bool isBatch = targetToken == null && (!string.IsNullOrEmpty(targetRegex) || !string.IsNullOrEmpty(batchTag) || !string.IsNullOrEmpty(batchParent));

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

            var targetsObj = CollectBatchTargets(targetRegex, batchTag, batchParent);
            if (targetsObj is ErrorResponse err)
                return err;

            var targets = (List<GameObject>)targetsObj;

            if (targets.Count == 0)
                return new SuccessResponse("No objects matched the criteria.", new { deleted = new List<object>(), count = 0 });

            // Sort children before parents to avoid accessing destroyed objects
            targets.Sort((a, b) => GetGameObjectPath(b).Count(c => c == '/') - GetGameObjectPath(a).Count(c => c == '/'));

            var deleted = new List<object>();
            foreach (var go in targets)
            {
                if (go == null) continue;
                deleted.Add(new { path = GetGameObjectPath(go), instance_id = go.GetInstanceID() });
                Undo.DestroyObjectImmediate(go);
            }

            return new SuccessResponse($"Deleted {deleted.Count} objects.", new { deleted, count = deleted.Count });
        }

        #endregion

        #region Duplicate Action

        private static object HandleDuplicate(JObject @params, ToolParams p)
        {
            var targetToken = p.GetRaw("target");
            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for 'duplicate' action.");

            var resolveResult = ResolveTargetWithAmbiguity(targetToken);
            if (resolveResult.Error != null)
                return resolveResult.Error;

            var sourceGo = resolveResult.GameObject;
            string newName = p.Get("name");
            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? offset = VectorParsing.ParseVector3(@params["offset"]);
            string parentPath = p.Get("parent");

            GameObject duplicatedGo = UnityEngine.Object.Instantiate(sourceGo);
            Undo.RegisterCreatedObjectUndo(duplicatedGo, $"Duplicate {sourceGo.name}");

            duplicatedGo.name = !string.IsNullOrEmpty(newName)
                ? newName
                : sourceGo.name.Replace("(Clone)", "").Trim() + "_Copy";

            if (position.HasValue)
            {
                duplicatedGo.transform.localPosition = position.Value;
            }
            else if (offset.HasValue)
            {
                duplicatedGo.transform.position = sourceGo.transform.position + offset.Value;
            }

            if (!string.IsNullOrEmpty(parentPath))
            {
                var newParent = ResolveTarget(parentPath);
                if (newParent != null)
                    duplicatedGo.transform.SetParent(newParent.transform, true);
            }
            else
            {
                duplicatedGo.transform.SetParent(sourceGo.transform.parent, true);
            }

            EditorUtility.SetDirty(duplicatedGo);
            MarkOwningSceneDirty(duplicatedGo);

            return new SuccessResponse($"Duplicated '{sourceGo.name}' as '{duplicatedGo.name}'.", new
            {
                source = new { path = GetGameObjectPath(sourceGo), instance_id = sourceGo.GetInstanceID() },
                duplicate = new
                {
                    path = GetGameObjectPath(duplicatedGo),
                    name = duplicatedGo.name,
                    instance_id = duplicatedGo.GetInstanceID()
                }
            });
        }

        #endregion

        #region Move Relative Action

        private static object HandleMoveRelative(JObject @params, ToolParams p)
        {
            var targetToken = p.GetRaw("target");
            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required for 'move_relative' action.");

            var resolveResult = ResolveTargetWithAmbiguity(targetToken);
            if (resolveResult.Error != null)
                return resolveResult.Error;

            var targetGo = resolveResult.GameObject;

            string refStr = p.Get("reference");
            if (string.IsNullOrEmpty(refStr))
                return new ErrorResponse("'reference' parameter is required for 'move_relative' action.");

            var refGo = ResolveTarget(refStr);
            if (refGo == null)
                return new ErrorResponse($"Reference object '{refStr}' not found.");

            string direction = p.Get("direction");
            float distance = p.GetFloat("distance") ?? 1f;
            Vector3? customOffset = VectorParsing.ParseVector3(@params["offset"]);
            bool useWorldSpace = p.GetBool("world_space", true);

            Undo.RecordObject(targetGo.transform, $"Move {targetGo.name} relative to {refGo.name}");

            Vector3 newPosition;

            if (customOffset.HasValue)
            {
                newPosition = useWorldSpace
                    ? refGo.transform.position + customOffset.Value
                    : refGo.transform.TransformPoint(customOffset.Value);
            }
            else if (!string.IsNullOrEmpty(direction))
            {
                string dirLower = direction.ToLowerInvariant();
                var validDirections = new HashSet<string> { "right", "left", "up", "down", "forward", "front", "back", "backward", "behind" };
                if (!validDirections.Contains(dirLower))
                    return new ErrorResponse($"Invalid direction: '{direction}'. Valid: right, left, up, down, forward, front, back, backward, behind.");
                Vector3 dirVector = GetDirectionVector(dirLower, refGo.transform, useWorldSpace);
                newPosition = refGo.transform.position + dirVector * distance;
            }
            else
            {
                return new ErrorResponse("Either 'direction' or 'offset' parameter is required for 'move_relative' action.");
            }

            targetGo.transform.position = newPosition;

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new SuccessResponse($"Moved '{targetGo.name}' relative to '{refGo.name}'.", new
            {
                path = GetGameObjectPath(targetGo),
                instance_id = targetGo.GetInstanceID(),
                new_position = new { x = targetGo.transform.position.x, y = targetGo.transform.position.y, z = targetGo.transform.position.z }
            });
        }

        private static Vector3 GetDirectionVector(string direction, Transform referenceTransform, bool useWorldSpace)
        {
            if (useWorldSpace)
            {
                return direction switch
                {
                    "right" => Vector3.right,
                    "left" => Vector3.left,
                    "up" => Vector3.up,
                    "down" => Vector3.down,
                    "forward" or "front" => Vector3.forward,
                    "back" or "backward" or "behind" => Vector3.back,
                    _ => Vector3.forward
                };
            }

            return direction switch
            {
                "right" => referenceTransform.right,
                "left" => -referenceTransform.right,
                "up" => referenceTransform.up,
                "down" => -referenceTransform.up,
                "forward" or "front" => referenceTransform.forward,
                "back" or "backward" or "behind" => -referenceTransform.forward,
                _ => referenceTransform.forward
            };
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

        private static GameObject ResolveTarget(string target)
        {
            if (int.TryParse(target, out int id))
                return GameObjectLookup.FindById(id);

            if (target.Contains("/"))
                return FindByPath(target);

            // Search all objects including inactive (GameObject.Find only finds active)
            foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
            {
                if (go.name == target)
                    return go;
            }
            return null;
        }

        private static GameObject FindByPath(string path)
        {
            foreach (var go in GameObjectLookup.GetAllSceneObjects(true))
            {
                if (GetGameObjectPath(go) == path)
                    return go;
            }
            return null;
        }

        #endregion

        #region Shared Helpers

        private static object CollectBatchTargets(string targetRegex, string batchTag, string batchParent)
        {
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
                    try
                    {
                        if (go.CompareTag(batchTag))
                            targets.Add(go);
                    }
                    catch { }
                }
            }
            else if (!string.IsNullOrEmpty(batchParent))
            {
                var parentGo = ResolveTarget(batchParent);
                if (parentGo == null)
                    return new ErrorResponse($"Parent '{batchParent}' not found.");

                foreach (Transform child in parentGo.transform)
                {
                    targets.Add(child.gameObject);
                }
            }

            return targets;
        }

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
