from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Search and browse Unity Editor menu items. Returns menu paths that can be used with execute_menu_item.

Includes all menu items registered via [MenuItem] attribute — both project scripts and installed packages (URP, 2D, etc.).

Options:
- path_prefix: Filter to descendants of a menu path (e.g. "GameObject/Light" returns all light creation items)
- search: Case-insensitive text search across all menu paths (e.g. "Point Light" finds matching items)

Examples:
  list_menu_items(path_prefix="GameObject/Light")
  list_menu_items(search="2D")
  list_menu_items(path_prefix="GameObject", search="Light")""",
    annotations=ToolAnnotations(
        title="List Menu Items",
        readOnlyHint=True,
    ),
)
async def list_menu_items(
    ctx: Context,
    path_prefix: Annotated[
        str,
        "Filter to menu items under this path (e.g. 'GameObject/Light'). Trailing slash optional.",
    ]
    | None = None,
    search: Annotated[
        str,
        "Case-insensitive text search across menu item paths.",
    ]
    | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params = {}
    if path_prefix:
        params["pathPrefix"] = path_prefix
    if search:
        params["search"] = search

    return await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "list_menu_items",
        params,
    )
