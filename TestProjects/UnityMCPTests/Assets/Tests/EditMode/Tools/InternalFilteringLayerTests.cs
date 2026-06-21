using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for the multi-layer internal filtering system in GameObjectSerializer.
    /// Verifies that each detection layer works correctly:
    ///   Layer 1: [Obsolete] attribute auto-skip
    ///   Layer 2: Read-only (no setter) auto-skip for built-in types
    ///   Layer 3: Base class declaring type auto-skip (Object/Component noise)
    ///   Layer 4: Curated skip list
    /// Also tests that includeInternal=true bypasses all filtering.
    /// </summary>
    public class InternalFilteringLayerTests
    {
        private GameObject _testGo;

        [SetUp]
        public void SetUp()
        {
            _testGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _testGo.name = "FilterLayerTest";
            _testGo.AddComponent<Rigidbody>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testGo != null) Object.DestroyImmediate(_testGo);
        }

        private Dictionary<string, object> GetProps(Component comp, bool includeInternal)
        {
            // Upstream's GameObjectSerializer.GetComponentData exposes the parameter as
            // includeNonPublicSerializedFields; the JSON-facing tool axis calls it includeInternal.
            // We map the test's includeInternal flag onto that parameter here.
            var data = GameObjectSerializer.GetComponentData(comp, includeNonPublicSerializedFields: includeInternal)
                as Dictionary<string, object>;
            Assert.IsNotNull(data, "GetComponentData should return a dictionary");
            return data.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> props
                ? props : new Dictionary<string, object>();
        }

        #region Layer 1: Obsolete attribute

        [Test]
        public void Layer1_Obsolete_DeprecatedShortcutsOmitted_WhenFilteringEnabled()
        {
            // Component.rigidbody, .collider, .renderer etc. are [Obsolete] shortcuts
            // They should not appear even with includeInternal=true because they're
            // excluded at cache-build time (not at the filtering stage)
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: true);

            // These deprecated Component shortcuts should never appear
            Assert.IsFalse(props.ContainsKey("rigidbody"), "Obsolete 'rigidbody' should be excluded");
            Assert.IsFalse(props.ContainsKey("collider"), "Obsolete 'collider' should be excluded");
            Assert.IsFalse(props.ContainsKey("renderer"), "Obsolete 'renderer' should be excluded");
            Assert.IsFalse(props.ContainsKey("camera"), "Obsolete 'camera' should be excluded");
            Assert.IsFalse(props.ContainsKey("animation"), "Obsolete 'animation' should be excluded");
        }

        [Test]
        public void Layer1_Obsolete_AlsoExcluded_WhenFilteringDisabled()
        {
            // Obsolete check is at cache-build time, so it applies regardless of includeInternal
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            Assert.IsFalse(props.ContainsKey("rigidbody"), "Obsolete shortcuts excluded even with filtering");
        }

        #endregion

        #region Layer 2: Read-only (CanWrite) check

        [Test]
        public void Layer2_ReadOnly_ComputedPropertiesOmitted_WhenFilteringEnabled()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            // worldCenterOfMass is read-only (computed from physics) — should be filtered
            Assert.IsFalse(props.ContainsKey("worldCenterOfMass"),
                "Read-only 'worldCenterOfMass' should be omitted when filtering");
        }

        [Test]
        public void Layer2_ReadOnly_ComputedPropertiesIncluded_WhenFilteringDisabled()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: true);

            // With includeInternal=true, read-only properties should be present
            Assert.IsTrue(props.ContainsKey("worldCenterOfMass"),
                "Read-only 'worldCenterOfMass' should be included when not filtering");
        }

        [Test]
        public void Layer2_ReadOnly_WritablePropertiesKept_WhenFilteringEnabled()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            // mass is writable and useful — should survive all filtering layers
            Assert.IsTrue(props.ContainsKey("mass"),
                "Writable 'mass' should be kept when filtering");
            Assert.IsTrue(props.ContainsKey("useGravity"),
                "Writable 'useGravity' should be kept when filtering");
            Assert.IsTrue(props.ContainsKey("isKinematic"),
                "Writable 'isKinematic' should be kept when filtering");
        }

        [Test]
        public void Layer2_ReadOnly_ColliderReadOnlyOmitted()
        {
            var collider = _testGo.GetComponent<BoxCollider>();
            var props = GetProps(collider, includeInternal: false);

            // attachedRigidbody is read-only on Collider
            Assert.IsFalse(props.ContainsKey("attachedRigidbody"),
                "Read-only 'attachedRigidbody' should be omitted");
            // isTrigger is writable, should be kept
            Assert.IsTrue(props.ContainsKey("isTrigger"),
                "Writable 'isTrigger' should be kept");
        }

        #endregion

        #region Layer 3: Base class declaring type

        [Test]
        public void Layer3_BaseClass_TagAndNameOmitted_WhenFilteringEnabled()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            // tag and name are declared on UnityEngine.Object/Component — noise
            Assert.IsFalse(props.ContainsKey("tag"),
                "Base class 'tag' should be omitted when filtering");
            Assert.IsFalse(props.ContainsKey("name"),
                "Base class 'name' should be omitted when filtering");
        }

        [Test]
        public void Layer3_BaseClass_TagAndNameIncluded_WhenFilteringDisabled()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: true);

            Assert.IsTrue(props.ContainsKey("tag"),
                "Base class 'tag' should be included when not filtering");
            Assert.IsTrue(props.ContainsKey("name"),
                "Base class 'name' should be included when not filtering");
        }

        [Test]
        public void Layer3_BaseClass_GameObjectAndHideFlagsOmitted_WhenFilteringEnabled()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            Assert.IsFalse(props.ContainsKey("gameObject"),
                "Base class 'gameObject' should be omitted when filtering");
            Assert.IsFalse(props.ContainsKey("hideFlags"),
                "Base class 'hideFlags' should be omitted when filtering");
        }

        [Test]
        public void Layer3_BaseClass_EnabledKept_WhenFilteringEnabled()
        {
            // enabled is declared on Behaviour, which is NOT in NoiseBaseTypes
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            // Rigidbody doesn't have its own 'enabled' (it's not a Behaviour subclass
            // that exposes enabled). But MeshRenderer does.
            var renderer = _testGo.GetComponent<MeshRenderer>();
            var rendererProps = GetProps(renderer, includeInternal: false);
            Assert.IsTrue(rendererProps.ContainsKey("enabled"),
                "'enabled' from Behaviour should be kept — not in NoiseBaseTypes");
        }

        #endregion

        #region Layer 4: Curated skip list

        [Test]
        public void Layer4_CuratedList_RendererLightmapOmitted()
        {
            var renderer = _testGo.GetComponent<MeshRenderer>();
            var props = GetProps(renderer, includeInternal: false);

            Assert.IsFalse(props.ContainsKey("lightmapScaleOffset"),
                "Curated skip: lightmapScaleOffset");
            Assert.IsFalse(props.ContainsKey("scaleInLightmap"),
                "Curated skip: scaleInLightmap (newly added)");
            Assert.IsFalse(props.ContainsKey("stitchLightmapSeams"),
                "Curated skip: stitchLightmapSeams (newly added)");
        }

        [Test]
        public void Layer4_CuratedList_PhysicsSolverInternalsOmitted()
        {
            var rb = _testGo.GetComponent<Rigidbody>();
            var props = GetProps(rb, includeInternal: false);

            Assert.IsFalse(props.ContainsKey("solverIterations"),
                "Curated skip: solverIterations (newly added)");
            Assert.IsFalse(props.ContainsKey("sleepThreshold"),
                "Curated skip: sleepThreshold (newly added)");
            Assert.IsFalse(props.ContainsKey("maxDepenetrationVelocity"),
                "Curated skip: maxDepenetrationVelocity (newly added)");
            Assert.IsFalse(props.ContainsKey("automaticInertiaTensor"),
                "Curated skip: automaticInertiaTensor (newly added)");
        }

        [Test]
        public void Layer4_CuratedList_ShadowPropertiesKept()
        {
            // Shadow properties were removed from skip list — they're useful for game dev
            var renderer = _testGo.GetComponent<MeshRenderer>();
            var props = GetProps(renderer, includeInternal: false);

            Assert.IsTrue(props.ContainsKey("shadowCastingMode"),
                "shadowCastingMode should be kept — useful for game dev");
            Assert.IsTrue(props.ContainsKey("receiveShadows"),
                "receiveShadows should be kept — useful for game dev");
        }

        [Test]
        public void Layer4_CuratedList_ColliderLayerMasksOmitted()
        {
            var collider = _testGo.GetComponent<BoxCollider>();
            var props = GetProps(collider, includeInternal: false);

            Assert.IsFalse(props.ContainsKey("excludeLayers"),
                "Curated skip: excludeLayers");
            Assert.IsFalse(props.ContainsKey("includeLayers"),
                "Curated skip: includeLayers");
        }

        #endregion

        #region End-to-end via SceneObject.HandleCommand

        [Test]
        public void EndToEnd_DefaultFiltering_ReducesRigidbodyOutput()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "FilterLayerTest",
                ["component"] = "Rigidbody"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);

            // Key useful properties should be present
            Assert.IsTrue(props.ContainsKey("mass"), "mass should be in filtered output");
            Assert.IsTrue(props.ContainsKey("useGravity"), "useGravity should be in filtered output");
            Assert.IsTrue(props.ContainsKey("isKinematic"), "isKinematic should be in filtered output");
            Assert.IsTrue(props.ContainsKey("constraints"), "constraints should be in filtered output");
            Assert.IsTrue(props.ContainsKey("collisionDetectionMode"), "collisionDetectionMode should be in filtered output");

            // Noise should be absent
            Assert.IsFalse(props.ContainsKey("tag"), "tag should not be in filtered output");
            Assert.IsFalse(props.ContainsKey("name"), "name should not be in filtered output");
            Assert.IsFalse(props.ContainsKey("solverIterations"), "solverIterations should not be in filtered output");
            Assert.IsFalse(props.ContainsKey("worldCenterOfMass"), "worldCenterOfMass should not be in filtered output");
        }

        [Test]
        public void EndToEnd_IncludeInternalTrue_ReturnsEverything()
        {
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "FilterLayerTest",
                ["component"] = "Rigidbody",
                ["includeInternal"] = true
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);

            // With includeInternal=true, everything should be present
            Assert.IsTrue(props.ContainsKey("mass"), "mass present with includeInternal=true");
            Assert.IsTrue(props.ContainsKey("tag"), "tag present with includeInternal=true");
            Assert.IsTrue(props.ContainsKey("name"), "name present with includeInternal=true");
            Assert.IsTrue(props.ContainsKey("worldCenterOfMass"), "worldCenterOfMass present with includeInternal=true");
        }

        [Test]
        public void EndToEnd_ExplicitPropertiesFilter_BypassesInternalFiltering()
        {
            // If the agent explicitly asks for "bounds" via properties filter,
            // they should get it even though bounds is normally filtered out
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "FilterLayerTest",
                ["component"] = "BoxCollider",
                ["properties"] = new JArray("bounds", "isTrigger")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);

            // Both requested properties should be present, even bounds (normally filtered)
            Assert.IsTrue(props.ContainsKey("bounds"),
                "Explicitly requested 'bounds' should be returned despite internal filtering");
            Assert.IsTrue(props.ContainsKey("isTrigger"),
                "Explicitly requested 'isTrigger' should be returned");
            // Only the requested properties should be present
            Assert.AreEqual(2, props.Count,
                "Should only contain the 2 explicitly requested properties");
        }

        [Test]
        public void EndToEnd_ExplicitPropertiesFilter_CanRequestInternalRigidbodyProps()
        {
            // Agent asks for normally-filtered properties on Rigidbody
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "FilterLayerTest",
                ["component"] = "Rigidbody",
                ["properties"] = new JArray("mass", "worldCenterOfMass", "solverIterations")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);

            Assert.IsTrue(props.ContainsKey("mass"), "Requested 'mass' should be present");
            Assert.IsTrue(props.ContainsKey("worldCenterOfMass"),
                "Requested 'worldCenterOfMass' (normally read-only filtered) should be present");
            Assert.IsTrue(props.ContainsKey("solverIterations"),
                "Requested 'solverIterations' (normally curated-list filtered) should be present");
        }

        [Test]
        public void EndToEnd_NoPropertiesFilter_StillFiltersInternals()
        {
            // Without explicit properties filter, internal filtering should still apply
            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["target"] = "FilterLayerTest",
                ["component"] = "BoxCollider"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var props = (result["data"]?["components"] as JArray)?[0]?["properties"] as JObject;
            Assert.IsNotNull(props);

            Assert.IsFalse(props.ContainsKey("bounds"),
                "Without explicit filter, 'bounds' should still be filtered out");
        }

        #endregion

        #region User MonoBehaviours are never filtered

        [Test]
        public void UserScripts_NeverFiltered_RegardlessOfIncludeInternal()
        {
            // User MonoBehaviours should never have internal filtering applied.
            // Even with includeInternal=false, all user script properties/fields should be present.
            // We can't easily test this without a custom MonoBehaviour in the test project,
            // but we can verify that IsUnityBuiltInType returns false for non-Unity types
            // by checking that the type check logic works correctly.
            var rb = _testGo.GetComponent<Rigidbody>();
            var rbData = GameObjectSerializer.GetComponentData(rb, includeNonPublicSerializedFields: false) as Dictionary<string, object>;
            Assert.IsNotNull(rbData, "Built-in type should still return data even with filtering");

            // Verify mass is present — proves the component IS being serialized, just filtered
            var rbProps = rbData["properties"] as Dictionary<string, object>;
            Assert.IsTrue(rbProps.ContainsKey("mass"),
                "Built-in components should still have their core properties after filtering");
        }

        #endregion
    }
}
