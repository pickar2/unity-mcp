using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class InspectComponentTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        #region List Action

        [Test]
        public void List_NoFilters_ReturnsComponentTypes()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list"
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            int total = result["data"].Value<int>("total");
            Assert.IsTrue(total > 0, "Should find at least some component types");
            Assert.IsNotNull(result["data"]["types"]);
        }

        [Test]
        public void List_WithSearch_FiltersResults()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["search"] = "Rigidbody"
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var types = result["data"]["types"] as JArray;
            Assert.IsNotNull(types);
            Assert.IsTrue(types.Count > 0, "Should find Rigidbody types");

            foreach (var t in types)
            {
                string typeName = t.Value<string>("typeName") ?? "";
                string fullName = t.Value<string>("fullName") ?? "";
                Assert.IsTrue(
                    typeName.Contains("Rigidbody") || fullName.Contains("Rigidbody"),
                    $"Type '{typeName}' should match 'Rigidbody' search");
            }
        }

        [Test]
        public void List_WithCategory_FiltersResults()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["category"] = "Physics"
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var types = result["data"]["types"] as JArray;
            Assert.IsTrue(types.Count > 0, "Should find physics component types");

            foreach (var t in types)
            {
                string category = t.Value<string>("category");
                Assert.IsTrue(
                    category.Contains("Physics"),
                    $"Category '{category}' should contain 'Physics'");
            }
        }

        [Test]
        public void List_Pagination_Works()
        {
            var page0 = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["pageSize"] = 5,
                ["page"] = 0
            }));

            Assert.IsTrue(page0.Value<bool>("success"));
            var types0 = page0["data"]["types"] as JArray;
            Assert.AreEqual(5, types0.Count, "Page 0 should have exactly 5 items");
            int total = page0["data"].Value<int>("total");
            Assert.IsTrue(total > 5, "Total should be > 5 for pagination test to be meaningful");
            Assert.AreEqual(1, page0["data"].Value<int>("nextPage"));

            var page1 = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["pageSize"] = 5,
                ["page"] = 1
            }));

            var types1 = page1["data"]["types"] as JArray;
            Assert.AreEqual(5, types1.Count);

            // Pages should have different items
            string firstName0 = types0[0].Value<string>("typeName");
            string firstName1 = types1[0].Value<string>("typeName");
            Assert.AreNotEqual(firstName0, firstName1, "Different pages should have different items");
        }

        [Test]
        public void List_TypeInfo_HasRequiredFields()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["search"] = "Rigidbody",
                ["pageSize"] = 1
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var types = result["data"]["types"] as JArray;
            Assert.IsTrue(types.Count > 0);

            var first = types[0] as JObject;
            Assert.IsNotNull(first["typeName"], "Should have typeName");
            Assert.IsNotNull(first["fullName"], "Should have fullName");
            Assert.IsNotNull(first["category"], "Should have category");
            Assert.IsNotNull(first["assembly"], "Should have assembly");
        }

        [Test]
        public void List_NonexistentSearch_ReturnsEmpty()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "list",
                ["search"] = "ZZZZZZ_NONEXISTENT_TYPE_ZZZZZZ"
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(0, result["data"].Value<int>("total"));
        }

        #endregion

        #region Schema Action

        [Test]
        public void Schema_Rigidbody_ReturnsProperties()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema",
                ["typeName"] = "Rigidbody"
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            var data = result["data"] as JObject;
            Assert.AreEqual("Rigidbody", data.Value<string>("typeName"));
            Assert.IsNotNull(data["fullName"]);
            Assert.IsNotNull(data["assembly"]);

            var properties = data["properties"] as JArray;
            Assert.IsNotNull(properties);
            Assert.IsTrue(properties.Count > 0, "Rigidbody should have properties");

            // Check that mass property exists
            var massProp = properties.Cast<JObject>()
                .FirstOrDefault(p => p.Value<string>("name") == "m_Mass" ||
                                     p.Value<string>("displayName") == "Mass");
            Assert.IsNotNull(massProp, "Should have mass property");
        }

        [Test]
        public void Schema_Light_HasEnumValues()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema",
                ["typeName"] = "Light"
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            var properties = result["data"]["properties"] as JArray;

            // Find an enum property (Light has m_Type for LightType)
            var enumProp = properties.Cast<JObject>()
                .FirstOrDefault(p => p.Value<string>("type") == "Enum");
            Assert.IsNotNull(enumProp, "Light should have at least one enum property");
            Assert.IsNotNull(enumProp["enumValues"], "Enum property should have enumValues");

            var enumValues = enumProp["enumValues"] as JObject;
            Assert.IsTrue(enumValues.Count > 0, "Enum should have at least one value");
        }

        [Test]
        public void Schema_WithoutEnums_OmitsEnumValues()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema",
                ["typeName"] = "Light",
                ["includeEnumValues"] = false
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var properties = result["data"]["properties"] as JArray;

            var enumProp = properties.Cast<JObject>()
                .FirstOrDefault(p => p.Value<string>("type") == "Enum");
            if (enumProp != null)
            {
                Assert.IsNull(enumProp["enumValues"],
                    "includeEnumValues=false should omit enum values");
            }
        }

        [Test]
        public void Schema_WithDefaults_IncludesDefaultValues()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema",
                ["typeName"] = "Rigidbody",
                ["includeDefaults"] = true
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var properties = result["data"]["properties"] as JArray;

            // At least some properties should have defaults
            bool anyDefaults = properties.Cast<JObject>()
                .Any(p => p["default"] != null && p["default"].Type != JTokenType.Null);
            Assert.IsTrue(anyDefaults, "Should have at least some default values");
        }

        [Test]
        public void Schema_InvalidType_ReturnsError()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema",
                ["typeName"] = "ZZZZZZ_NONEXISTENT_ZZZZZZ"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("not found"));
        }

        [Test]
        public void Schema_MissingTypeName_ReturnsError()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("type_name"));
        }

        [Test]
        public void Schema_ShortName_ResolvesCorrectly()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "schema",
                ["typeName"] = "BoxCollider"
            }));

            Assert.IsTrue(result.Value<bool>("success"), $"Expected success: {result}");
            Assert.AreEqual("BoxCollider", result["data"].Value<string>("typeName"));
            StringAssert.Contains("UnityEngine", result["data"].Value<string>("fullName"));
        }

        #endregion

        #region Edge Cases

        [Test]
        public void UnknownAction_ReturnsError()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject
            {
                ["action"] = "delete"
            }));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.IsTrue(result.Value<string>("error").Contains("Unknown action"));
        }

        [Test]
        public void DefaultAction_IsList()
        {
            var result = ToJO(InspectComponent.HandleCommand(new JObject()));

            Assert.IsTrue(result.Value<bool>("success"),
                "No action param should default to 'list'");
        }

        #endregion
    }
}
