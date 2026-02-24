using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests that enum properties serialize as string names and can be written back
    /// using either string names or integer values.
    /// </summary>
    public class EnumSerializationTests
    {
        private GameObject _testGo;

        [SetUp]
        public void SetUp()
        {
            _testGo = new GameObject("EnumTestObject");
        }

        [TearDown]
        public void TearDown()
        {
            if (_testGo != null) Object.DestroyImmediate(_testGo);
        }

        #region Read Tests (Serialization)

        [Test]
        public void GetComponentData_Light_SerializesTypeAsString()
        {
            var light = _testGo.AddComponent<Light>();
            light.type = LightType.Spot;

            var data = GameObjectSerializer.GetComponentData(light);
            var jo = JObject.FromObject(data);
            var props = jo["properties"] as JObject;

            // The Light component's type property should be a string, not an integer
            // Light uses reflection path, so the type field is serialized via StringEnumConverter
            var typeValue = props["type"];
            Assert.IsNotNull(typeValue, "Light should have a 'type' property");
            Assert.AreEqual(JTokenType.String, typeValue.Type,
                $"Expected string enum, got {typeValue.Type}: {typeValue}");
            Assert.AreEqual("Spot", typeValue.Value<string>());
        }

        [Test]
        public void GetComponentData_Light_DirectionalSerializesCorrectly()
        {
            var light = _testGo.AddComponent<Light>();
            light.type = LightType.Directional;

            var data = GameObjectSerializer.GetComponentData(light);
            var jo = JObject.FromObject(data);
            var typeValue = jo["properties"]?["type"];

            Assert.IsNotNull(typeValue);
            Assert.AreEqual("Directional", typeValue.Value<string>());
        }

        [Test]
        public void GetComponentData_Rigidbody_SerializesInterpolationAsString()
        {
            var rb = _testGo.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var data = GameObjectSerializer.GetComponentData(rb);
            var jo = JObject.FromObject(data);
            var interpValue = jo["properties"]?["interpolation"];

            Assert.IsNotNull(interpValue, "Rigidbody should have interpolation property");
            Assert.AreEqual(JTokenType.String, interpValue.Type);
            Assert.AreEqual("Interpolate", interpValue.Value<string>());
        }

        #endregion

        #region Write Tests (Setting Enums)

        [Test]
        public void SceneObject_SetEnumByString_Works()
        {
            var light = _testGo.AddComponent<Light>();
            light.type = LightType.Point;

            var result = JObject.FromObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component"] = "Light",
                ["properties"] = new JObject { ["m_Type"] = "Spot" }
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            Assert.AreEqual(LightType.Spot, light.type);
        }

        [Test]
        public void SceneObject_SetEnumByInt_Works()
        {
            var light = _testGo.AddComponent<Light>();
            light.type = LightType.Point;

            var result = JObject.FromObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component"] = "Light",
                ["properties"] = new JObject { ["m_Type"] = (int)LightType.Directional }
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            Assert.AreEqual(LightType.Directional, light.type);
        }

        [Test]
        public void SceneObject_SetEnumByString_CaseInsensitive()
        {
            var light = _testGo.AddComponent<Light>();

            var result = JObject.FromObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component"] = "Light",
                ["properties"] = new JObject { ["m_Type"] = "spot" }
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            Assert.AreEqual(LightType.Spot, light.type);
        }

        [Test]
        public void SceneObject_SetEnumInvalidString_ReturnsError()
        {
            _testGo.AddComponent<Light>();

            var result = JObject.FromObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component"] = "Light",
                ["properties"] = new JObject { ["m_Type"] = "NonexistentType" }
            }));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        #endregion

        #region Roundtrip Tests

        [Test]
        public void Roundtrip_ReadEnumThenWriteBack()
        {
            var light = _testGo.AddComponent<Light>();
            light.type = LightType.Spot;

            // Read
            var data = GameObjectSerializer.GetComponentData(light);
            var jo = JObject.FromObject(data);
            var readValue = jo["properties"]?["type"]?.Value<string>();
            Assert.AreEqual("Spot", readValue);

            // Change to something else first
            light.type = LightType.Point;

            // Write back the value we read
            var result = JObject.FromObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component"] = "Light",
                ["properties"] = new JObject { ["m_Type"] = readValue }
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            Assert.AreEqual(LightType.Spot, light.type, "Roundtrip should preserve enum value");
        }

        #endregion
    }
}
