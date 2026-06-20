using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class SceneObjectSetTests
    {
        private List<GameObject> testObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("SetTestObject");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            testObjects.Add(go);
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

        private GameObject CreateTestObject(string name)
        {
            var go = new GameObject(name);
            testObjects.Add(go);
            return go;
        }

        #region Target Resolution

        [Test]
        public void Set_ByName_FindsAndModifiesObject()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["position"] = new JArray { 10f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(new Vector3(10f, 0f, 0f), testObjects[0].transform.localPosition);
        }

        [Test]
        public void Set_ByInstanceId_FindsAndModifiesObject()
        {
            int id = testObjects[0].GetInstanceID();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = id,
                ["position"] = new JArray { 20f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(new Vector3(20f, 0f, 0f), testObjects[0].transform.localPosition);
        }

        [Test]
        public void Set_ByPath_FindsAndModifiesObject()
        {
            var parent = CreateTestObject("SetPathParent");
            testObjects[0].transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "/SetPathParent/SetTestObject",
                ["position"] = new JArray { 5f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(new Vector3(5f, 0f, 0f), testObjects[0].transform.localPosition);
        }

        [Test]
        public void Set_NonexistentTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "NonExistentObj12345",
                ["active"] = false
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for nonexistent object");
        }

        [Test]
        public void Set_WithoutTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["active"] = false
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without target");
        }

        #endregion

        #region Transform (Local Coordinates)

        [Test]
        public void Set_Position_SetsLocalPosition()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["position"] = new JArray { 1f, 2f, 3f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(new Vector3(1f, 2f, 3f), testObjects[0].transform.localPosition);
        }

        [Test]
        public void Set_Rotation_SetsLocalEulerAngles()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["rotation"] = new JArray { 0f, 90f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(90f, testObjects[0].transform.localEulerAngles.y, 0.1f);
        }

        [Test]
        public void Set_Scale_SetsLocalScale()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["scale"] = new JArray { 2f, 3f, 4f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(new Vector3(2f, 3f, 4f), testObjects[0].transform.localScale);
        }

        [Test]
        public void Set_PositionUnderParent_IsLocalNotWorld()
        {
            var parent = CreateTestObject("SetLocalParent");
            parent.transform.position = new Vector3(100, 0, 0);
            testObjects[0].transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = testObjects[0].GetInstanceID(),
                ["position"] = new JArray { 5f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(new Vector3(5f, 0f, 0f), testObjects[0].transform.localPosition,
                "Position should be set as local coordinates");
            Assert.AreEqual(105f, testObjects[0].transform.position.x, 0.01f,
                "World position should be parent + local");
        }

        #endregion

        #region Name, Active, Tag, Layer

        [Test]
        public void Set_Name_RenamesObject()
        {
            int id = testObjects[0].GetInstanceID();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = id,
                ["name"] = "RenamedSetObj"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("RenamedSetObj", testObjects[0].name);
        }

        [Test]
        public void Set_ActiveFalse_DeactivatesObject()
        {
            Assert.IsTrue(testObjects[0].activeSelf);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["active"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(testObjects[0].activeSelf);
        }

        [Test]
        public void Set_ActiveTrue_ActivatesObject()
        {
            testObjects[0].SetActive(false);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = testObjects[0].GetInstanceID(),
                ["active"] = true
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(testObjects[0].activeSelf);
        }

        [Test]
        public void Set_Tag_SetsTag()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["tag"] = "MainCamera"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("MainCamera", testObjects[0].tag);
        }

        [Test]
        public void Set_NewTag_AutoCreatesTag()
        {
            const string testTag = "SceneObjSetAutoTag99";

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["tag"] = testTag
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(testTag, testObjects[0].tag);
            Assert.That(UnityEditorInternal.InternalEditorUtility.tags, Does.Contain(testTag));

            try { UnityEditorInternal.InternalEditorUtility.RemoveTag(testTag); } catch { }
        }

        [Test]
        public void Set_LayerByName_SetsLayer()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["layer"] = "UI"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(LayerMask.NameToLayer("UI"), testObjects[0].layer);
        }

        [Test]
        public void Set_LayerByNumber_SetsLayer()
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["layer"] = uiLayer
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(uiLayer, testObjects[0].layer);
        }

        [Test]
        public void Set_InvalidLayerName_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["layer"] = "NonExistentLayer12345"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for invalid layer name");
        }

        #endregion

        #region Reparenting

        [Test]
        public void Set_Parent_ReparentsObject()
        {
            var newParent = CreateTestObject("SetNewParent");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["parent"] = "SetNewParent"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(newParent.transform, testObjects[0].transform.parent);
        }

        [Test]
        public void Set_CircularParent_ReturnsError()
        {
            var child = CreateTestObject("CircularChild");
            child.transform.SetParent(testObjects[0].transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["parent"] = "CircularChild"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for circular parenting");
            var errorStr = result["error"]?.ToString() ?? result["message"]?.ToString() ?? "";
            Assert.That(errorStr, Does.Contain("loop").IgnoreCase,
                "Error should mention hierarchy loop");
        }

        #endregion

        #region Add / Remove Components

        [Test]
        public void Set_AddComponents_AddsComponentsToObject()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["add_components"] = new JArray { "Rigidbody", "BoxCollider" }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(testObjects[0].GetComponent<Rigidbody>(), "Should have Rigidbody");
            Assert.IsNotNull(testObjects[0].GetComponent<BoxCollider>(), "Should have BoxCollider");
        }

        [Test]
        public void Set_AddComponentWithProperties_AddsAndConfigures()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["add_components"] = new JArray
                {
                    new JObject
                    {
                        ["typeName"] = "Rigidbody",
                        ["properties"] = new JObject { ["mass"] = 25f }
                    }
                }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var rb = testObjects[0].GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, "Should have Rigidbody");
            Assert.AreEqual(25f, rb.mass, 0.001f, "Mass should be configured");
        }

        [Test]
        public void Set_RemoveComponents_RemovesComponentsFromObject()
        {
            testObjects[0].AddComponent<Rigidbody>();
            testObjects[0].AddComponent<BoxCollider>();
            Assert.IsNotNull(testObjects[0].GetComponent<Rigidbody>());

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["remove_components"] = new JArray { "Rigidbody" }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(testObjects[0].GetComponent<Rigidbody>(), "Rigidbody should be removed");
            Assert.IsNotNull(testObjects[0].GetComponent<BoxCollider>(), "BoxCollider should remain");
        }

        #endregion

        #region Component Properties

        [Test]
        public void Set_ComponentAndProperties_SetsPropertiesOnComponent()
        {
            testObjects[0].AddComponent<Rigidbody>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["component"] = "Rigidbody",
                ["properties"] = new JObject { ["mass"] = 50f, ["useGravity"] = false }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var rb = testObjects[0].GetComponent<Rigidbody>();
            Assert.AreEqual(50f, rb.mass, 0.001f);
            Assert.IsFalse(rb.useGravity);
        }

        [Test]
        public void Set_ComponentProperties_SetsMultipleComponentProperties()
        {
            testObjects[0].AddComponent<Rigidbody>();
            testObjects[0].AddComponent<BoxCollider>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["component_properties"] = new JObject
                {
                    ["Rigidbody"] = new JObject { ["mass"] = 15f },
                    ["BoxCollider"] = new JObject { ["isTrigger"] = true }
                }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(15f, testObjects[0].GetComponent<Rigidbody>().mass, 0.001f);
            Assert.IsTrue(testObjects[0].GetComponent<BoxCollider>().isTrigger);
        }

        #endregion

        #region Batch Operations

        [Test]
        public void Set_BatchByTag_ModifiesAllMatchingObjects()
        {
            testObjects[0].tag = "MainCamera";
            var go2 = CreateTestObject("SetBatchObj2");
            go2.tag = "MainCamera";
            var go3 = CreateTestObject("SetBatchOther");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["tag"] = "MainCamera",
                ["active"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(testObjects[0].activeSelf, "First tagged object should be deactivated");
            Assert.IsFalse(go2.activeSelf, "Second tagged object should be deactivated");
            Assert.IsTrue(go3.activeSelf, "Untagged object should remain active");
        }

        [Test]
        public void Set_BatchByRegex_ModifiesAllMatchingObjects()
        {
            var a = CreateTestObject("BatchRegexTarget_A");
            var b = CreateTestObject("BatchRegexTarget_B");
            var c = CreateTestObject("BatchOtherObj");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target_regex"] = ".*BatchRegexTarget.*",
                ["active"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(a.activeSelf);
            Assert.IsFalse(b.activeSelf);
            Assert.IsTrue(c.activeSelf, "Non-matching object should remain active");
        }

        [Test]
        public void Set_BatchByParent_ModifiesAllChildren()
        {
            var parent = CreateTestObject("BatchParent");
            var child1 = CreateTestObject("BatchChild1");
            child1.transform.SetParent(parent.transform);
            var child2 = CreateTestObject("BatchChild2");
            child2.transform.SetParent(parent.transform);
            var other = CreateTestObject("BatchNotChild");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["parent"] = "BatchParent",
                ["active"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(child1.activeSelf);
            Assert.IsFalse(child2.activeSelf);
            Assert.IsTrue(other.activeSelf, "Non-child should remain active");
            Assert.IsTrue(parent.activeSelf, "Parent itself should remain active");
        }

        [Test]
        public void Set_BatchNoMatches_ReturnsSuccessWithZeroCount()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target_regex"] = ".*NothingMatches12345.*",
                ["active"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var count = result["data"]?["count"]?.Value<int>();
            Assert.AreEqual(0, count, "Count should be 0 when no objects match");
        }

        #endregion

        #region Response Structure

        [Test]
        public void Set_SingleSuccess_ReturnsChanges()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = "SetTestObject",
                ["active"] = false,
                ["position"] = new JArray { 1f, 2f, 3f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsNotNull(data?["changes"], "Should include changes list");
            var changes = data["changes"] as JArray;
            Assert.IsTrue(changes.Count > 0, "Should have at least one change");
        }

        [Test]
        public void Set_MultipleProperties_AppliesAll()
        {
            int id = testObjects[0].GetInstanceID();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = id,
                ["name"] = "FullyModified",
                ["position"] = new JArray { 100f, 200f, 300f },
                ["scale"] = new JArray { 5f, 5f, 5f },
                ["tag"] = "MainCamera"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("FullyModified", testObjects[0].name);
            Assert.AreEqual(new Vector3(100f, 200f, 300f), testObjects[0].transform.localPosition);
            Assert.AreEqual(new Vector3(5f, 5f, 5f), testObjects[0].transform.localScale);
            Assert.AreEqual("MainCamera", testObjects[0].tag);
        }

        #endregion
    }
}
