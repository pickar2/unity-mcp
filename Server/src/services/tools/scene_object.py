"""
Unified scene object interaction tool.

Provides a single tool for interacting with GameObjects in the Unity scene:
- list: Discover objects with filtering and pagination
- get: Read object state and component data
- set: Modify object properties (single or batch)
- create: Create new GameObjects
- delete: Delete objects (single or batch)

Uses path-based addressing (/Canvas/Panel/Button) for intuitive object targeting.
"""

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Interact with GameObjects in the Unity scene. Works in both edit mode and play mode.

Actions:
- list: Discover objects with filtering. Returns paginated flat list with paths.
- get: Read object state. Target can be name, path, or instance_id.
- set: Modify object properties. Supports batch operations via target_regex/tag/parent.
- create: Create new GameObject. Optionally with primitive type and components.
- delete: Delete objects. Supports batch operations.

Target Resolution:
- instance_id (int): Direct reference, always unique
- path (string with /): Full path like "/Canvas/Panel/Button", always unique
- name (string): Object name. Must be unique, otherwise error with list of matches

Batch Operations (for set/delete):
- target_regex: Regex pattern on full path
- tag: All objects with this tag
- parent: All direct children of this parent path

Examples:
  scene_object(action="list", tag="Enemy")
  scene_object(action="get", target="/Player", components=true)
  scene_object(action="set", target="Player", active=false, position=[10, 0, 5])
  scene_object(action="set", target_regex=".*Enemy", active=false)
  scene_object(action="create", name="Cube", primitive="Cube", position=[0, 1, 0])
  scene_object(action="delete", target="/Temp/Object")""",
    annotations=ToolAnnotations(
        title="Scene Object",
    ),
)
async def scene_object(
    ctx: Context,
    action: Annotated[
        Literal["list", "get", "set", "create", "delete"],
        "Action to perform. Default: get",
    ] = "get",
    target: Annotated[
        str | int | None,
        "Object reference: name, path, or instance_id. Required for get/set/delete (single).",
    ] = None,
    target_regex: Annotated[
        str | None, "Regex pattern on full path for batch operations."
    ] = None,
    tag: Annotated[
        str | None, "Filter by tag (list) or apply to all with tag (batch set/delete)."
    ] = None,
    parent: Annotated[
        str | None, "Parent path for create, or filter for list/batch operations."
    ] = None,
    component: Annotated[
        str | None,
        "Filter to objects having this component (list) or component type to modify (set).",
    ] = None,
    components: Annotated[
        bool | list[str] | None,
        "Include component data in response (get), or list of component types to add (create).",
    ] = None,
    properties: Annotated[
        dict | None, "Properties to set on object or component."
    ] = None,
    name: Annotated[
        str | None, "New name for set/rename, or object name for create."
    ] = None,
    active: Annotated[bool | None, "Set active state."] = None,
    position: Annotated[
        list[float] | dict | None, "World position [x,y,z] or {x,y,z}."
    ] = None,
    rotation: Annotated[
        list[float] | dict | None, "Euler rotation [x,y,z] or {x,y,z}."
    ] = None,
    scale: Annotated[list[float] | dict | None, "Scale [x,y,z] or {x,y,z}."] = None,
    layer: Annotated[int | str | None, "Layer number or name."] = None,
    primitive: Annotated[
        str | None,
        "Primitive type for create: Cube, Sphere, Capsule, Cylinder, Plane, Quad.",
    ] = None,
    depth: Annotated[
        int | None,
        "Max traversal depth for list. Default: 1 (direct children only), 0=unlimited.",
    ] = None,
    include_inactive: Annotated[
        bool | None, "Include inactive objects in list. Default: true."
    ] = None,
    page_size: Annotated[
        int | None, "Page size for list pagination. Default: 50."
    ] = None,
    cursor: Annotated[int | None, "Pagination cursor. Default: 0."] = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {"action": action}

    if target is not None:
        params["target"] = target
    if target_regex is not None:
        params["target_regex"] = target_regex
    if tag is not None:
        params["tag"] = tag
    if parent is not None:
        params["parent"] = parent
    if component is not None:
        params["component"] = component
    if name is not None:
        params["name"] = name
    if active is not None:
        params["active"] = active
    if position is not None:
        params["position"] = position
    if rotation is not None:
        params["rotation"] = rotation
    if scale is not None:
        params["scale"] = scale
    if layer is not None:
        params["layer"] = layer
    if primitive is not None:
        params["primitive"] = primitive
    if properties is not None:
        params["properties"] = properties
    if depth is not None:
        params["depth"] = depth
    if include_inactive is not None:
        params["include_inactive"] = include_inactive
    if page_size is not None:
        params["page_size"] = coerce_int(page_size)
    if cursor is not None:
        params["cursor"] = coerce_int(cursor)

    if components is not None:
        if action == "get":
            params["components"] = coerce_bool(components, default=False)
        elif action == "create" and isinstance(components, list):
            params["components"] = components

    resp = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "scene_object", params
    )
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
