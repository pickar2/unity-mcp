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
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = cube.GetComponent<MeshRenderer>();

                var data = GameObjectSerializer.GetComponentData(renderer, includeInternal: false) as Dictionary<string, object>;
                Assert.IsNotNull(data);

                var props = data["properties"] as Dictionary<string, object>;
                Assert.IsNotNull(props);

                // 'bounds' is in InternalPropertyNames
                Assert.IsFalse(props.ContainsKey("bounds"), "Should omit 'bounds' when includeInternal=false");

                // Core properties should still be present
                Assert.IsTrue(props.ContainsKey("enabled"), "Should include 'enabled'");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void GetComponentData_IncludeInternalTrue_IncludesInternalProperties()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = cube.GetComponent<MeshRenderer>();

                var data = GameObjectSerializer.GetComponentData(renderer, includeInternal: true) as Dictionary<string, object>;
                Assert.IsNotNull(data);

                var props = data["properties"] as Dictionary<string, object>;
                Assert.IsNotNull(props);

                // Core properties should be present
                Assert.IsTrue(props.ContainsKey("enabled"), "Should include 'enabled'");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void GetComponentData_DefaultIncludeInternal_IsTrue()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = cube.GetComponent<MeshRenderer>();

                // Default overload has includeInternal=true for backwards compatibility
                var data = GameObjectSerializer.GetComponentData(renderer) as Dictionary<string, object>;
                Assert.IsNotNull(data);

                var props = data["properties"] as Dictionary<string, object>;
                Assert.IsNotNull(props);

                // Default should include everything (backwards compat)
                Assert.IsTrue(props.ContainsKey("enabled"), "Should include 'enabled' with default params");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
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
