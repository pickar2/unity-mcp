"""Scene hierarchy resource — quick overview of the active scene's object tree."""

from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_resource(
    uri="mcpforunity://scene/hierarchy",
    name="scene_hierarchy",
    description="Quick read-only overview of the active scene's GameObject hierarchy (root objects with child counts). Use manage_scene(action='get_hierarchy', parent=...) for deeper exploration.\n\nURI: mcpforunity://scene/hierarchy",
)
async def get_scene_hierarchy(ctx: Context) -> MCPResponse:
    unity_instance = await get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_scene",
        {"action": "get_hierarchy", "pageSize": 100},
    )
    if isinstance(response, dict) and response.get("success"):
        return MCPResponse(
            success=True,
            message="Scene hierarchy retrieved.",
            data=response.get("data"),
        )
    if isinstance(response, dict):
        return MCPResponse(**response)
    return MCPResponse(success=False, message=str(response))
