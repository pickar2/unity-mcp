"""
Defines the inspect_buffer tool for reading ComputeBuffer/GraphicsBuffer contents.
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Inspects ComputeBuffer/GraphicsBuffer contents. BLOCKING - returns results immediately.

Uses reflection-based discovery. Target syntax:
- "GameObject/Component.fieldName" - find buffer by GameObject name
- "instanceId:12345/Component.fieldName" - find by instance ID (unambiguous)
- "*/Component.*" - discovery mode: list all matching buffers

Format syntax for decoding: "name:type@offset,..."
- Example: "position:float3@0,velocity:float3@16,mass:float@32,id:int@36"
- Supported types: float, float2, float3, float4, int, int2, int3, int4, uint, half
- If format omitted, returns raw bytes as base64

Use list_only=true to discover buffers before reading.""",
    annotations=ToolAnnotations(
        title="Inspect Buffer",
    ),
)
async def inspect_buffer(
    ctx: Context,
    target: Annotated[str, "Buffer location: 'GameObject/Component.field', 'instanceId:N/Component.field', or '*/Component.*' for discovery"],
    start: Annotated[int | str, "Starting element index (0-based). Defaults to 0."] | None = None,
    count: Annotated[int | str, "Number of elements to read. Defaults to 8."] | None = None,
    format: Annotated[str, "Format string: 'name:type@offset,...'. Example: 'position:float3@0,velocity:float3@16'. If omitted, returns raw base64."] | None = None,
    list_only: Annotated[bool | str, "If true, only list matching buffers without reading data. Use for discovery."] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "target": target,
        "start": coerce_int(start) if start is not None else None,
        "count": coerce_int(count) if count is not None else None,
        "format": format,
        "listOnly": coerce_bool(list_only, default=False),
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "inspect_buffer", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Buffer inspection complete."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error inspecting buffer: {str(e)}"}
