"""Scene Object CLI commands - unified scene interaction."""

import json
import click
from typing import Optional, Tuple, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.confirmation import confirm_destructive_action


@click.group()
def scene_obj():
    """Scene object operations - list, get, set, create, delete GameObjects."""
    pass


@scene_obj.command("list")
@click.option("--regex", "-r", default=None, help="Filter by path regex pattern.")
@click.option("--tag", "-t", default=None, help="Filter by tag.")
@click.option("--parent", "-p", default=None, help="List children of this parent path.")
@click.option(
    "--component", "-c", default=None, help="Filter to objects having this component."
)
@click.option(
    "--depth",
    "-d",
    default=1,
    type=int,
    help="Traversal depth. Default: 1 (direct children), 0=unlimited.",
)
@click.option(
    "--include-inactive/--no-inactive",
    default=True,
    help="Include inactive objects. Default: true.",
)
@click.option("--limit", "-l", default=50, type=int, help="Maximum results per page.")
@click.option("--cursor", default=0, type=int, help="Pagination cursor.")
@handle_unity_errors
def list_objects(
    regex: Optional[str],
    tag: Optional[str],
    parent: Optional[str],
    component: Optional[str],
    depth: int,
    include_inactive: bool,
    limit: int,
    cursor: int,
):
    """List GameObjects in the scene.

    \b
    Examples:
        unity-mcp scene-obj list
        unity-mcp scene-obj list --tag Enemy
        unity-mcp scene-obj list --parent /Canvas --depth 2
        unity-mcp scene-obj list --regex ".*Enemy.*"
        unity-mcp scene-obj list --component Rigidbody
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "list",
        "depth": depth,
        "include_inactive": include_inactive,
        "page_size": limit,
        "cursor": cursor,
    }

    if regex:
        params["target_regex"] = regex
    if tag:
        params["tag"] = tag
    if parent:
        params["parent"] = parent
    if component:
        params["component"] = component

    result = run_command("scene_object", params, config)
    click.echo(format_output(result, config.format))


@scene_obj.command("get")
@click.argument("target")
@click.option(
    "--components/--no-components", default=False, help="Include full component data."
)
@handle_unity_errors
def get_object(target: str, components: bool):
    """Get details of a GameObject.

    TARGET can be name, path (with /), or instance ID.

    \b
    Examples:
        unity-mcp scene-obj get Player
        unity-mcp scene-obj get /Canvas/Panel/Button
        unity-mcp scene-obj get 12345 --components
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get",
        "target": target,
        "components": components,
    }

    result = run_command("scene_object", params, config)
    click.echo(format_output(result, config.format))


@scene_obj.command("set")
@click.argument("target", required=False)
@click.option(
    "--regex",
    "-r",
    default=None,
    help="Apply to all objects matching path regex (batch).",
)
@click.option(
    "--tag", "-t", default=None, help="Apply to all objects with tag (batch)."
)
@click.option(
    "--parent", "-p", default=None, help="Apply to all children of parent (batch)."
)
@click.option("--name", "-n", default=None, help="New name for the object.")
@click.option("--active/--inactive", default=None, help="Set active state.")
@click.option(
    "--position",
    "-pos",
    nargs=3,
    type=float,
    default=None,
    help="World position as X Y Z.",
)
@click.option(
    "--rotation",
    "-rot",
    nargs=3,
    type=float,
    default=None,
    help="Euler rotation as X Y Z.",
)
@click.option(
    "--scale", "-s", nargs=3, type=float, default=None, help="Scale as X Y Z."
)
@click.option("--reparent", default=None, help="New parent path.")
@click.option("--set-tag", default=None, help="New tag.")
@click.option("--layer", default=None, help="New layer (number or name).")
@click.option("--component", default=None, help="Component type to modify.")
@click.option(
    "--properties",
    default=None,
    help="Properties to set as JSON. E.g., '{\"mass\": 10}'",
)
@handle_unity_errors
def set_object(
    target: Optional[str],
    regex: Optional[str],
    tag: Optional[str],
    parent: Optional[str],
    name: Optional[str],
    active: Optional[bool],
    position: Optional[Tuple[float, float, float]],
    rotation: Optional[Tuple[float, float, float]],
    scale: Optional[Tuple[float, float, float]],
    reparent: Optional[str],
    set_tag: Optional[str],
    layer: Optional[str],
    component: Optional[str],
    properties: Optional[str],
):
    """Modify a GameObject (single or batch).

    TARGET is required for single object. Use --regex, --tag, or --parent for batch.

    \b
    Examples:
        unity-mcp scene-obj set Player --active
        unity-mcp scene-obj set Player --position 10 0 5
        unity-mcp scene-obj set /Player --component Rigidbody --properties '{"mass": 10}'
        unity-mcp scene-obj set --regex ".*Enemy" --active false
        unity-mcp scene-obj set --tag Enemy --active false
    """
    config = get_config()

    is_batch = any([regex, tag, parent])

    if not is_batch and not target:
        print_error("TARGET is required for single object mode.")
        return

    params: dict[str, Any] = {"action": "set"}

    if target:
        params["target"] = target
    if regex:
        params["target_regex"] = regex
    if tag:
        params["tag"] = tag
    if parent:
        params["parent"] = parent
    if name:
        params["name"] = name
    if active is not None:
        params["active"] = active
    if position:
        params["position"] = list(position)
    if rotation:
        params["rotation"] = list(rotation)
    if scale:
        params["scale"] = list(scale)
    if reparent:
        params["parent"] = reparent
    if set_tag:
        params["tag"] = set_tag
    if layer:
        params["layer"] = layer
    if component:
        params["component"] = component
    if properties:
        try:
            params["properties"] = json.loads(properties)
        except json.JSONDecodeError as e:
            print_error(f"Invalid JSON properties: {e}")
            return

    result = run_command("scene_object", params, config)
    click.echo(format_output(result, config.format))

    if result.get("success"):
        data = result.get("data", {})
        count = data.get("count", 1)
        if is_batch:
            print_success(f"Updated {count} objects")
        else:
            print_success(f"Updated object '{target}'")


@scene_obj.command("create")
@click.argument("name")
@click.option(
    "--primitive",
    "-p",
    type=click.Choice(["Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"]),
    default=None,
    help="Create primitive type.",
)
@click.option("--parent", default=None, help="Parent path for the new object.")
@click.option(
    "--position", "-pos", nargs=3, type=float, default=None, help="Position as X Y Z."
)
@click.option(
    "--rotation",
    "-rot",
    nargs=3,
    type=float,
    default=None,
    help="Euler rotation as X Y Z.",
)
@click.option(
    "--scale", "-s", nargs=3, type=float, default=None, help="Scale as X Y Z."
)
@click.option("--tag", "-t", default=None, help="Tag to assign.")
@click.option("--layer", default=None, help="Layer to assign (number or name).")
@click.option(
    "--components", default=None, help="Comma-separated list of components to add."
)
@click.option("--inactive", is_flag=True, help="Create as inactive.")
@handle_unity_errors
def create_object(
    name: str,
    primitive: Optional[str],
    parent: Optional[str],
    position: Optional[Tuple[float, float, float]],
    rotation: Optional[Tuple[float, float, float]],
    scale: Optional[Tuple[float, float, float]],
    tag: Optional[str],
    layer: Optional[str],
    components: Optional[str],
    inactive: bool,
):
    """Create a new GameObject.

    \b
    Examples:
        unity-mcp scene-obj create Player
        unity-mcp scene-obj create Cube --primitive Cube
        unity-mcp scene-obj create Enemy --parent /Enemies --tag Enemy
        unity-mcp scene-obj create Item --components Rigidbody,BoxCollider
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "name": name,
    }

    if primitive:
        params["primitive"] = primitive
    if parent:
        params["parent"] = parent
    if position:
        params["position"] = list(position)
    if rotation:
        params["rotation"] = list(rotation)
    if scale:
        params["scale"] = list(scale)
    if tag:
        params["tag"] = tag
    if layer:
        params["layer"] = layer
    if components:
        params["components"] = [c.strip() for c in components.split(",")]
    if inactive:
        params["active"] = False

    result = run_command("scene_object", params, config)
    click.echo(format_output(result, config.format))

    if result.get("success"):
        print_success(f"Created object '{name}'")


@scene_obj.command("delete")
@click.argument("target", required=False)
@click.option(
    "--regex",
    "-r",
    default=None,
    help="Delete all objects matching path regex (batch).",
)
@click.option("--tag", "-t", default=None, help="Delete all objects with tag (batch).")
@click.option(
    "--parent", "-p", default=None, help="Delete all children of parent (batch)."
)
@click.option("--force", "-f", is_flag=True, help="Skip confirmation prompt.")
@handle_unity_errors
def delete_object(
    target: Optional[str],
    regex: Optional[str],
    tag: Optional[str],
    parent: Optional[str],
    force: bool,
):
    """Delete GameObject(s).

    TARGET is required for single object. Use --regex, --tag, or --parent for batch.

    \b
    Examples:
        unity-mcp scene-obj delete TempObject
        unity-mcp scene-obj delete /Temp/Object
        unity-mcp scene-obj delete --regex ".*Temp.*" --force
        unity-mcp scene-obj delete --tag Temp --force
    """
    config = get_config()

    is_batch = any([regex, tag, parent])

    if not is_batch and not target:
        print_error("TARGET is required for single object mode.")
        return

    if not force:
        if is_batch:
            criteria = []
            if regex:
                criteria.append(f"regex '{regex}'")
            if tag:
                criteria.append(f"tag '{tag}'")
            if parent:
                criteria.append(f"children of '{parent}'")
            confirm_destructive_action(
                "Delete", f"objects matching {' OR '.join(criteria)}", "", False
            )
        else:
            confirm_destructive_action("Delete", "GameObject", target or "", False)

    params: dict[str, Any] = {"action": "delete"}

    if target:
        params["target"] = target
    if regex:
        params["target_regex"] = regex
    if tag:
        params["tag"] = tag
    if parent:
        params["parent"] = parent

    result = run_command("scene_object", params, config)
    click.echo(format_output(result, config.format))

    if result.get("success"):
        data = result.get("data", {})
        count = data.get("count", 1)
        if is_batch:
            print_success(f"Deleted {count} objects")
        else:
            print_success(f"Deleted object '{target}'")
