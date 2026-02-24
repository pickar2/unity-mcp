"""
Integration tests for invoke_method tool.

Validates Python parameter handling and serialization.
"""

import pytest

from .test_helpers import DummyContext, DummyMCP


def setup_invoke_method_tool():
    mcp = DummyMCP()
    import services.tools.invoke_method
    from services.registry import get_registered_tools

    for tool_info in get_registered_tools():
        if tool_info["name"] == "invoke_method":
            mcp.tools["invoke_method"] = tool_info["func"]
    return mcp.tools


@pytest.mark.asyncio
async def test_instance_method_basic(monkeypatch):
    """Instance method call sends correct params."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Method 'TakeDamage' invoked successfully (void).",
        }

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    resp = await invoke(
        ctx=DummyContext(),
        method="TakeDamage",
        target="Player",
        component="PlayerHealth",
        args=[50],
    )
    assert resp["success"] is True
    p = captured["params"]
    assert p["method"] == "TakeDamage"
    assert p["target"] == "Player"
    assert p["component"] == "PlayerHealth"
    assert p["args"] == [50]


@pytest.mark.asyncio
async def test_instance_method_by_path(monkeypatch):
    """Instance method with path target."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "OK"}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    resp = await invoke(
        ctx=DummyContext(),
        method="SetState",
        target="/Enemies/Goblin",
        component="EnemyAI",
        args=["idle"],
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "/Enemies/Goblin"
    assert captured["params"]["args"] == ["idle"]


@pytest.mark.asyncio
async def test_instance_method_by_id(monkeypatch):
    """Instance method with integer instance_id target."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "OK"}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    resp = await invoke(
        ctx=DummyContext(),
        method="Reset",
        target=12345,
        component="Transform",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == 12345
    assert "args" not in captured["params"]


@pytest.mark.asyncio
async def test_static_method(monkeypatch):
    """Static method call sends type instead of target+component."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"returnValue": 42, "returnType": "System.Int32"},
        }

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    resp = await invoke(
        ctx=DummyContext(),
        method="ResetLevel",
        type="GameManager",
    )
    assert resp["success"] is True
    p = captured["params"]
    assert p["type"] == "GameManager"
    assert p["method"] == "ResetLevel"
    assert "target" not in p
    assert "component" not in p


@pytest.mark.asyncio
async def test_no_args_omitted(monkeypatch):
    """When args is None, it's not included in params."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "OK"}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    await invoke(ctx=DummyContext(), method="DoStuff", type="MyType")
    assert "args" not in captured["params"]


@pytest.mark.asyncio
async def test_args_as_json_string(monkeypatch):
    """Args passed as JSON string are parsed into list."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "OK"}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    await invoke(
        ctx=DummyContext(),
        method="AddItem",
        target="Player",
        component="Inventory",
        args='["sword", 1]',
    )
    assert captured["params"]["args"] == ["sword", 1]


@pytest.mark.asyncio
async def test_args_multiple_types(monkeypatch):
    """Args with mixed types (string, int, float, bool, list)."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "OK"}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    await invoke(
        ctx=DummyContext(),
        method="Configure",
        target="Obj",
        component="MyComp",
        args=["label", 42, 3.14, True, [1, 2, 3]],
    )
    assert captured["params"]["args"] == ["label", 42, 3.14, True, [1, 2, 3]]


@pytest.mark.asyncio
async def test_non_dict_response_wrapped(monkeypatch):
    """Non-dict response from Unity is wrapped in error dict."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        return "unexpected string response"

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    resp = await invoke(ctx=DummyContext(), method="Foo", type="Bar")
    assert resp["success"] is False
    assert "unexpected string response" in resp["message"]


@pytest.mark.asyncio
async def test_command_type_is_invoke_method(monkeypatch):
    """Verify the command type sent to Unity is 'invoke_method'."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, command_type, params, **_kwargs):
        captured["command_type"] = command_type
        return {"success": True}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    await invoke(ctx=DummyContext(), method="Test", type="SomeType")
    assert captured["command_type"] == "invoke_method"


@pytest.mark.asyncio
async def test_optional_params_omitted(monkeypatch):
    """Optional params that are None are not sent to Unity."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True}

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    await invoke(ctx=DummyContext(), method="Run", type="Runner")
    p = captured["params"]
    assert set(p.keys()) == {"method", "type"}


@pytest.mark.asyncio
async def test_error_response_from_unity(monkeypatch):
    """Unity error response is passed through."""
    tools = setup_invoke_method_tool()
    invoke = tools["invoke_method"]

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        return {
            "success": False,
            "message": "Method 'Foo' not found on Bar.",
            "data": {"availableMethods": ["void Baz()", "int Qux(String s)"]},
        }

    import services.tools.invoke_method as mod

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    resp = await invoke(ctx=DummyContext(), method="Foo", type="Bar")
    assert resp["success"] is False
    assert "not found" in resp["message"]
