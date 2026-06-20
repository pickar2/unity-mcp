using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class SceneObjectListGetTests
    {
        private List<GameObject> testObjects = new List<GameObject>();

        private GameObject CreateTestObject(string name)
        {
            var go = new GameObject(name);
            testObjects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in testObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            testObjects.Clear();
        }

        #region List Basic

        [Test]
        public void List_ReturnsRootObjects()
        {
            var go = CreateTestObject("ListTestRoot");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsNotNull(objects, "Response should include objects array");
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "ListTestRoot"),
                "Should find the test object");
        }

        [Test]
        public void List_ReturnsPathForEachObject()
        {
            var parent = CreateTestObject("ListParent");
            var child = CreateTestObject("ListChild");
            child.transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["path"]?.ToString().Contains("ListParent/ListChild") == true),
                "Should return full path for nested objects");
        }

        [Test]
        public void List_DefaultDepthOne_ReturnsOnlyRoots()
        {
            var parent = CreateTestObject("DepthParent");
            var child = new GameObject("DepthChild");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "DepthParent"),
                "Should find root object at depth 1");
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "DepthChild"),
                "Should not find child at depth 1");
        }

        [Test]
        public void List_DepthZero_ReturnsAllDescendants()
        {
            var parent = CreateTestObject("DeepParent");
            var child = new GameObject("DeepChild");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);
            var grandchild = new GameObject("DeepGrandchild");
            testObjects.Add(grandchild);
            grandchild.transform.SetParent(child.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "DeepGrandchild"),
                "Depth 0 should return all descendants");
        }

        [Test]
        public void List_DepthTwo_ReturnsRootsAndDirectChildren()
        {
            var parent = CreateTestObject("D2Parent");
            var child = new GameObject("D2Child");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);
            var grandchild = new GameObject("D2Grandchild");
            testObjects.Add(grandchild);
            grandchild.transform.SetParent(child.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["depth"] = 2
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "D2Child"),
                "Depth 2 should include children");
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "D2Grandchild"),
                "Depth 2 should not include grandchildren");
        }

        #endregion

        #region List Filters

        [Test]
        public void List_FilterByTag_ReturnsOnlyMatchingObjects()
        {
            var tagged = CreateTestObject("TaggedEnemy");
            tagged.tag = "MainCamera"; // Use built-in tag
            var untagged = CreateTestObject("UntaggedObj");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["tag"] = "MainCamera",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "TaggedEnemy"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "UntaggedObj"));
        }

        [Test]
        public void List_FilterByComponent_ReturnsOnlyMatchingObjects()
        {
            var withRb = CreateTestObject("WithRigidbody");
            withRb.AddComponent<Rigidbody>();
            var withoutRb = CreateTestObject("WithoutRigidbody");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["component"] = "Rigidbody",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "WithRigidbody"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "WithoutRigidbody"));
        }

        [Test]
        public void List_FilterByRegex_ReturnsOnlyMatchingPaths()
        {
            CreateTestObject("RegexTarget_A");
            CreateTestObject("RegexTarget_B");
            CreateTestObject("OtherObject");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["target_regex"] = ".*RegexTarget.*",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "RegexTarget_A"));
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "RegexTarget_B"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "OtherObject"));
        }

        [Test]
        public void List_FilterByParent_ReturnsOnlyChildren()
        {
            var parent = CreateTestObject("FilterParent");
            var child1 = new GameObject("FilterChild1");
            testObjects.Add(child1);
            child1.transform.SetParent(parent.transform);
            var child2 = new GameObject("FilterChild2");
            testObjects.Add(child2);
            child2.transform.SetParent(parent.transform);
            var other = CreateTestObject("FilterOther");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["parent"] = "FilterParent"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "FilterChild1"));
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "FilterChild2"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "FilterOther"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "FilterParent"),
                "Parent itself should not be in results");
        }

        [Test]
        public void List_FilterByLayer_ReturnsOnlyMatchingObjects()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            var onLayer = CreateTestObject("OnUILayer");
            onLayer.layer = uiLayer;
            var offLayer = CreateTestObject("NotOnUILayer");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["layer"] = "UI",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "OnUILayer"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "NotOnUILayer"));
        }

        [Test]
        public void List_FilterByLayerNumeric_ReturnsMatchingObjects()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            var onLayer = CreateTestObject("NumericLayerObj");
            onLayer.layer = uiLayer;

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["layer"] = uiLayer,
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "NumericLayerObj"));
        }

        [Test]
        public void List_InvalidRegex_ReturnsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(".*Invalid target_regex pattern.*"));
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["target_regex"] = "[invalid regex("
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Invalid regex should return error");
        }

        [Test]
        public void List_IncludeInactiveFalse_ExcludesInactiveObjects()
        {
            var active = CreateTestObject("ActiveListObj");
            var inactive = CreateTestObject("InactiveListObj");
            inactive.SetActive(false);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["include_inactive"] = false,
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.IsTrue(objects.Any(o => o["name"]?.ToString() == "ActiveListObj"));
            Assert.IsFalse(objects.Any(o => o["name"]?.ToString() == "InactiveListObj"));
        }

        [Test]
        public void List_NonexistentParent_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["parent"] = "NonExistentParent12345"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should error for nonexistent parent");
        }

        #endregion

        #region List Pagination

        [Test]
        public void List_Pagination_RespectsPageSize()
        {
            for (int i = 0; i < 5; i++)
                CreateTestObject($"PageObj_{i}");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["page_size"] = 2,
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            Assert.AreEqual(2, objects.Count, "Should return exactly page_size objects");
            Assert.IsTrue(result["data"]?["has_more"]?.Value<bool>() == true, "Should indicate more results");
        }

        [Test]
        public void List_Pagination_CursorAdvances()
        {
            for (int i = 0; i < 5; i++)
                CreateTestObject($"CursorObj_{i}");

            var page1 = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["page_size"] = 3,
                ["cursor"] = 0,
                ["depth"] = 0
            }));
            var page1Objects = page1["data"]?["objects"] as JArray;

            int nextCursor = page1["data"]?["next_cursor"]?.Value<int>() ?? 3;

            var page2 = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["page_size"] = 3,
                ["cursor"] = nextCursor,
                ["depth"] = 0
            }));
            var page2Objects = page2["data"]?["objects"] as JArray;

            // Pages should not overlap
            var page1Names = page1Objects.Select(o => o["name"]?.ToString()).ToHashSet();
            var page2Names = page2Objects.Select(o => o["name"]?.ToString()).ToHashSet();
            Assert.IsFalse(page1Names.Overlaps(page2Names), "Pages should not overlap");
        }

        [Test]
        public void List_ResponseIncludesTotalCount()
        {
            CreateTestObject("TotalCountObj");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var total = result["data"]?["total"]?.Value<int>();
            Assert.IsNotNull(total, "Response should include total count");
            Assert.IsTrue(total > 0, "Total should be positive");
        }

        #endregion

        #region List Object Summary

        [Test]
        public void List_ObjectSummaryIncludesExpectedFields()
        {
            var go = CreateTestObject("SummaryObj");
            go.tag = "MainCamera";
            go.AddComponent<Rigidbody>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["target_regex"] = ".*SummaryObj",
                ["depth"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var objects = result["data"]?["objects"] as JArray;
            var obj = objects.First(o => o["name"]?.ToString() == "SummaryObj");

            Assert.IsNotNull(obj["path"], "Summary should include path");
            Assert.IsNotNull(obj["name"], "Summary should include name");
            Assert.IsNotNull(obj["instance_id"], "Summary should include instance_id");
            Assert.IsNotNull(obj["active"], "Summary should include active");
            Assert.IsNotNull(obj["tag"], "Summary should include tag");
            Assert.IsNotNull(obj["component_types"], "Summary should include component_types");
        }

        #endregion

        #region Get Basic

        [Test]
        public void Get_ByName_ReturnsObjectDetails()
        {
            var go = CreateTestObject("GetTestObj");
            go.transform.localPosition = new Vector3(1, 2, 3);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "GetTestObj"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.AreEqual("GetTestObj", data?["name"]?.ToString());
        }

        [Test]
        public void Get_ByPath_ReturnsObjectDetails()
        {
            var parent = CreateTestObject("GetParent");
            var child = new GameObject("GetChild");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "/GetParent/GetChild"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("GetChild", result["data"]?["name"]?.ToString());
        }

        [Test]
        public void Get_ByInstanceId_ReturnsObjectDetails()
        {
            var go = CreateTestObject("GetByIdObj");
            int id = go.GetInstanceID();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = id
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("GetByIdObj", result["data"]?["name"]?.ToString());
        }

        [Test]
        public void Get_NonexistentTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "NonExistentObj12345"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for nonexistent object");
        }

        [Test]
        public void Get_WithoutTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without target");
        }

        #endregion

        #region Get Detail Fields

        [Test]
        public void Get_ReturnsTransformData()
        {
            var go = CreateTestObject("TransformDetailObj");
            go.transform.localPosition = new Vector3(10, 20, 30);
            go.transform.localEulerAngles = new Vector3(0, 90, 0);
            go.transform.localScale = new Vector3(2, 2, 2);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "TransformDetailObj"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var transform = result["data"]?["transform"];
            Assert.IsNotNull(transform, "Should include transform data");
            Assert.IsNotNull(transform["position"], "Transform should include position (local)");
            Assert.IsNotNull(transform["world_position"], "Transform should include world_position");
            Assert.IsNotNull(transform["scale"], "Transform should include scale");
        }

        [Test]
        public void Get_ReturnsParentAndChildren()
        {
            var parent = CreateTestObject("DetailParent");
            var child = new GameObject("DetailChild");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "DetailParent"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            var children = data?["children"] as JArray;
            Assert.IsNotNull(children, "Should include children array");
            Assert.IsTrue(children.Count > 0, "Should have at least one child");
        }

        [Test]
        public void Get_ReturnsComponentTypes()
        {
            var go = CreateTestObject("ComponentTypesObj");
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "ComponentTypesObj"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var types = result["data"]?["component_types"] as JArray;
            Assert.IsNotNull(types, "Should include component_types");
            var typeNames = types.Select(t => t.ToString()).ToList();
            Assert.That(typeNames, Does.Contain("Rigidbody"));
            Assert.That(typeNames, Does.Contain("BoxCollider"));
        }

        [Test]
        public void Get_WithComponentsFlag_ReturnsFullComponentData()
        {
            var go = CreateTestObject("FullComponentObj");
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 5f;

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "FullComponentObj",
                ["components"] = true
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components, "Should include full components data when flag is true");
            Assert.IsTrue(components.Count > 0, "Should have component entries");
        }

        [Test]
        public void Get_WithoutComponentsFlag_OmitsFullComponentData()
        {
            var go = CreateTestObject("NoComponentDataObj");
            go.AddComponent<Rigidbody>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "NoComponentDataObj",
                ["components"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsNull(data?["components"],
                "Should not include full components data when flag is false");
            Assert.IsNotNull(data?["component_types"],
                "Should still include component type names");
        }

        #endregion

        #region Get Ambiguity

        [Test]
        public void Get_AmbiguousName_ReturnsErrorWithMatches()
        {
            CreateTestObject("AmbiguousName");
            CreateTestObject("AmbiguousName");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "AmbiguousName"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for ambiguous name");
            var errorStr = result["error"]?.ToString() ?? result["message"]?.ToString() ?? "";
            Assert.That(errorStr, Does.Contain("Multiple").IgnoreCase,
                "Error should mention multiple matches");
        }

        [Test]
        public void Get_AmbiguousName_ErrorIncludesMatchList()
        {
            CreateTestObject("DuplicateName");
            CreateTestObject("DuplicateName");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "DuplicateName"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            var data = result["data"];
            Assert.IsNotNull(data?["matches"], "Error response should include matches list");
        }

        #endregion

        #region Get Components List Filter

        [Test]
        public void Get_ComponentsArray_FiltersToSpecifiedTypes()
        {
            var go = CreateTestObject("ComponentsFilterTest");
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "ComponentsFilterTest",
                ["components"] = new JArray("Rigidbody")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components, "Should include components array");
            Assert.AreEqual(1, components.Count, "Should only include Rigidbody, not BoxCollider");
            Assert.AreEqual("UnityEngine.Rigidbody", components[0]["typeName"]?.ToString());
        }

        [Test]
        public void Get_ComponentsArrayMultiple_FiltersToAllSpecified()
        {
            var go = CreateTestObject("MultiComponentsFilter");
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();
            go.AddComponent<AudioSource>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "MultiComponentsFilter",
                ["components"] = new JArray("Rigidbody", "BoxCollider")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components);
            Assert.AreEqual(2, components.Count, "Should include Rigidbody and BoxCollider only");
        }

        [Test]
        public void Get_ComponentsArrayAndComponentParam_MergesFilters()
        {
            var go = CreateTestObject("MergedFilter");
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "MergedFilter",
                ["components"] = new JArray("Rigidbody"),
                ["component"] = "BoxCollider"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.AreEqual(2, components.Count, "Should include both array and singular filter");
        }

        #endregion

        #region Get Properties List Filter

        [Test]
        public void Get_PropertiesArray_FiltersComponentProperties()
        {
            var go = CreateTestObject("PropsFilter");
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.useGravity = false;

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "PropsFilter",
                ["component"] = "Rigidbody",
                ["properties"] = new JArray("mass")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components);
            var props = components[0]["properties"] as JObject;
            Assert.IsNotNull(props);
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass' property");
            Assert.IsFalse(props.ContainsKey("useGravity"), "Should NOT include 'useGravity' — not in filter");
        }

        [Test]
        public void Get_PropertiesArrayMultiple_FiltersAllSpecified()
        {
            var go = CreateTestObject("MultiPropsFilter");
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.useGravity = true;
            rb.isKinematic = true;

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "MultiPropsFilter",
                ["component"] = "Rigidbody",
                ["properties"] = new JArray("mass", "isKinematic")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass'");
            Assert.IsTrue(props.ContainsKey("isKinematic"), "Should include 'isKinematic'");
            Assert.IsFalse(props.ContainsKey("useGravity"), "Should not include unfiltered property");
        }

        #endregion

        #region Get IncludeInternal

        // NOTE: Upstream's GameObjectSerializer.GetComponentData exposes only
        // includeNonPublicSerializedFields, not beta's separate includeInternal
        // filtering axis. The include_internal flag is still wired through the
        // Python tool and forwarded as includeInternal on the C# side, where it
        // is mapped to includeNonPublicSerializedFields. These tests confirm the
        // parameter is accepted without breaking the get action — they do NOT
        // assert the curated internal-property skip list, which is beta-only.

        [Test]
        public void Get_IncludeInternalDefault_AcceptedAndReturnsComponentData()
        {
            var go = CreateTestObject("InternalDefault");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "InternalDefault/Cube",
                ["component"] = "MeshRenderer"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props, "Component should expose its properties");
            Object.DestroyImmediate(cube);
        }

        [Test]
        public void Get_IncludeInternalTrue_AcceptedAndReturnsComponentData()
        {
            var go = CreateTestObject("InternalTrue");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "InternalTrue/Cube",
                ["component"] = "MeshRenderer",
                ["includeInternal"] = true
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props, "Component should expose its properties with includeInternal=true");
            Object.DestroyImmediate(cube);
        }

        #endregion
    }
}
