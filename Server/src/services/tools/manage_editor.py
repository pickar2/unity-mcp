from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, coerce_int


@mcp_for_unity_tool(
    description="""Controls Unity editor play mode and settings. All actions are BLOCKING - no need to sleep or poll after calling.

RECOMMENDED WORKFLOW for entering play mode after code changes:
  Use action='play' with recompile=true - this compiles scripts, checks for errors, and enters play mode in ONE call.
  Do NOT manually call refresh_unity -> read_console -> play separately - that's 3 calls instead of 1.

Actions:
- play: Enter play mode. Use recompile=true after code changes to compile first and fail if errors. Use paused=true to start paused (for debugging).
- pause: Toggle pause state while in play mode.
- stop: Exit play mode.
- step: Advance simulation by N frames (requires play mode). Use 'frames' parameter.
- set_active_tool: Set editor tool (Move, Rotate, Scale, etc).
- add_tag/remove_tag: Manage project tags.
- add_layer/remove_layer: Manage project layers.
- telemetry_status/telemetry_ping: Diagnostics (read-only).

All actions complete before returning - no polling needed.""",
    annotations=ToolAnnotations(
        title="Manage Editor",
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "play", "pause", "stop", "step", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer"], "Get and update the Unity Editor state."],
    wait_for_completion: Annotated[bool | str,
                                   "Optional. If True, waits for certain actions (accepts true/false or 'true'/'false')"] | None = None,
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
    recompile: Annotated[bool | str,
                         "If true, trigger script recompilation before the action. Returns error if compilation fails (accepts true/false or 'true'/'false')"] | None = None,
    frames: Annotated[int | str,
                      "Number of frames to step (for 'step' action). Defaults to 1. Large values block until complete."] | None = None,
    paused: Annotated[bool | str,
                      "If true with action='play', enter play mode immediately paused (for debugging). Accepts true/false or 'true'/'false'."] | None = None,
) -> dict[str, Any]:
    # Get active instance from request state (injected by middleware)
    unity_instance = get_unity_instance_from_context(ctx)

    wait_for_completion = coerce_bool(wait_for_completion)
    recompile = coerce_bool(recompile)
    paused = coerce_bool(paused)

    try:
        # Diagnostics: quick telemetry checks
        if action == "telemetry_status":
            return {"success": True, "telemetry_enabled": is_telemetry_enabled()}

        if action == "telemetry_ping":
            record_tool_usage("diagnostic_ping", True, 1.0, None)
            return {"success": True, "message": "telemetry ping queued"}
        # Prepare parameters, removing None values
        params = {
            "action": action,
            "waitForCompletion": wait_for_completion,
            "toolName": tool_name,
            "tagName": tag_name,
            "layerName": layer_name,
            "recompile": recompile,
            "frames": coerce_int(frames) if frames is not None else None,
            "paused": paused,
        }
        params = {k: v for k, v in params.items() if v is not None}

        # Send command using centralized retry helper with instance routing
        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_editor", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing editor: {str(e)}"}
