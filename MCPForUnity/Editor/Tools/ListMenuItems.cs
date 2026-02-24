using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.MenuItems;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("list_menu_items", AutoRegister = false)]
    public static class ListMenuItems
    {
        public static object HandleCommand(JObject @params)
        {
            string pathPrefix = @params["path_prefix"]?.ToString() ?? @params["pathPrefix"]?.ToString();
            string search = @params["search"]?.ToString();
            bool refresh = @params["refresh"]?.ToObject<bool>() ?? false;

            var items = GetMenuItems.GetMenuItemsInternal(refresh);

            if (!string.IsNullOrEmpty(pathPrefix))
            {
                string prefix = pathPrefix.TrimEnd('/') + "/";
                items = items
                    .Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrEmpty(search))
            {
                items = items
                    .Where(item => item.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            return new SuccessResponse(
                $"Found {items.Count} menu items.",
                new { total = items.Count, items }
            );
        }
    }
}
