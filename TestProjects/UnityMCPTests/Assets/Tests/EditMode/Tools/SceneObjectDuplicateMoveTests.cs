using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class SceneObjectDuplicateMoveTests
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

        private GameObject FindAndTrack(string name)
        {
            var go = GameObject.Find(name);
            if (go != null && !testObjects.Contains(go))
                testObjects.Add(go);
            return go;
        }

        private GameObject TrackById(int instanceId)
        {
            var go = UnityEditor.EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go != null && !testObjects.Contains(go))
                testObjects.Add(go);
            return go;
        }

        #region Duplicate Basic

        [Test]
        public void Duplicate_ByName_CreatesClone()
        {
            var source = CreateTestObject("DupSource");
            source.AddComponent<Rigidbody>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupSource"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var dupData = result["data"]?["duplicate"];
            Assert.IsNotNull(dupData, "Response should include duplicate data");
            var dupId = dupData["instance_id"]?.Value<int>();
            Assert.IsTrue(dupId.HasValue, "Should return instance_id of duplicate");

            var dup = TrackById(dupId.Value);
            Assert.IsNotNull(dup, "Duplicate should exist in scene");
            Assert.IsNotNull(dup.GetComponent<Rigidbody>(), "Duplicate should have copied components");
        }

        [Test]
        public void Duplicate_WithCustomName_UsesProvidedName()
        {
            CreateTestObject("DupNameSource");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupNameSource",
                ["name"] = "MyCustomClone"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var dup = FindAndTrack("MyCustomClone");
            Assert.IsNotNull(dup, "Duplicate should use provided name");
        }

        [Test]
        public void Duplicate_WithoutName_GeneratesDefaultName()
        {
            CreateTestObject("DupDefaultName");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupDefaultName"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var dupName = result["data"]?["duplicate"]?["name"]?.ToString();
            Assert.IsNotNull(dupName, "Should have a name");
            Assert.That(dupName, Does.Contain("Copy").IgnoreCase,
                "Default name should contain 'Copy'");

            var dupId = result["data"]?["duplicate"]?["instance_id"]?.Value<int>();
            if (dupId.HasValue) TrackById(dupId.Value);
        }

        [Test]
        public void Duplicate_NonexistentTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "NonExistentDup12345"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for nonexistent target");
        }

        [Test]
        public void Duplicate_WithoutTarget_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without target");
        }

        #endregion

        #region Duplicate Positioning

        [Test]
        public void Duplicate_WithOffset_PositionsRelativeToOriginal()
        {
            var source = CreateTestObject("DupOffsetSource");
            source.transform.position = new Vector3(10, 0, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupOffsetSource",
                ["offset"] = new JArray { 5f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var dupId = result["data"]?["duplicate"]?["instance_id"]?.Value<int>();
            var dup = TrackById(dupId.Value);
            Assert.IsNotNull(dup);
            Assert.AreEqual(15f, dup.transform.position.x, 0.01f,
                "Duplicate should be at source position + offset");
        }

        [Test]
        public void Duplicate_WithAbsolutePosition_UsesProvidedPosition()
        {
            var source = CreateTestObject("DupPosSource");
            source.transform.position = new Vector3(10, 0, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupPosSource",
                ["position"] = new JArray { 99f, 0f, 0f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var dupId = result["data"]?["duplicate"]?["instance_id"]?.Value<int>();
            var dup = TrackById(dupId.Value);
            Assert.IsNotNull(dup);
            Assert.AreEqual(99f, dup.transform.position.x, 0.01f,
                "Duplicate should be at the provided absolute position");
        }

        #endregion

        #region Duplicate Parenting

        [Test]
        public void Duplicate_WithParent_ReparentsDuplicate()
        {
            var source = CreateTestObject("DupReparentSource");
            var newParent = CreateTestObject("DupNewParent");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupReparentSource",
                ["parent"] = "DupNewParent"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var dupId = result["data"]?["duplicate"]?["instance_id"]?.Value<int>();
            var dup = TrackById(dupId.Value);
            Assert.IsNotNull(dup);
            Assert.AreEqual(newParent.transform, dup.transform.parent,
                "Duplicate should be under specified parent");
        }

        [Test]
        public void Duplicate_WithoutParent_InheritsSameParent()
        {
            var sharedParent = CreateTestObject("SharedParent");
            var source = CreateTestObject("DupInheritSource");
            source.transform.SetParent(sharedParent.transform);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = source.GetInstanceID()
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var dupId = result["data"]?["duplicate"]?["instance_id"]?.Value<int>();
            var dup = TrackById(dupId.Value);
            Assert.IsNotNull(dup);
            Assert.AreEqual(sharedParent.transform, dup.transform.parent,
                "Duplicate should inherit source's parent");
        }

        #endregion

        #region Duplicate Response

        [Test]
        public void Duplicate_Response_IncludesSourceAndDuplicateInfo()
        {
            var source = CreateTestObject("DupResponseSource");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "duplicate",
                ["target"] = "DupResponseSource",
                ["name"] = "DupResponseClone"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsNotNull(data?["source"], "Response should include source info");
            Assert.IsNotNull(data?["duplicate"], "Response should include duplicate info");
            Assert.IsNotNull(data["source"]?["instance_id"], "Source should have instance_id");
            Assert.IsNotNull(data["duplicate"]?["instance_id"], "Duplicate should have instance_id");
            Assert.IsNotNull(data["duplicate"]?["name"], "Duplicate should have name");

            var dupId = data["duplicate"]?["instance_id"]?.Value<int>();
            if (dupId.HasValue) TrackById(dupId.Value);
        }

        #endregion

        #region Move Relative - Direction

        [Test]
        public void MoveRelative_DirectionRight_MovesObjectInWorldSpace()
        {
            var target = CreateTestObject("MoveTarget");
            target.transform.position = Vector3.zero;
            var reference = CreateTestObject("MoveReference");
            reference.transform.position = new Vector3(10, 0, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveTarget",
                ["reference"] = "MoveReference",
                ["direction"] = "right",
                ["distance"] = 3f
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(13f, target.transform.position.x, 0.01f,
                "Should be reference.x + distance in right direction");
            Assert.AreEqual(0f, target.transform.position.y, 0.01f);
            Assert.AreEqual(0f, target.transform.position.z, 0.01f);
        }

        [Test]
        public void MoveRelative_DirectionUp_MovesObjectUpward()
        {
            var target = CreateTestObject("MoveUpTarget");
            var reference = CreateTestObject("MoveUpRef");
            reference.transform.position = new Vector3(0, 5, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveUpTarget",
                ["reference"] = "MoveUpRef",
                ["direction"] = "up",
                ["distance"] = 2f
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(7f, target.transform.position.y, 0.01f,
                "Should be reference.y + distance upward");
        }

        [Test]
        public void MoveRelative_DirectionForward_MovesInWorldForward()
        {
            var target = CreateTestObject("MoveFwdTarget");
            var reference = CreateTestObject("MoveFwdRef");
            reference.transform.position = new Vector3(0, 0, 10);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveFwdTarget",
                ["reference"] = "MoveFwdRef",
                ["direction"] = "forward",
                ["distance"] = 5f
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(15f, target.transform.position.z, 0.01f,
                "Should be reference.z + distance forward");
        }

        [Test]
        public void MoveRelative_DefaultDistance_UsesOne()
        {
            var target = CreateTestObject("MoveDefDistTarget");
            var reference = CreateTestObject("MoveDefDistRef");
            reference.transform.position = Vector3.zero;

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveDefDistTarget",
                ["reference"] = "MoveDefDistRef",
                ["direction"] = "right"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1f, target.transform.position.x, 0.01f,
                "Default distance should be 1");
        }

        #endregion

        #region Move Relative - Offset

        [Test]
        public void MoveRelative_CustomOffset_PositionsRelativeToReference()
        {
            var target = CreateTestObject("MoveOffsetTarget");
            var reference = CreateTestObject("MoveOffsetRef");
            reference.transform.position = new Vector3(10, 20, 30);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveOffsetTarget",
                ["reference"] = "MoveOffsetRef",
                ["offset"] = new JArray { 1f, 2f, 3f }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(11f, target.transform.position.x, 0.01f);
            Assert.AreEqual(22f, target.transform.position.y, 0.01f);
            Assert.AreEqual(33f, target.transform.position.z, 0.01f);
        }

        #endregion

        #region Move Relative - Local Space

        [Test]
        public void MoveRelative_LocalSpace_UsesReferenceLocalAxes()
        {
            var target = CreateTestObject("MoveLocalTarget");
            var reference = CreateTestObject("MoveLocalRef");
            reference.transform.position = Vector3.zero;
            reference.transform.rotation = Quaternion.Euler(0, 90, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveLocalTarget",
                ["reference"] = "MoveLocalRef",
                ["direction"] = "forward",
                ["distance"] = 5f,
                ["world_space"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            // Reference is rotated 90 degrees on Y, so "forward" in local space is world +X
            Assert.AreEqual(5f, target.transform.position.x, 0.1f,
                "Local forward of 90-Y-rotated reference should be world +X");
            Assert.AreEqual(0f, target.transform.position.z, 0.1f);
        }

        [Test]
        public void MoveRelative_LocalSpaceOffset_TransformsOffsetByReferenceRotation()
        {
            var target = CreateTestObject("MoveLocalOffTarget");
            var reference = CreateTestObject("MoveLocalOffRef");
            reference.transform.position = Vector3.zero;
            reference.transform.rotation = Quaternion.Euler(0, 90, 0);

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveLocalOffTarget",
                ["reference"] = "MoveLocalOffRef",
                ["offset"] = new JArray { 0f, 0f, 5f },
                ["world_space"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            // Offset [0,0,5] in local space of 90-Y-rotated reference = [5,0,0] in world
            Assert.AreEqual(5f, target.transform.position.x, 0.1f);
            Assert.AreEqual(0f, target.transform.position.z, 0.1f);
        }

        #endregion

        #region Move Relative - Validation

        [Test]
        public void MoveRelative_WithoutTarget_ReturnsError()
        {
            CreateTestObject("MoveValRef");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["reference"] = "MoveValRef",
                ["direction"] = "right"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without target");
        }

        [Test]
        public void MoveRelative_WithoutReference_ReturnsError()
        {
            CreateTestObject("MoveNoRefTarget");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveNoRefTarget",
                ["direction"] = "right"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail without reference");
        }

        [Test]
        public void MoveRelative_WithoutDirectionOrOffset_ReturnsError()
        {
            CreateTestObject("MoveNoDirTarget");
            CreateTestObject("MoveNoDirRef");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveNoDirTarget",
                ["reference"] = "MoveNoDirRef"
            }));

            Assert.IsFalse(result.Value<bool>("success"),
                "Should fail without direction or offset");
        }

        [Test]
        public void MoveRelative_NonexistentReference_ReturnsError()
        {
            CreateTestObject("MoveExistTarget");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveExistTarget",
                ["reference"] = "NonExistentRef12345",
                ["direction"] = "right"
            }));

            Assert.IsFalse(result.Value<bool>("success"),
                "Should fail for nonexistent reference");
        }

        #endregion

        #region Move Relative Response

        [Test]
        public void MoveRelative_Success_ReturnsNewPosition()
        {
            var target = CreateTestObject("MoveRespTarget");
            var reference = CreateTestObject("MoveRespRef");
            reference.transform.position = Vector3.zero;

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "move_relative",
                ["target"] = "MoveRespTarget",
                ["reference"] = "MoveRespRef",
                ["direction"] = "right",
                ["distance"] = 3f
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsNotNull(data?["new_position"], "Response should include new_position");
            Assert.AreEqual(3f, data["new_position"]?["x"]?.Value<float>() ?? 0f, 0.01f);
        }

        #endregion

        #region Invalid Action

        [Test]
        public void UnknownAction_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "fly_to_moon"
            }));

            Assert.IsFalse(result.Value<bool>("success"), "Should fail for unknown action");
        }

        [Test]
        public void NullParams_ReturnsError()
        {
            var result = ToJObject(SceneObject.HandleCommand(null));
            Assert.IsFalse(result.Value<bool>("success"), "Should fail for null params");
        }

        #endregion
    }
}
