using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class SceneObjectCreateTests
    {
        private List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in createdObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            createdObjects.Clear();
        }

        private GameObject FindAndTrack(string name)
        {
            var go = GameObject.Find(name);
            if (go != null && !createdObjects.Contains(go))
                createdObjects.Add(go);
            return go;
        }

        #region Basic Create

        [Test]
        public void Create_WithNameOnly_CreatesEmptyGameObject()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjCreate_Empty"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjCreate_Empty");
            Assert.IsNotNull(created, "GameObject should be created");
            Assert.AreEqual("SceneObjCreate_Empty", created.name);
        }

        [Test]
        public void Create_WithoutName_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without name");
        }

        [Test]
        public void Create_WithEmptyName_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = ""
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail with empty name");
        }

        #endregion

        #region Primitive Types

        [Test]
        public void Create_PrimitiveCube_CreatesCubeWithMeshAndCollider()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjCube",
                ["primitive"] = "Cube"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjCube");
            Assert.IsNotNull(created);
            Assert.IsNotNull(created.GetComponent<MeshFilter>(), "Should have MeshFilter");
            Assert.IsNotNull(created.GetComponent<MeshRenderer>(), "Should have MeshRenderer");
            Assert.IsNotNull(created.GetComponent<BoxCollider>(), "Should have BoxCollider");
        }

        [Test]
        public void Create_PrimitiveSphere_CreatesSphere()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjSphere",
                ["primitive"] = "Sphere"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjSphere");
            Assert.IsNotNull(created);
            Assert.IsNotNull(created.GetComponent<SphereCollider>(), "Should have SphereCollider");
        }

        [Test]
        public void Create_InvalidPrimitive_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjBadPrimitive",
                ["primitive"] = "Hexagon"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for invalid primitive type");
        }

        #endregion

        #region Transform

        [Test]
        public void Create_WithPosition_SetsLocalPosition()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjPositioned",
                ["position"] = new JArray { 1f, 2f, 3f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjPositioned");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), created.transform.localPosition);
        }

        [Test]
        public void Create_WithRotation_SetsLocalEulerAngles()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjRotated",
                ["rotation"] = new JArray { 0f, 90f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjRotated");
            Assert.IsNotNull(created);
            Assert.AreEqual(90f, created.transform.localEulerAngles.y, 0.1f);
        }

        [Test]
        public void Create_WithScale_SetsLocalScale()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjScaled",
                ["scale"] = new JArray { 2f, 3f, 4f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjScaled");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), created.transform.localScale);
        }

        [Test]
        public void Create_UnderParent_PositionIsLocal()
        {
            var parent = new GameObject("CreateLocalParent");
            createdObjects.Add(parent);
            parent.transform.position = new Vector3(100, 0, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "CreateLocalChild",
                ["parent"] = "CreateLocalParent",
                ["position"] = new JArray { 5f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("CreateLocalChild");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), created.transform.localPosition,
                "Position should be local (relative to parent)");
        }

        #endregion

        #region Parenting

        [Test]
        public void Create_WithParent_SetsParentCorrectly()
        {
            var parent = new GameObject("SceneObjParent");
            createdObjects.Add(parent);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjChild",
                ["parent"] = "SceneObjParent"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var child = FindAndTrack("SceneObjChild");
            Assert.IsNotNull(child);
            Assert.AreEqual(parent.transform, child.transform.parent);
        }

        [Test]
        public void Create_WithParentByPath_SetsParent()
        {
            var root = new GameObject("CreateRoot");
            createdObjects.Add(root);
            var mid = new GameObject("CreateMid");
            createdObjects.Add(mid);
            mid.transform.SetParent(root.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "PathChild",
                ["parent"] = "/CreateRoot/CreateMid"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var child = FindAndTrack("PathChild");
            Assert.IsNotNull(child);
            Assert.AreEqual(mid.transform, child.transform.parent);
        }

        #endregion

        #region Tag and Layer

        [Test]
        public void Create_WithBuiltInTag_SetsTag()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjTagged",
                ["tag"] = "MainCamera"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjTagged");
            Assert.IsNotNull(created);
            Assert.AreEqual("MainCamera", created.tag);
        }

        [Test]
        public void Create_WithNewTag_AutoCreatesTag()
        {
            const string testTag = "SceneObjAutoTag54321";

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjAutoTagged",
                ["tag"] = testTag
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjAutoTagged");
            Assert.IsNotNull(created);
            Assert.AreEqual(testTag, created.tag);
            Assert.That(UnityEditorInternal.InternalEditorUtility.tags, Does.Contain(testTag));

            try { UnityEditorInternal.InternalEditorUtility.RemoveTag(testTag); } catch { }
        }

        [Test]
        public void Create_WithLayerByName_SetsLayer()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjLayered",
                ["layer"] = "UI"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjLayered");
            Assert.IsNotNull(created);
            Assert.AreEqual(LayerMask.NameToLayer("UI"), created.layer);
        }

        [Test]
        public void Create_WithLayerByNumber_SetsLayer()
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjLayerNum",
                ["layer"] = uiLayer
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjLayerNum");
            Assert.IsNotNull(created);
            Assert.AreEqual(uiLayer, created.layer);
        }

        [Test]
        public void Create_AsInactive_SetsActiveStateFalse()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjInactive",
                ["active"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Can't use FindAndTrack since it's inactive - use instance_id from response
            var instanceId = result["data"]?["instance_id"]?.Value<int>();
            Assert.IsNotNull(instanceId, "Response should include instance_id");

            var created = UnityEditor.EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
            if (created != null) createdObjects.Add(created);
            Assert.IsNotNull(created);
            Assert.IsFalse(created.activeSelf, "Object should be inactive");
        }

        #endregion

        #region Components

        [Test]
        public void Create_WithComponentStrings_AddsComponents()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjWithComps",
                ["components"] = new JArray { "Rigidbody", "BoxCollider" }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjWithComps");
            Assert.IsNotNull(created);
            Assert.IsNotNull(created.GetComponent<Rigidbody>(), "Should have Rigidbody");
            Assert.IsNotNull(created.GetComponent<BoxCollider>(), "Should have BoxCollider");
        }

        [Test]
        public void Create_WithComponentObjects_AddsComponentsWithProperties()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjCompProps",
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["typeName"] = "Rigidbody",
                        ["properties"] = new JObject { ["mass"] = 10f }
                    }
                }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var created = FindAndTrack("SceneObjCompProps");
            Assert.IsNotNull(created);
            var rb = created.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, "Should have Rigidbody");
            Assert.AreEqual(10f, rb.mass, 0.001f, "Mass should be set from properties");
        }

        #endregion

        #region Response Structure

        [Test]
        public void Create_Success_ReturnsPathNameAndInstanceId()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "create",
                ["name"] = "SceneObjResponseCheck"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"];
            Assert.IsNotNull(data, "Response should include data");
            Assert.IsNotNull(data["path"], "Data should include path");
            Assert.IsNotNull(data["name"], "Data should include name");
            Assert.AreEqual("SceneObjResponseCheck", data["name"]?.ToString());

            var instanceId = data["instance_id"]?.Value<int>();
            Assert.IsTrue(instanceId.HasValue && instanceId.Value != 0,
                "Data should include non-zero instance_id");

            FindAndTrack("SceneObjResponseCheck");
        }

        #endregion
    }
}
