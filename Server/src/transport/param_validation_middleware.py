"""
Middleware that intercepts Pydantic ValidationErrors on tool calls and
reformats them into friendly, actionable error messages with "did you mean?"
suggestions for mistyped parameter names.
"""

import difflib
import inspect
import logging

from fastmcp.exceptions import ToolError
from fastmcp.server.middleware import Middleware, MiddlewareContext
from pydantic import ValidationError

logger = logging.getLogger("mcp-for-unity-server")


class ParamValidationMiddleware(Middleware):
    """Catches Pydantic validation errors and provides friendly suggestions."""

    def __init__(self, tool_params: dict[str, list[str]]):
        super().__init__()
        self._tool_params = tool_params

    async def on_call_tool(self, context, call_next):
        try:
            return await call_next(context)
        except ValidationError as e:
            tool_name = context.message.name
            valid_params = self._tool_params.get(tool_name, [])
            raise ToolError(self._format_errors(e, valid_params)) from e

    @staticmethod
    def _format_errors(exc: ValidationError, valid_params: list[str]) -> str:
        parts = []
        for err in exc.errors():
            param = err["loc"][-1] if err.get("loc") else "?"

            if err["type"] == "unexpected_keyword_argument":
                matches = difflib.get_close_matches(
                    param, valid_params, n=1, cutoff=0.4
                )
                if matches:
                    parts.append(
                        f"Unknown parameter '{param}'. Did you mean '{matches[0]}'?"
                    )
                else:
                    parts.append(
                        f"Unknown parameter '{param}'. "
                        f"Valid parameters: {', '.join(valid_params)}"
                    )

            elif err["type"] == "missing_argument":
                parts.append(f"Missing required parameter '{param}'.")

            else:
                parts.append(f"Parameter '{param}': {err['msg']}")

        return "\n".join(parts) if parts else str(exc)


def build_tool_params_map() -> dict[str, list[str]]:
    """Build {tool_name: [param_names]} from the registered tool functions.

    Must be called AFTER tool modules are imported but BEFORE decorator
    wrapping (which replaces the original signature).
    """
    from services.registry import get_registered_tools

    result: dict[str, list[str]] = {}
    for tool_info in get_registered_tools():
        sig = inspect.signature(tool_info["func"])
        params = [p for p in sig.parameters if p != "ctx"]
        result[tool_info["name"]] = params
    return result
