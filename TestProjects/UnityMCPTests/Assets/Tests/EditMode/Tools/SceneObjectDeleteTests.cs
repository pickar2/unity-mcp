using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class SceneObjectDeleteTests
    {
        private List<GameObject> testObjects = new List<GameObject>();

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

        #region Single Delete

        [Test]
        public void Delete_ByName_DestroysObject()
        {
            CreateTestObject("DeleteByName");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target"] = "DeleteByName"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(GameObject.Find("DeleteByName"), "Object should be destroyed");
        }

        [Test]
        public void Delete_ByPath_DestroysObject()
        {
            var parent = CreateTestObject("DeleteParent");
            var child = new GameObject("DeleteChild");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target"] = "/DeleteParent/DeleteChild"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(0, parent.transform.childCount, "Child should be destroyed");
        }

        [Test]
        public void Delete_ByInstanceId_DestroysObject()
        {
            var go = CreateTestObject("DeleteById");
            int id = go.GetInstanceID();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target"] = id
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(GameObject.Find("DeleteById"), "Object should be destroyed");
        }

        [Test]
        public void Delete_NonexistentTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target"] = "NonExistentDeleteObj12345"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for nonexistent object");
        }

        [Test]
        public void Delete_WithoutTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without target");
        }

        #endregion

        #region Delete Response

        [Test]
        public void Delete_Success_ReturnsDeletedInfoAndCount()
        {
            CreateTestObject("DeleteResponseObj");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target"] = "DeleteResponseObj"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsNotNull(data?["deleted"], "Should include deleted list");
            Assert.AreEqual(1, data?["count"]?.Value<int>(), "Count should be 1");
        }

        #endregion

        #region Batch Delete

        [Test]
        public void Delete_BatchByTag_DestroysAllMatchingObjects()
        {
            var a = CreateTestObject("DeleteTagA");
            a.tag = "MainCamera";
            var b = CreateTestObject("DeleteTagB");
            b.tag = "MainCamera";
            var c = CreateTestObject("DeleteTagOther");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["tag"] = "MainCamera"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(GameObject.Find("DeleteTagA"), "Tagged object A should be destroyed");
            Assert.IsNull(GameObject.Find("DeleteTagB"), "Tagged object B should be destroyed");
            Assert.IsNotNull(GameObject.Find("DeleteTagOther"), "Non-tagged object should survive");
        }

        [Test]
        public void Delete_BatchByRegex_DestroysMatchingObjects()
        {
            CreateTestObject("DeleteRegex_X");
            CreateTestObject("DeleteRegex_Y");
            CreateTestObject("DeleteKeepMe");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target_regex"] = ".*DeleteRegex_.*"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(GameObject.Find("DeleteRegex_X"), "Matching object X should be destroyed");
            Assert.IsNull(GameObject.Find("DeleteRegex_Y"), "Matching object Y should be destroyed");
            Assert.IsNotNull(GameObject.Find("DeleteKeepMe"), "Non-matching object should survive");
        }

        [Test]
        public void Delete_BatchByParent_DestroysAllChildren()
        {
            var parent = CreateTestObject("DeleteBatchParent");
            var child1 = new GameObject("DeleteBatchChild1");
            testObjects.Add(child1);
            child1.transform.SetParent(parent.transform);
            var child2 = new GameObject("DeleteBatchChild2");
            testObjects.Add(child2);
            child2.transform.SetParent(parent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["parent"] = "DeleteBatchParent"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(0, parent.transform.childCount, "All children should be destroyed");
            Assert.IsNotNull(GameObject.Find("DeleteBatchParent"), "Parent itself should survive");
        }

        [Test]
        public void Delete_BatchNoMatches_ReturnsSuccessWithZeroCount()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target_regex"] = ".*NothingMatchesThisPattern12345.*"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var count = result["data"]?["count"]?.Value<int>();
            Assert.AreEqual(0, count, "Count should be 0 when no objects match");
        }

        [Test]
        public void Delete_BatchReturnsCount()
        {
            CreateTestObject("DeleteCountA");
            CreateTestObject("DeleteCountB");
            CreateTestObject("DeleteCountC");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "delete",
                ["target_regex"] = ".*DeleteCount[ABC]"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var count = result["data"]?["count"]?.Value<int>();
            Assert.AreEqual(3, count, "Should report 3 deleted objects");
        }

        #endregion
    }
}
