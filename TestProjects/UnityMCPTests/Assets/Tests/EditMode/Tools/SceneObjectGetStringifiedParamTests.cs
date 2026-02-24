using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for scene_object get action when parameters arrive as stringified JSON
    /// (as happens through the MCP bridge). Covers two bugs from the bug report:
    /// Bug 1: properties parameter not filtering when sent as stringified array
    /// Bug 2: components parameter not working when sent as stringified array
    /// </summary>
    public class SceneObjectGetStringifiedParamTests
    {
        private GameObject _testGo;

        [SetUp]
        public void SetUp()
        {
            _testGo = new GameObject("StringifiedParamTest");
            var rb = _testGo.AddComponent<Rigidbody>();
            rb.mass = 7.5f;
            rb.useGravity = false;
            rb.isKinematic = true;
            _testGo.AddComponent<BoxCollider>();
            _testGo.AddComponent<AudioSource>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testGo != null) Object.DestroyImmediate(_testGo);
        }

        #region Bug 1: Stringified properties array

        [Test]
        public void Get_PropertiesAsStringifiedArray_FiltersProperties()
        {
            // MCP bridge may stringify: properties=["mass"] → properties="[\"mass\"]"
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["component"] = "Rigidbody",
                ["properties"] = "[\"mass\"]"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components, "Should include components data");
            Assert.IsTrue(components.Count > 0, "Should have at least one component");
            var props = components[0]["properties"] as JObject;
            Assert.IsNotNull(props, "Component should have properties");
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass' property");
            Assert.IsFalse(props.ContainsKey("useGravity"),
                "Should NOT include 'useGravity' — not in filter");
            Assert.IsFalse(props.ContainsKey("isKinematic"),
                "Should NOT include 'isKinematic' — not in filter");
        }

        [Test]
        public void Get_PropertiesAsStringifiedArrayMultiple_FiltersCorrectly()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["component"] = "Rigidbody",
                ["properties"] = "[\"mass\", \"isKinematic\"]"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props, "Should have filtered properties");
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass'");
            Assert.IsTrue(props.ContainsKey("isKinematic"), "Should include 'isKinematic'");
            Assert.IsFalse(props.ContainsKey("useGravity"),
                "Should NOT include 'useGravity' — not in filter");
        }

        #endregion

        #region Bug 2: Stringified components array

        [Test]
        public void Get_ComponentsAsStringifiedArray_ReturnsComponentData()
        {
            // MCP bridge may stringify: components=["Rigidbody"] → components="[\"Rigidbody\"]"
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["components"] = "[\"Rigidbody\"]"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components,
                "Should include components data when components is a stringified array");
            Assert.AreEqual(1, components.Count,
                "Should filter to only Rigidbody");
            Assert.AreEqual("UnityEngine.Rigidbody", components[0]["typeName"]?.ToString());
        }

        [Test]
        public void Get_ComponentsAsStringifiedArrayMultiple_FiltersCorrectly()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["components"] = "[\"Rigidbody\", \"BoxCollider\"]"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components,
                "Should include components for stringified multi-type array");
            Assert.AreEqual(2, components.Count,
                "Should include Rigidbody and BoxCollider only, not AudioSource");
            var typeNames = components.Select(c => c["typeName"]?.ToString()).ToList();
            Assert.That(typeNames, Does.Contain("UnityEngine.Rigidbody"));
            Assert.That(typeNames, Does.Contain("UnityEngine.BoxCollider"));
        }

        #endregion

        #region Both bugs combined

        [Test]
        public void Get_BothComponentsAndPropertiesStringified_WorksCorrectly()
        {
            // Both parameters stringified — the full MCP bridge scenario
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["components"] = "[\"Rigidbody\"]",
                ["properties"] = "[\"mass\"]"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components, "Should have component data");
            Assert.AreEqual(1, components.Count, "Should only include Rigidbody");

            var props = components[0]["properties"] as JObject;
            Assert.IsNotNull(props, "Rigidbody should have properties");
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass'");
            Assert.AreEqual(1, props.Count,
                "Should ONLY include 'mass', nothing else");
        }

        #endregion

        #region Baseline — native JArray still works

        [Test]
        public void Get_ComponentsAsNativeJArray_StillWorks()
        {
            // Ensure the existing native JArray path is not broken
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["components"] = new JArray("Rigidbody")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var components = result["data"]?["components"] as JArray;
            Assert.IsNotNull(components, "Native JArray should still work");
            Assert.AreEqual(1, components.Count);
        }

        [Test]
        public void Get_PropertiesAsNativeJArray_StillWorks()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "StringifiedParamTest",
                ["component"] = "Rigidbody",
                ["properties"] = new JArray("mass")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);
            Assert.IsTrue(props.ContainsKey("mass"));
            Assert.IsFalse(props.ContainsKey("useGravity"));
        }

        #endregion
    }
}
