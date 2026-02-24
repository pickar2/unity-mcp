using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    public class IncludeInternalFilteringTests
    {
        private GameObject _testGo;

        [SetUp]
        public void SetUp()
        {
            _testGo = new GameObject("InternalFilterTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (_testGo != null) Object.DestroyImmediate(_testGo);
        }

        [Test]
        public void GetComponentData_IncludeInternalFalse_OmitsInternalProperties()
        {
            var rb = _testGo.AddComponent<Rigidbody>();

            var data = GameObjectSerializer.GetComponentData(rb, includeInternal: false) as Dictionary<string, object>;
            Assert.IsNotNull(data);

            var props = data["properties"] as Dictionary<string, object>;
            Assert.IsNotNull(props);

            // 'drag' is in InternalPropertyNames (deprecated alias for linearDamping)
            Assert.IsFalse(props.ContainsKey("drag"), "Should omit 'drag' when includeInternal=false");
            Assert.IsFalse(props.ContainsKey("angularDrag"), "Should omit 'angularDrag' when includeInternal=false");

            // But modern property names should still be present
            Assert.IsTrue(props.ContainsKey("linearDamping"), "Should include 'linearDamping'");
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass'");
        }

        [Test]
        public void GetComponentData_IncludeInternalTrue_IncludesInternalProperties()
        {
            var rb = _testGo.AddComponent<Rigidbody>();

            var data = GameObjectSerializer.GetComponentData(rb, includeInternal: true) as Dictionary<string, object>;
            Assert.IsNotNull(data);

            var props = data["properties"] as Dictionary<string, object>;
            Assert.IsNotNull(props);

            // Both deprecated and modern should be present
            Assert.IsTrue(props.ContainsKey("linearDamping"), "Should include 'linearDamping'");
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass'");
        }

        [Test]
        public void GetComponentData_DefaultIncludeInternal_IsTrue()
        {
            var rb = _testGo.AddComponent<Rigidbody>();

            // Default overload has includeInternal=true for backwards compatibility
            var data = GameObjectSerializer.GetComponentData(rb) as Dictionary<string, object>;
            Assert.IsNotNull(data);

            var props = data["properties"] as Dictionary<string, object>;
            Assert.IsNotNull(props);

            // Default should include everything (backwards compat)
            Assert.IsTrue(props.ContainsKey("mass"), "Should include 'mass' with default params");
        }

        [Test]
        public void GetComponentData_RendererInternalProperties_Omitted()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "RendererInternalTest";
            try
            {
                var renderer = cube.GetComponent<MeshRenderer>();

                var data = GameObjectSerializer.GetComponentData(renderer, includeInternal: false) as Dictionary<string, object>;
                var props = data?["properties"] as Dictionary<string, object>;

                Assert.IsNotNull(props);

                // Renderer internal properties from skiplist
                Assert.IsFalse(props.ContainsKey("lightmapScaleOffset"), "Should omit lightmapScaleOffset");
                Assert.IsFalse(props.ContainsKey("realtimeLightmapScaleOffset"), "Should omit realtimeLightmapScaleOffset");
                Assert.IsFalse(props.ContainsKey("rayTracingMode"), "Should omit rayTracingMode");
                Assert.IsFalse(props.ContainsKey("motionVectorGenerationMode"), "Should omit motionVectorGenerationMode");
                Assert.IsFalse(props.ContainsKey("rendererPriority"), "Should omit rendererPriority");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void GetComponentData_ColliderLayerMasks_Omitted()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ColliderLayerMaskTest";
            try
            {
                var collider = cube.GetComponent<BoxCollider>();

                var data = GameObjectSerializer.GetComponentData(collider, includeInternal: false) as Dictionary<string, object>;
                var props = data?["properties"] as Dictionary<string, object>;

                Assert.IsNotNull(props);

                // Collider layer mask properties from skiplist
                Assert.IsFalse(props.ContainsKey("excludeLayers"), "Should omit excludeLayers");
                Assert.IsFalse(props.ContainsKey("includeLayers"), "Should omit includeLayers");
                Assert.IsFalse(props.ContainsKey("forceSendLayers"), "Should omit forceSendLayers");
                Assert.IsFalse(props.ContainsKey("forceReceiveLayers"), "Should omit forceReceiveLayers");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }
    }
}
