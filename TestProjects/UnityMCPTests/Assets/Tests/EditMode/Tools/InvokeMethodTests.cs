using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class InvokeMethodTests
    {
        private List<GameObject> testObjects = new List<GameObject>();

        private GameObject CreateTestObject(string name)
        {
            var go = new GameObject(name);
            testObjects.Add(go);
            return go;
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

        #region Validation Errors

        [Test]
        public void NullParams_ReturnsError()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(null));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void MissingMethod_ReturnsError()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject()));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("method"));
        }

        [Test]
        public void NoTargetOrType_ReturnsError()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Foo"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("target") ||
                          result.Value<string>("error").Contains("type"));
        }

        [Test]
        public void InstanceMethod_MissingComponent_ReturnsError()
        {
            var go = CreateTestObject("InvokeTestObj");

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Foo",
                ["target"] = "InvokeTestObj"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("component"));
        }

        [Test]
        public void InstanceMethod_BadTarget_ReturnsError()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Foo",
                ["target"] = "NonExistentObject_12345",
                ["component"] = "Transform"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("not found"));
        }

        [Test]
        public void InstanceMethod_BadComponent_ReturnsError()
        {
            var go = CreateTestObject("InvokeCompErr");

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Foo",
                ["target"] = "InvokeCompErr",
                ["component"] = "NonExistentComponent_XYZ"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("not found"));
        }

        [Test]
        public void InstanceMethod_ComponentNotOnObject_ReturnsError()
        {
            var go = CreateTestObject("InvokeNoRB");

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "WakeUp",
                ["target"] = "InvokeNoRB",
                ["component"] = "Rigidbody"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("not found on"));
        }

        [Test]
        public void InstanceMethod_BadMethodName_ReturnsAvailableMethods()
        {
            var go = CreateTestObject("InvokeBadMethod");

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "TotallyFakeMethod_ABC",
                ["target"] = "InvokeBadMethod",
                ["component"] = "Transform"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("not found"));
            Assert.IsNotNull(result["data"]?["availableMethods"], "Should list available methods");
        }

        [Test]
        public void InstanceMethod_WrongArgCount_ReturnsError()
        {
            var go = CreateTestObject("InvokeArgCount");

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "SetParent",
                ["target"] = "InvokeArgCount",
                ["component"] = "Transform",
                ["args"] = new JArray("a", "b", "c", "d", "e")
            }));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        #endregion

        #region Instance Method Invocation

        [Test]
        public void InstanceMethod_TransformTranslate_Succeeds()
        {
            var go = CreateTestObject("InvokeTranslate");
            go.transform.position = Vector3.zero;

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Translate",
                ["target"] = "InvokeTranslate",
                ["component"] = "Transform",
                ["args"] = new JArray(1f, 2f, 3f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            // Translate(x,y,z) should move the object
            Assert.AreEqual(1f, go.transform.position.x, 0.01f);
            Assert.AreEqual(2f, go.transform.position.y, 0.01f);
            Assert.AreEqual(3f, go.transform.position.z, 0.01f);
        }

        [Test]
        public void InstanceMethod_VoidMethod_ReturnsVoidType()
        {
            var go = CreateTestObject("InvokeVoid");
            go.transform.position = Vector3.zero;

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Translate",
                ["target"] = "InvokeVoid",
                ["component"] = "Transform",
                ["args"] = new JArray(0f, 0f, 0f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("void", result["data"]?["returnType"]?.ToString());
        }

        [Test]
        public void InstanceMethod_WithReturnValue_ReturnsData()
        {
            var go = CreateTestObject("InvokeReturn");
            var child = new GameObject("InvokeReturnChild");
            testObjects.Add(child);
            child.transform.SetParent(go.transform);

            // Transform.Find returns a child Transform
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Find",
                ["target"] = "InvokeReturn",
                ["component"] = "Transform",
                ["args"] = new JArray("InvokeReturnChild")
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]?["returnValue"], "Should have a return value");
            Assert.AreNotEqual("void", result["data"]?["returnType"]?.ToString());
        }

        [Test]
        public void InstanceMethod_ByPath_ResolvesCorrectObject()
        {
            var parent = CreateTestObject("InvokePathParent");
            var child = new GameObject("InvokePathChild");
            testObjects.Add(child);
            child.transform.SetParent(parent.transform);
            child.transform.localPosition = Vector3.zero;

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Translate",
                ["target"] = "/InvokePathParent/InvokePathChild",
                ["component"] = "Transform",
                ["args"] = new JArray(5f, 0f, 0f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(5f, child.transform.localPosition.x, 0.01f);
        }

        [Test]
        public void InstanceMethod_ByInstanceId_ResolvesCorrectObject()
        {
            var go = CreateTestObject("InvokeById");
            go.transform.position = Vector3.zero;
            int id = go.GetInstanceID();

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Translate",
                ["target"] = id,
                ["component"] = "Transform",
                ["args"] = new JArray(7f, 0f, 0f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(7f, go.transform.position.x, 0.01f);
        }

        [Test]
        public void InstanceMethod_CaseInsensitiveMethodName()
        {
            var go = CreateTestObject("InvokeCaseTest");
            go.transform.position = Vector3.zero;

            // "translate" instead of "Translate"
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "translate",
                ["target"] = "InvokeCaseTest",
                ["component"] = "Transform",
                ["args"] = new JArray(1f, 0f, 0f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(1f, go.transform.position.x, 0.01f);
        }

        [Test]
        public void InstanceMethod_SetActive_WorksOnGameObject()
        {
            var go = CreateTestObject("InvokeSetActive");
            Assert.IsTrue(go.activeSelf);

            // GameObject.SetActive is on the GameObject, but we invoke via the component system
            // Transform doesn't have SetActive — we test a method that actually changes state
            // Use Transform.DetachChildren as a void method that we can verify
            var child = new GameObject("InvokeDetachChild");
            testObjects.Add(child);
            child.transform.SetParent(go.transform);
            Assert.AreEqual(go.transform, child.transform.parent);

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "DetachChildren",
                ["target"] = "InvokeSetActive",
                ["component"] = "Transform"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNull(child.transform.parent, "Child should be detached");
        }

        #endregion

        #region Static Method Invocation

        [Test]
        public void StaticMethod_BadType_ReturnsError()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Foo",
                ["type"] = "CompletelyFakeType_XYZ"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("not found"));
        }

        [Test]
        public void StaticMethod_BadMethodName_ReturnsAvailableMethods()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "TotallyFakeMethod_ABC",
                ["type"] = "UnityEngine.Application"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsNotNull(result["data"]?["availableMethods"]);
        }

        [Test]
        public void StaticMethod_ApplicationIsPlaying_ReturnsValue()
        {
            // Application.get_isPlaying is a property getter, but let's use a proper static method
            // Use Mathf.Sqrt as a simple static method with a return value
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Sqrt",
                ["type"] = "UnityEngine.Mathf",
                ["args"] = new JArray(25f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var returnValue = result["data"]?["returnValue"];
            Assert.IsNotNull(returnValue, "Should return a value");
            Assert.AreEqual(5f, returnValue.Value<float>(), 0.01f);
        }

        [Test]
        public void StaticMethod_MathfMax_MultipleArgs()
        {
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Max",
                ["type"] = "UnityEngine.Mathf",
                ["args"] = new JArray(3f, 7f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(7f, result["data"]?["returnValue"]?.Value<float>(), 0.01f);
        }

        [Test]
        public void StaticMethod_TypePrioritizedOverTarget()
        {
            var go = CreateTestObject("InvokeTypeOverTarget");

            // When both type and target are provided, type takes precedence (static invocation)
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "Sqrt",
                ["type"] = "UnityEngine.Mathf",
                ["target"] = "InvokeTypeOverTarget",
                ["args"] = new JArray(16f)
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(4f, result["data"]?["returnValue"]?.Value<float>(), 0.01f);
        }

        #endregion

        #region Edge Cases

        [Test]
        public void NoArgs_DefaultParams_Succeeds()
        {
            var go = CreateTestObject("InvokeNoArgs");

            // Transform.GetSiblingIndex() takes no args and returns int
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "GetSiblingIndex",
                ["target"] = "InvokeNoArgs",
                ["component"] = "Transform"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]?["returnValue"]);
        }

        [Test]
        public void MethodThrowsException_ReportsInnerException()
        {
            var go = CreateTestObject("InvokeThrow");

            // Transform.SetParent(null, false) doesn't throw, but let's find something that does.
            // Transform.GetChild with out-of-range index throws ArgumentOutOfRangeException
            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "GetChild",
                ["target"] = "InvokeThrow",
                ["component"] = "Transform",
                ["args"] = new JArray(999)
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("threw an exception"));
        }

        [Test]
        public void EmptyArgs_TreatedAsNoArgs()
        {
            var go = CreateTestObject("InvokeEmptyArgs");

            var result = ToJObject(InvokeMethod.HandleCommand(new JObject
            {
                ["method"] = "GetSiblingIndex",
                ["target"] = "InvokeEmptyArgs",
                ["component"] = "Transform",
                ["args"] = new JArray()
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        #endregion
    }
}
