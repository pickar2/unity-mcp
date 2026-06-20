from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Discover component types and inspect their property schemas, including enum values and defaults.

Actions:
- list: Browse available component types. Filter by search term or category. Returns paginated results.
- schema: Get full property schema for a component type — field names, types, enum value mappings, defaults, ranges.

Use 'list' to discover what components exist (e.g. "what Light types are available?").
Use 'schema' to understand a component's properties before setting them (e.g. "what are the enum values for Light2D.m_LightType?").

Examples:
  inspect_component(action="list", search="Light")
  inspect_component(action="list", category="Physics 2D")
  inspect_component(action="schema", type_name="Light2D")
  inspect_component(action="schema", type_name="Rigidbody", include_enum_values=True)
  inspect_component(action="schema", type_name="PlayerHealth", include_methods=True)""",
    annotations=ToolAnnotations(
        title="Inspect Component",
        readOnlyHint=True,
    ),
)
async def inspect_component(
    ctx: Context,
    action: Annotated[
        Literal["list", "schema"],
        "Action to perform: 'list' to browse types, 'schema' to get property details for a specific type.",
    ],
    # For 'list' action
    search: Annotated[
        str,
        "Search term to filter component types by name (e.g. 'Light', 'Collider2D').",
    ]
    | None = None,
    category: Annotated[
        str,
        "Filter by category derived from namespace (e.g. 'Physics', 'Physics 2D', 'Rendering', 'UI', 'Audio', 'Scripts').",
    ]
    | None = None,
    page: Annotated[
        int | str,
        "Page number for paginated results (0-indexed). Default 0.",
    ]
    | None = None,
    page_size: Annotated[
        int | str,
        "Number of results per page. Default 50.",
    ]
    | None = None,
    # For 'schema' action
    type_name: Annotated[
        str,
        "Component type name for schema action. Short name ('Rigidbody') or full name ('UnityEngine.Rigidbody').",
    ]
    | None = None,
    include_enum_values: Annotated[
        bool | str,
        "Include enum value mappings in schema (default true).",
    ]
    | None = None,
    include_defaults: Annotated[
        bool | str,
        "Include default values in schema (default true).",
    ]
    | None = None,
    include_methods: Annotated[
        bool | str,
        "Include public method signatures in schema (default false). Useful for invoke_method discovery.",
    ]
    | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {"action": action}

    if search:
        params["search"] = search
    if category:
        params["category"] = category

    coerced_page = coerce_int(page, default=None)
    if coerced_page is not None:
        params["page"] = coerced_page

    coerced_page_size = coerce_int(page_size, default=None)
    if coerced_page_size is not None:
        params["pageSize"] = coerced_page_size

    if type_name:
        params["typeName"] = type_name

    include_enums = coerce_bool(include_enum_values, default=None)
    if include_enums is not None:
        params["includeEnumValues"] = include_enums

    include_defs = coerce_bool(include_defaults, default=None)
    if include_defs is not None:
        params["includeDefaults"] = include_defs

    include_meths = coerce_bool(include_methods, default=None)
    if include_meths is not None:
        params["includeMethods"] = include_meths

    return await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "inspect_component",
        params,
    )
