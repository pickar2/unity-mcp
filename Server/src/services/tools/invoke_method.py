"""
Tool for invoking methods on Unity components or static types via reflection.

Supports:
- Instance methods on components attached to GameObjects
- Static methods on any resolvable type
- Automatic argument conversion (primitives, Vector3, Color, etc.)
- Return value serialization
"""

from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Invoke a method on a Unity component or static type via reflection.

Instance methods: provide target (GameObject) + component + method.
Static methods: provide type + method.

Arguments are converted automatically: primitives, Vector3 ([x,y,z]), Color, asset paths, etc.
Return values are serialized. Void methods return null.

Works in both edit mode and play mode.

Examples:
  invoke_method(target="Player", component="PlayerHealth", method="TakeDamage", args=[50])
  invoke_method(target="/Enemies/Goblin", component="EnemyAI", method="SetState", args=["idle"])
  invoke_method(type="GameManager", method="ResetLevel")
  invoke_method(target="Player", component="Inventory", method="AddItem", args=["sword", 1])""",
    annotations=ToolAnnotations(
        title="Invoke Method",
        destructiveHint=True,
    ),
)
async def invoke_method(
    ctx: Context,
    method: Annotated[str, "Method name to invoke."],
    target: Annotated[
        str | int | None,
        "GameObject reference: name, path, or instance_id (for instance methods).",
    ] = None,
    component: Annotated[
        str | None,
        "Component type name on the target GameObject (for instance methods).",
    ] = None,
    type: Annotated[
        str | None,
        "Fully qualified or short type name (for static methods). Use instead of target+component.",
    ] = None,
    args: Annotated[
        list[Any] | str | None,
        "Positional arguments for the method. JSON types are auto-converted to Unity types.",
    ] = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    parsed_args = parse_json_payload(args)

    params: dict[str, Any] = {"method": method}

    if target is not None:
        params["target"] = target
    if component is not None:
        params["component"] = component
    if type is not None:
        params["type"] = type
    if parsed_args is not None:
        params["args"] = parsed_args if isinstance(parsed_args, list) else args

    resp = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "invoke_method", params
    )
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
