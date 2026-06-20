using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class ListMenuItemsTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void NoParams_ReturnsSuccess()
        {
            var res = ListMenuItems.HandleCommand(new JObject());
            var jo = ToJO(res);
            Assert.IsTrue(jo.Value<bool>("success"), $"Expected success: {jo}");
            Assert.IsNotNull(jo["data"]?["items"]);
            Assert.IsTrue(jo["data"].Value<int>("total") >= 0);
        }

        [Test]
        public void WithSearch_FiltersResults()
        {
            var allResult = ToJO(ListMenuItems.HandleCommand(new JObject()));
            int allCount = allResult["data"].Value<int>("total");

            var filteredResult = ToJO(ListMenuItems.HandleCommand(new JObject
            {
                ["search"] = "ZZZZZ_NONEXISTENT_MENU_ITEM_ZZZZZ"
            }));
            int filteredCount = filteredResult["data"].Value<int>("total");

            Assert.AreEqual(0, filteredCount, "Nonexistent search should return 0 results");
            Assert.IsTrue(allCount >= filteredCount);
        }

        [Test]
        public void WithPathPrefix_FiltersToDescendants()
        {
            // Get items under a known prefix
            var result = ToJO(ListMenuItems.HandleCommand(new JObject
            {
                ["pathPrefix"] = "ZZZZZ_NONEXISTENT_PREFIX"
            }));
            int count = result["data"].Value<int>("total");
            Assert.AreEqual(0, count, "Nonexistent prefix should return 0 results");
        }

        [Test]
        public void WithPathPrefixAndSearch_CombinesFilters()
        {
            var result = ToJO(ListMenuItems.HandleCommand(new JObject
            {
                ["pathPrefix"] = "ZZZZZ",
                ["search"] = "YYYYY"
            }));
            int count = result["data"].Value<int>("total");
            Assert.AreEqual(0, count, "Combined nonexistent filters should return 0");
        }

        [Test]
        public void Items_AreSortedStrings()
        {
            var result = ToJO(ListMenuItems.HandleCommand(new JObject()));
            var items = result["data"]["items"] as JArray;
            if (items != null && items.Count > 1)
            {
                for (int i = 1; i < items.Count; i++)
                {
                    string prev = items[i - 1].ToString();
                    string curr = items[i].ToString();
                    Assert.IsTrue(string.CompareOrdinal(prev, curr) <= 0,
                        $"Items not sorted: '{prev}' should come before '{curr}'");
                }
            }
        }
    }
}
