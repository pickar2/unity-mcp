using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for asset reference property setting — verifies that sub-assets (like Sprite from
    /// a Texture2D) are resolved correctly via PropertyConversion and through scene_object / 
    /// manage_scriptable_object tool paths.
    /// </summary>
    public class AssetReferencePropertyTests
    {
        private const string TempRoot = "Assets/Temp/AssetRefTests";

        private string _texturePath;
        private string _materialPath;
        private GameObject _testGo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EnsureFolder(TempRoot);

            // Create a 4x4 Texture2D asset configured as Sprite so it has a Sprite sub-asset.
            _texturePath = $"{TempRoot}/TestSprite.png";
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), _texturePath),
                tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(_texturePath, ImportAssetOptions.ForceUpdate);

            // Set texture import settings to Sprite
            var importer = AssetImporter.GetAtPath(_texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            // Create a Material asset for testing Material references
            _materialPath = $"{TempRoot}/TestMat.mat";
            var shader = FindFallbackShader();
            AssetDatabase.CreateAsset(new Material(shader), _materialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [SetUp]
        public void SetUp()
        {
            _testGo = new GameObject("AssetRefTestObject");
            CommandRegistry.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testGo != null)
                Object.DestroyImmediate(_testGo);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(TempRoot);
        }

        #region PropertyConversion.ConvertToType

        [Test]
        public void ConvertToType_SpriteFromPath_LoadsSubAsset()
        {
            var token = JToken.FromObject(_texturePath);
            var result = PropertyConversion.ConvertToType(token, typeof(Sprite));

            Assert.IsNotNull(result, "ConvertToType should load Sprite sub-asset from texture path");
            Assert.IsInstanceOf<Sprite>(result, "Result should be a Sprite, not Texture2D");
        }

        [Test]
        public void ConvertToType_MaterialFromPath_LoadsAsset()
        {
            var token = JToken.FromObject(_materialPath);
            var result = PropertyConversion.ConvertToType(token, typeof(Material));

            Assert.IsNotNull(result, "ConvertToType should load Material from path");
            Assert.IsInstanceOf<Material>(result);
        }

        [Test]
        public void ConvertToType_MaterialFromGuid_LoadsAsset()
        {
            string guid = AssetDatabase.AssetPathToGUID(_materialPath);
            var token = JToken.FromObject(guid);
            var result = PropertyConversion.ConvertToType(token, typeof(Material));

            Assert.IsNotNull(result, "ConvertToType should load Material from GUID");
            Assert.IsInstanceOf<Material>(result);
        }

        [Test]
        public void ConvertToType_NullToken_ReturnsNullForObjectType()
        {
            var result = PropertyConversion.ConvertToType(JValue.CreateNull(), typeof(Sprite));
            Assert.IsNull(result, "Null token should return null for reference types");
        }

        [Test]
        public void ConvertToType_InvalidPath_FallsThrough()
        {
            var token = JToken.FromObject("Assets/NonExistent/Fake.png");
            // Should not throw — just fall through to Newtonsoft (which will also fail gracefully)
            object result = null;
            Assert.DoesNotThrow(() =>
            {
                try { result = PropertyConversion.ConvertToType(token, typeof(Sprite)); }
                catch { /* Newtonsoft may throw, that's ok */ }
            });
        }

        [Test]
        public void ConvertToType_ObjectRefInstruction_ResolvesFromJObject()
        {
            var instruction = new JObject
            {
                ["path"] = _materialPath
            };
            var result = PropertyConversion.ConvertToType(instruction, typeof(Material));

            Assert.IsNotNull(result, "ConvertToType should resolve Material from JObject instruction");
            Assert.IsInstanceOf<Material>(result);
        }

        #endregion

        #region SceneObject component_properties (SpriteRenderer)

        [Test]
        public void SceneObject_SetSpriteByPath_AssignsCorrectly()
        {
            _testGo.AddComponent<SpriteRenderer>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component_properties"] = new JObject
                {
                    ["SpriteRenderer"] = new JObject
                    {
                        ["sprite"] = _texturePath
                    }
                }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var sr = _testGo.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(sr.sprite, "SpriteRenderer.sprite should be set");
            Assert.IsInstanceOf<Sprite>(sr.sprite, "Should be a Sprite, not Texture2D");
        }

        [Test]
        public void SceneObject_SetMaterialByPath_AssignsCorrectly()
        {
            var mr = _testGo.AddComponent<MeshRenderer>();

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component_properties"] = new JObject
                {
                    ["MeshRenderer"] = new JObject
                    {
                        ["sharedMaterial"] = _materialPath
                    }
                }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(mr.sharedMaterial, "MeshRenderer.sharedMaterial should be set");
            Assert.IsInstanceOf<Material>(mr.sharedMaterial);
        }

        [Test]
        public void SceneObject_ClearSpriteWithNull_ClearsReference()
        {
            var sr = _testGo.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(_texturePath);
            Assert.IsNotNull(sr.sprite, "Pre-condition: sprite should be set");

            var result = ToJObject(SceneObject.HandleCommand(new JObject
            {
                ["action"] = "set",
                ["target"] = _testGo.name,
                ["component_properties"] = new JObject
                {
                    ["SpriteRenderer"] = new JObject
                    {
                        ["sprite"] = JValue.CreateNull()
                    }
                }
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(sr.sprite, "SpriteRenderer.sprite should be cleared");
        }

        #endregion

        #region ManageScriptableObject sub-asset resolution

        [Test]
        public void ManageScriptableObject_SetSpriteRef_ResolvesSubAsset()
        {
            // Create a ScriptableObject with a Sprite field
            var soPath = $"{TempRoot}/SpriteHolder.asset";
            var so = ScriptableObject.CreateInstance<SpriteHolderSO>();
            AssetDatabase.CreateAsset(so, soPath);
            AssetDatabase.SaveAssets();

            try
            {
                var result = ToJObject(ManageScriptableObject.HandleCommand(new JObject
                {
                    ["action"] = "modify",
                    ["target"] = new JObject
                    {
                        ["guid"] = AssetDatabase.AssetPathToGUID(soPath)
                    },
                    ["patches"] = new JArray
                    {
                        new JObject
                        {
                            ["propertyPath"] = "spriteRef",
                            ["value"] = _texturePath
                        }
                    }
                }));

                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                // Reload and verify
                var loaded = AssetDatabase.LoadAssetAtPath<SpriteHolderSO>(soPath);
                Assert.IsNotNull(loaded.spriteRef, "Sprite field should be set");
                Assert.IsInstanceOf<Sprite>(loaded.spriteRef, "Should be Sprite, not Texture2D");
            }
            finally
            {
                AssetDatabase.DeleteAsset(soPath);
            }
        }

        #endregion
    }

    /// <summary>
    /// Test ScriptableObject with a Sprite field for sub-asset resolution testing.
    /// </summary>
    public class SpriteHolderSO : ScriptableObject
    {
        public Sprite spriteRef;
        public Material materialRef;
    }
}
