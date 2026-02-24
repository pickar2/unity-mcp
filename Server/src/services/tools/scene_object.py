"""
Unified scene object interaction tool.

Provides a single tool for interacting with GameObjects in the Unity scene:
- list: Discover objects with filtering and pagination
- get: Read object state and component data
- set: Modify object properties (single or batch), add/remove components
- create: Create new GameObjects
- delete: Delete objects (single or batch)
- duplicate: Clone existing objects
- move_relative: Move objects relative to a reference object

Uses path-based addressing (/Canvas/Panel/Button) for intuitive object targeting.
"""

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Interact with GameObjects in the Unity scene. Works in both edit mode and play mode.

Actions:
- list: Discover objects with filtering (tag, layer, component, regex, parent). Returns paginated flat list with paths.
- get: Read object state and optionally full component data. Target can be name, path, or instance_id.
- set: Modify object properties. Supports batch via target_regex/tag/parent. Can add/remove components.
- create: Create new GameObject with optional primitive type, components, transform.
- delete: Delete objects. Supports batch operations.
- duplicate: Clone a GameObject with optional new name, position offset, parent.
- move_relative: Move object relative to a reference object by direction or offset.

Target Resolution:
- instance_id (int): Direct reference, always unique
- path (string with /): Full path like "Canvas/Panel/Button", always unique
- name (string): Object name. Must be unique, otherwise error with list of matches

Batch Operations (for set/delete):
- target_regex: Regex pattern on full path
- tag: All objects with this tag
- parent: All direct children of this parent path

Position/rotation params use LOCAL coordinates (relative to parent).
The get response returns both local (position, rotation) and world (world_position, world_rotation).
To unparent an object (move to scene root), set parent="/".

Examples:
  scene_object(action="list", tag="Enemy")
  scene_object(action="list", layer="Water", depth=0)
  scene_object(action="get", target="/Player", components=true)
  scene_object(action="get", target="/Player", component="SpriteRenderer", properties=["sprite", "color"])
  scene_object(action="get", target="/Player", components=["SpriteRenderer", "Rigidbody2D"])
  scene_object(action="get", target="/Player", component="Rigidbody", include_internal=true)
  scene_object(action="set", target="Player", active=false, position=[10, 0, 5])
  scene_object(action="set", target="Player", add_components=["Rigidbody", "BoxCollider"])
  scene_object(action="set", target="Player", add_components=[{"typeName": "Rigidbody", "properties": {"mass": 10}}])
  scene_object(action="set", target="Player", remove_components=["BoxCollider"])
  scene_object(action="set", target="Player", component="Rigidbody", properties={"mass": 10})
  scene_object(action="set", target_regex=".*Enemy", active=false)
  scene_object(action="set", target="Child", parent="/")
  scene_object(action="create", name="Cube", primitive="Cube", position=[0, 1, 0], is_static=true)
  scene_object(action="delete", target="/Temp/Object")
  scene_object(action="duplicate", target="Player", name="Player2", offset=[5, 0, 0], rotation=[0, 180, 0])
  scene_object(action="move_relative", target="Chair", reference="Table", direction="right", distance=2)""",
    annotations=ToolAnnotations(
        title="Scene Object",
    ),
)
async def scene_object(
    ctx: Context,
    action: Annotated[
        Literal["list", "get", "set", "create", "delete", "duplicate", "move_relative"],
        "Action to perform. Default: get",
    ] = "get",
    target: Annotated[
        str | int | None, "Object reference: name, path, or instance_id."
    ] = None,
    target_regex: Annotated[
        str | None, "Regex pattern on full path for batch operations."
    ] = None,
    tag: Annotated[
        str | None,
        "Filter by tag (list) or apply to all with tag (batch set/delete). Auto-creates missing tags.",
    ] = None,
    parent: Annotated[
        str | None,
        'Parent path for create/reparent, or filter for list/batch operations. Use "/" to unparent (move to scene root).',
    ] = None,
    component: Annotated[
        str | None,
        "Filter to objects having this component (list) or component type to modify properties on (set).",
    ] = None,
    components: Annotated[
        bool | list | str | None,
        "Include component data in response (get: bool or list of type names to filter). "
        'For create: list of components to add, each a string or {"typeName": "Rigidbody", "properties": {"mass": 10}}.',
    ] = None,
    add_components: Annotated[
        list[str | dict] | None,
        "List of components to add (set action). Each element can be a string type name "
        'or {"typeName": "Rigidbody", "properties": {"mass": 10}} to add with initial properties.',
    ] = None,
    remove_components: Annotated[
        list[str] | None, "List of component types to remove from object (set action)."
    ] = None,
    component_properties: Annotated[
        dict | None,
        "Set properties on multiple components: {'Rigidbody': {'mass': 10}, 'Collider': {'isTrigger': true}}",
    ] = None,
    properties: Annotated[
        list | dict | str | None,
        "For get: list of property names to read (e.g. ['sprite', 'color']). "
        "For set: dict of property values (e.g. {'mass': 10}). "
        "Also accepts JSON string.",
    ] = None,
    name: Annotated[
        str | None, "New name for set/rename/duplicate, or object name for create."
    ] = None,
    active: Annotated[bool | None, "Set active state."] = None,
    is_static: Annotated[
        bool | None, "Set static flag (enables static batching, lightmapping, etc.)."
    ] = None,
    position: Annotated[
        list[float] | dict | None, "Local position [x,y,z] or {x,y,z}."
    ] = None,
    rotation: Annotated[
        list[float] | dict | None, "Local euler rotation [x,y,z] or {x,y,z}."
    ] = None,
    scale: Annotated[
        list[float] | dict | None, "Local scale [x,y,z] or {x,y,z}."
    ] = None,
    layer: Annotated[
        int | str | None,
        "Layer number or name. For list: filter. For set/create: assign.",
    ] = None,
    primitive: Annotated[
        str | None,
        "Primitive type for create: Cube, Sphere, Capsule, Cylinder, Plane, Quad.",
    ] = None,
    offset: Annotated[
        list[float] | dict | None, "Position offset for duplicate or move_relative."
    ] = None,
    reference: Annotated[
        str | None, "Reference object for move_relative action."
    ] = None,
    direction: Annotated[
        str | None, "Direction for move_relative: right, left, up, down, forward, back."
    ] = None,
    distance: Annotated[
        float | None, "Distance for move_relative. Default: 1.0."
    ] = None,
    world_space: Annotated[
        bool | None, "Use world space for move_relative. Default: true."
    ] = None,
    depth: Annotated[
        int | None,
        "Max traversal depth for list. Default: 1 (direct children only), 0=unlimited.",
    ] = None,
    include_inactive: Annotated[
        bool | None, "Include inactive objects in list. Default: true."
    ] = None,
    include_internal: Annotated[
        bool | None,
        "Include engine-internal properties (lightmap, raytracing, LOD, layer masks, deprecated aliases). Default: false.",
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
    if is_static is not None:
        params["is_static"] = is_static
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
    if offset is not None:
        params["offset"] = offset
    if reference is not None:
        params["reference"] = reference
    if direction is not None:
        params["direction"] = direction
    if distance is not None:
        params["distance"] = distance
    if world_space is not None:
        params["world_space"] = world_space
    if depth is not None:
        params["depth"] = depth
    if include_inactive is not None:
        params["include_inactive"] = include_inactive
    if page_size is not None:
        params["page_size"] = coerce_int(page_size)
    if cursor is not None:
        params["cursor"] = coerce_int(cursor)
    if add_components is not None:
        params["add_components"] = add_components
    if remove_components is not None:
        params["remove_components"] = remove_components
    if component_properties is not None:
        params["component_properties"] = component_properties

    if components is not None:
        if action == "get":
            if isinstance(components, list):
                params["components"] = components
            else:
                params["components"] = coerce_bool(components, default=False)
        elif action == "create" and isinstance(components, list):
            params["components"] = components

    # For get: component filter implies components=true on the C# side
    if action == "get" and component is not None and components is None:
        params["components"] = True

    if action == "get":
        include_int = coerce_bool(include_internal, default=None)
        if include_int is not None:
            params["includeInternal"] = include_int

    resp = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "scene_object", params
    )
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
