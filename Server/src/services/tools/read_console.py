"""
Defines the read_console tool for accessing Unity Editor console messages.

The C# side uses LogCaptureService which captures logs via Application.logMessageReceived.
Each entry has a sequenceId and timestamp, enabling efficient polling via since_sequence_id.
"""

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool, parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Read or clear Unity console messages. BLOCKING - returns immediately with results.

NOTE: To check for compilation errors before playing, use manage_editor(action='play', recompile=true) instead.
That handles compile + error check + play in one call. Only use read_console for debugging/diagnostics.

Options:
- action: 'get' (default) or 'clear'
- types: ['error', 'warning', 'log', 'all'] - message types to include
- count: Max messages (default 100). Use page_size/cursor for pagination.
- since_sequence_id: Only return entries after this sequence ID (for efficient polling)
- since_timestamp: Only return entries after this ISO 8601 timestamp
- filter_regex: Regex filter (case-insensitive). Example: 'DIAGNOSTIC|CONTACTS.*particle 1842'
- page_size/cursor: Pagination support for large result sets
- count_only: Return only counts by type (no entries)
- include_stacktrace: Include stack traces in output

Entries include sequenceId and timestamp fields. Use since_sequence_id to poll for
new entries since your last read - pass the latestSequenceId from a previous response.""",
    annotations=ToolAnnotations(
        title="Read Console",
    ),
)
async def read_console(
    ctx: Context,
    action: Annotated[
        Literal["get", "clear"],
        "Get or clear the Unity Editor console. Defaults to 'get' if omitted.",
    ]
    | None = None,
    types: Annotated[
        list[Literal["error", "warning", "log", "all"]] | str,
        "Message types to get (accepts list or JSON string)",
    ]
    | None = None,
    count: Annotated[
        int | str,
        "Max messages to return in non-paging mode (accepts int or string, e.g., 5 or '5'). Ignored when paging with page_size/cursor.",
    ]
    | None = None,
    since_sequence_id: Annotated[
        int | str,
        "Only return entries with sequenceId greater than this value. Use latestSequenceId from a previous response for efficient polling.",
    ]
    | None = None,
    after_sequence_id: Annotated[int | str, "Alias for since_sequence_id."]
    | None = None,
    since_timestamp: Annotated[str, "Get messages after this timestamp (ISO 8601)"]
    | None = None,
    filter_regex: Annotated[
        str,
        "Regex pattern filter for messages (case-insensitive). Example: 'DIAGNOSTIC|CONTACTS.*particle 1842'",
    ]
    | None = None,
    page_size: Annotated[
        int | str, "Page size for paginated console reads. Defaults to 50 when omitted."
    ]
    | None = None,
    cursor: Annotated[
        int | str, "Opaque cursor for paging (0-based offset). Defaults to 0."
    ]
    | None = None,
    count_only: Annotated[
        bool | str, "If true, return only entry counts by type instead of full entries."
    ]
    | None = None,
    include_stacktrace: Annotated[
        bool | str,
        "Include stack traces in output (accepts true/false or 'true'/'false')",
    ]
    | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    # Set defaults
    action = action if action is not None else "get"

    # Parse types if it's a JSON string (handles client compatibility issue #561)
    if isinstance(types, str):
        types = parse_json_payload(types)
    # Validate types is a list after parsing
    if types is not None and not isinstance(types, list):
        return {
            "success": False,
            "message": (
                f"types must be a list, got {type(types).__name__}. "
                'If passing as JSON string, use format: \'["error", "warning"]\''
            ),
        }
    if types is not None:
        allowed_types = {"error", "warning", "log", "all"}
        normalized_types = []
        for entry in types:
            if not isinstance(entry, str):
                return {
                    "success": False,
                    "message": f"types entries must be strings, got {type(entry).__name__}",
                }
            normalized = entry.strip().lower()
            if normalized not in allowed_types:
                return {
                    "success": False,
                    "message": (
                        f"invalid types entry '{entry}'. "
                        f"Allowed values: {sorted(allowed_types)}"
                    ),
                }
            normalized_types.append(normalized)
        types = normalized_types
    else:
        types = ["error", "warning", "log"]

    # Coerce booleans defensively (strings like 'true'/'false')
    include_stacktrace = coerce_bool(include_stacktrace, default=False)
    count_only = coerce_bool(count_only, default=False)

    coerced_page_size = coerce_int(page_size, default=None)
    coerced_cursor = coerce_int(cursor, default=None)
    coerced_since_seq = coerce_int(since_sequence_id or after_sequence_id, default=None)

    # Normalize action if it's a string
    if isinstance(action, str):
        action = action.lower()

    # Coerce count defensively (string/float -> int).
    if isinstance(count, str) and count.strip().lower() in ("all", "*"):
        count = None
    else:
        count = coerce_int(count)

    # Default count to 100 when action is "get" and no count or page_size is specified
    if action == "get" and count is None and coerced_page_size is None:
        count = 100

    # Prepare parameters for the C# handler
    params_dict = {
        "action": action,
        "types": types,
        "count": count,
        "sinceSequenceId": coerced_since_seq,
        "sinceTimestamp": since_timestamp,
        "filterRegex": filter_regex,
        "pageSize": coerced_page_size,
        "cursor": coerced_cursor,
        "countOnly": count_only,
        "includeStacktrace": include_stacktrace,
    }
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Use centralized retry helper with instance routing
    resp = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "read_console", params_dict
    )
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
