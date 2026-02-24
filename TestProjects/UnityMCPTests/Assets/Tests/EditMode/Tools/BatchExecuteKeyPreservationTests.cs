using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Verifies that batch_execute only normalizes top-level parameter keys (snake_case → camelCase)
    /// and preserves nested value keys (e.g. Unity serialized property paths like m_PersistentCalls).
    /// </summary>
    public class BatchExecuteKeyPreservationTests
    {
        private GameObject testGo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            CommandRegistry.Initialize();
        }

        [SetUp]
        public void SetUp()
        {
            testGo = new GameObject("BatchKeyTestGO");
        }

        [TearDown]
        public void TearDown()
        {
            if (testGo != null)
                Object.DestroyImmediate(testGo);
        }

        [Test]
        public void NestedValueKeys_WithUnderscores_ArePreservedThroughBatch()
        {
            testGo.AddComponent<AudioSource>();

            // Use component_properties (snake_case top-level key) with nested keys.
            // The batch normalizer should convert component_properties → componentProperties
            // but must NOT mangle the nested AudioSource key or property names.
            var batchParams = new JObject
            {
                ["commands"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "scene_object",
                        ["params"] = new JObject
                        {
                            ["action"] = "set",
                            ["target"] = testGo.name,
                            ["component_properties"] = new JObject
                            {
                                ["AudioSource"] = new JObject
                                {
                                    ["volume"] = 0.42f
                                }
                            }
                        }
                    }
                }
            };

            var result = BatchExecute.HandleCommand(batchParams).GetAwaiter().GetResult();
            var resultObj = JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"), $"Batch should succeed: {resultObj}");

            // Verify the nested keys were preserved and the property was set correctly
            var audio = testGo.GetComponent<AudioSource>();
            Assert.AreEqual(0.42f, audio.volume, 0.001f);
        }

        [Test]
        public void TopLevelParameterKeys_AreStillNormalized()
        {
            testGo.AddComponent<AudioSource>();

            // Use snake_case top-level keys: component_properties
            // Batch normalization should convert this to componentProperties
            var batchParams = new JObject
            {
                ["commands"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "scene_object",
                        ["params"] = new JObject
                        {
                            ["action"] = "set",
                            ["target"] = testGo.name,
                            ["component_properties"] = new JObject
                            {
                                ["AudioSource"] = new JObject
                                {
                                    ["volume"] = 0.42f
                                }
                            }
                        }
                    }
                }
            };

            var result = BatchExecute.HandleCommand(batchParams).GetAwaiter().GetResult();
            var resultObj = JObject.FromObject(result);

            Assert.IsTrue(resultObj.Value<bool>("success"),
                $"Batch with snake_case top-level keys should succeed: {resultObj}");
            Assert.AreEqual(0.42f, testGo.GetComponent<AudioSource>().volume, 0.001f);
        }

        [Test]
        public void Regression_CreateGameObject_StillWorksViaBatch()
        {
            string goName = "BatchCreatedGO_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            GameObject created = null;

            try
            {
                var batchParams = new JObject
                {
                    ["commands"] = new JArray
                    {
                        new JObject
                        {
                            ["tool"] = "scene_object",
                            ["params"] = new JObject
                            {
                                ["action"] = "create",
                                ["name"] = goName,
                                ["primitive"] = "Cube"
                            }
                        }
                    }
                };

                var result = BatchExecute.HandleCommand(batchParams).GetAwaiter().GetResult();
                var resultObj = JObject.FromObject(result);

                Assert.IsTrue(resultObj.Value<bool>("success"), $"Batch create GO should succeed: {resultObj}");

                created = GameObject.Find(goName);
                Assert.IsNotNull(created, $"GameObject '{goName}' should exist in scene");
            }
            finally
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
        }
    }
}
