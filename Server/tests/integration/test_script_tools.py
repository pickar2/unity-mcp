import pytest
import asyncio

from .test_helpers import DummyContext, DummyMCP


def setup_asset_tools():
    """Setup asset-related tools for testing."""
    mcp = DummyMCP()
    import services.tools.manage_asset
    from services.registry import get_registered_tools

    for tool_info in get_registered_tools():
        tool_name = tool_info["name"]
        if any(keyword in tool_name for keyword in ["asset", "manage_asset"]):
            mcp.tools[tool_name] = tool_info["func"]
    return mcp.tools


@pytest.mark.asyncio
async def test_manage_asset_prefab_modify_request(monkeypatch):
    tools = setup_asset_tools()
    manage_asset = tools["manage_asset"]
    captured = {}

    async def fake_async(cmd, params, loop=None):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True}

    # Patch the async function in the tools module
    import services.tools.manage_asset as tools_manage_asset

    # Patch both at the module and at the function closure location
    monkeypatch.setattr(tools_manage_asset, "async_send_command_with_retry", fake_async)
    # Also patch the globals of the function object (handles dynamically loaded module alias)
    manage_asset.__globals__["async_send_command_with_retry"] = fake_async

    resp = await manage_asset(
        DummyContext(),
        action="modify",
        path="Assets/Prefabs/Player.prefab",
        properties={"hp": 100},
    )
    assert captured["cmd"] == "manage_asset"
    assert captured["params"]["action"] == "modify"
    assert captured["params"]["path"] == "Assets/Prefabs/Player.prefab"
    assert captured["params"]["properties"] == {"hp": 100}
    assert resp["success"] is True
