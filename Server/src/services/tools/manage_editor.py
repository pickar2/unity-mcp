import asyncio
import logging
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, coerce_int

logger = logging.getLogger(__name__)

# How long to wait for the domain reload to start after the deferred Refresh
_RELOAD_START_DELAY_S = 2.0
# Max time to wait for editor readiness after domain reload
_RELOAD_READY_TIMEOUT_S = 60.0


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
    action: Annotated[
        Literal[
            "telemetry_status",
            "telemetry_ping",
            "play",
            "pause",
            "stop",
            "step",
            "set_active_tool",
            "add_tag",
            "remove_tag",
            "add_layer",
            "remove_layer",
        ],
        "Get and update the Unity Editor state.",
    ],
    wait_for_completion: Annotated[
        bool | str,
        "Optional. If True, waits for certain actions (accepts true/false or 'true'/'false')",
    ]
    | None = None,
    tool_name: Annotated[str, "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str, "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str, "Layer name when adding and removing layers"]
    | None = None,
    recompile: Annotated[
        bool | str,
        "If true, trigger script recompilation before the action. Returns error if compilation fails (accepts true/false or 'true'/'false')",
    ]
    | None = None,
    frames: Annotated[
        int | str,
        "Number of frames to step (for 'step' action). Defaults to 1. Large values block until complete.",
    ]
    | None = None,
    paused: Annotated[
        bool | str,
        "If true with action='play', enter play mode immediately paused (for debugging). Accepts true/false or 'true'/'false'.",
    ]
    | None = None,
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
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_editor", params
        )

        # Handle deferred recompile: Unity returned before domain reload,
        # Python side waits for reconnection, verifies compilation, enters play mode.
        if isinstance(response, dict) and response.get("success"):
            data = response.get("data") or {}
            if isinstance(data, dict) and data.get("pending") == "recompile":
                return await _handle_deferred_recompile(ctx, unity_instance, data)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Editor operation successful."),
                "data": response.get("data"),
            }
        return (
            response
            if isinstance(response, dict)
            else {"success": False, "message": str(response)}
        )

    except Exception as e:
        return {"success": False, "message": f"Python error managing editor: {str(e)}"}


async def _handle_deferred_recompile(
    ctx: Context,
    unity_instance: str | None,
    pending_data: dict[str, Any],
) -> dict[str, Any]:
    """Wait for domain reload to complete, verify compilation, and optionally enter play mode.

    Called when Unity returned a deferred recompile response (Refresh queued via delayCall).
    The domain reload will kill the connection, so we wait for reconnection and then verify.
    """
    from services.tools.refresh_unity import wait_for_editor_ready

    enter_play = pending_data.get("enterPlayMode", False)
    is_paused = pending_data.get("paused", False)

    # Give Unity time to start the domain reload (delayCall fires next editor frame,
    # then Refresh triggers compilation which triggers domain reload).
    await asyncio.sleep(_RELOAD_START_DELAY_S)

    # Wait for Unity to come back after domain reload.
    # wait_for_editor_ready catches connection errors during the reload window.
    ready, elapsed = await wait_for_editor_ready(ctx, timeout_s=_RELOAD_READY_TIMEOUT_S)
    if not ready:
        return {
            "success": False,
            "message": f"Timed out waiting for recompilation to complete ({elapsed:.1f}s).",
        }

    # Check compilation result via editor state
    compilation_failed = await _check_compilation_failed(ctx)

    if compilation_failed:
        errors = await _get_console_errors(unity_instance)
        return {
            "success": False,
            "message": "Compilation failed. Check errors below.",
            "data": {
                "recompiled": True,
                "compilation_failed": True,
                "errors": errors,
            },
        }

    # Compilation succeeded — enter play mode if that was the original intent
    if enter_play:
        play_params: dict[str, Any] = {"action": "play"}
        if is_paused:
            play_params["paused"] = True

        play_resp = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_editor", play_params
        )

        if isinstance(play_resp, dict) and play_resp.get("success"):
            msg = play_resp.get("message", "Entered play mode.")
            return {
                "success": True,
                "message": f"Recompile succeeded. {msg}",
                "data": {"recompiled": True, "paused": is_paused},
            }
        # Play failed (e.g. script errors detected by Unity at play time)
        return (
            play_resp
            if isinstance(play_resp, dict)
            else {"success": False, "message": str(play_resp)}
        )

    return {
        "success": True,
        "message": f"Recompile succeeded ({elapsed:.1f}s).",
        "data": {"recompiled": True},
    }


async def _check_compilation_failed(ctx: Context) -> bool:
    """Check editor state for script compilation failure."""
    try:
        import services.resources.editor_state as editor_state_mod

        state_resp = await editor_state_mod.get_editor_state(ctx)
        state = (
            state_resp.model_dump() if hasattr(state_resp, "model_dump") else state_resp
        )
        if not isinstance(state, dict):
            return False
        data = state.get("data")
        if not isinstance(data, dict):
            return False
        compilation = data.get("compilation")
        if not isinstance(compilation, dict):
            return False
        return compilation.get("script_compilation_failed", False) is True
    except Exception as exc:
        logger.debug("Could not check compilation state: %s", exc)
        return False


async def _get_console_errors(unity_instance: str | None) -> list[dict]:
    """Fetch recent error entries from the Unity console."""
    try:
        resp = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "read_console",
            {"types": ["error"], "count": 20},
        )
        if isinstance(resp, dict) and resp.get("success"):
            data = resp.get("data", {})
            return data.get("entries", []) if isinstance(data, dict) else []
    except Exception as exc:
        logger.debug("Could not fetch console errors: %s", exc)
    return []
