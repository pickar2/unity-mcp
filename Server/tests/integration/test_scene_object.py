"""
Integration tests for scene_object tool.

Validates Python parameter handling and serialization.
"""

import pytest

from .test_helpers import DummyContext, DummyMCP


def setup_scene_object_tools():
    """Setup scene_object tool for testing."""
    mcp = DummyMCP()
    import services.tools.scene_object
    from services.registry import get_registered_tools

    for tool_info in get_registered_tools():
        tool_name = tool_info["name"]
        if "scene_object" in tool_name:
            mcp.tools[tool_name] = tool_info["func"]
    return mcp.tools


@pytest.mark.asyncio
async def test_scene_object_list_basic(monkeypatch):
    """Test basic list action."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "objects": [
                    {
                        "path": "/Main Camera",
                        "name": "Main Camera",
                        "instance_id": 12345,
                        "active": True,
                    },
                    {
                        "path": "/Player",
                        "name": "Player",
                        "instance_id": 12346,
                        "active": True,
                    },
                ],
                "total": 2,
                "cursor": 0,
                "page_size": 50,
                "next_cursor": None,
                "has_more": False,
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(ctx=DummyContext(), action="list")
    assert resp["success"] is True
    assert captured["params"]["action"] == "list"
    assert len(resp["data"]["objects"]) == 2


@pytest.mark.asyncio
async def test_scene_object_list_with_filters(monkeypatch):
    """Test list action with filters."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "data": {"objects": [], "total": 0}}

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="list",
        tag="Enemy",
        component="Rigidbody",
        depth=2,
        include_inactive=False,
    )
    assert resp["success"] is True
    assert captured["params"]["tag"] == "Enemy"
    assert captured["params"]["component"] == "Rigidbody"
    assert captured["params"]["depth"] == 2
    assert captured["params"]["include_inactive"] is False


@pytest.mark.asyncio
async def test_scene_object_get(monkeypatch):
    """Test get action."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "path": "/Player",
                "name": "Player",
                "instance_id": 12346,
                "active": True,
                "transform": {"position": {"x": 0, "y": 1, "z": 0}},
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(ctx=DummyContext(), action="get", target="/Player")
    assert resp["success"] is True
    assert captured["params"]["target"] == "/Player"


@pytest.mark.asyncio
async def test_scene_object_get_with_components(monkeypatch):
    """Test get action with components flag."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "data": {"path": "/Player", "components": []}}

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(), action="get", target="Player", components=True
    )
    assert resp["success"] is True
    assert captured["params"]["components"] is True


@pytest.mark.asyncio
async def test_scene_object_set_single(monkeypatch):
    """Test set action on single object."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Updated object 'Player'",
            "data": {"path": "/Player", "instance_id": 12346, "changes": ["active"]},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        active=False,
        position=[10, 0, 5],
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "Player"
    assert captured["params"]["active"] is False
    assert captured["params"]["position"] == [10, 0, 5]


@pytest.mark.asyncio
async def test_scene_object_set_batch_regex(monkeypatch):
    """Test batch set with target_regex."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Updated 3 objects",
            "data": {"affected": [], "count": 3},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target_regex=".*Enemy",
        active=False,
    )
    assert resp["success"] is True
    assert captured["params"]["target_regex"] == ".*Enemy"
    assert captured["params"]["active"] is False


@pytest.mark.asyncio
async def test_scene_object_create(monkeypatch):
    """Test create action."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Created object 'Enemy'",
            "data": {"path": "/Enemy", "name": "Enemy", "instance_id": 12350},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="create",
        name="Enemy",
        primitive="Cube",
        position=[5, 0, 3],
        components=["Rigidbody", "BoxCollider"],
    )
    assert resp["success"] is True
    assert captured["params"]["name"] == "Enemy"
    assert captured["params"]["primitive"] == "Cube"
    assert captured["params"]["position"] == [5, 0, 3]
    assert captured["params"]["components"] == ["Rigidbody", "BoxCollider"]


@pytest.mark.asyncio
async def test_scene_object_delete_single(monkeypatch):
    """Test delete action on single object."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Deleted object '/Temp'",
            "data": {"deleted": [{"path": "/Temp", "instance_id": 12351}], "count": 1},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(ctx=DummyContext(), action="delete", target="/Temp")
    assert resp["success"] is True
    assert captured["params"]["target"] == "/Temp"


@pytest.mark.asyncio
async def test_scene_object_delete_batch_tag(monkeypatch):
    """Test batch delete with tag."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Deleted 5 objects",
            "data": {"deleted": [], "count": 5},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(ctx=DummyContext(), action="delete", tag="Temp")
    assert resp["success"] is True
    assert captured["params"]["tag"] == "Temp"


@pytest.mark.asyncio
async def test_scene_object_pagination(monkeypatch):
    """Test pagination parameters."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"objects": [], "total": 100, "has_more": True},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(), action="list", page_size=25, cursor=50
    )
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 25
    assert captured["params"]["cursor"] == 50


@pytest.mark.asyncio
async def test_scene_object_ambiguity_error(monkeypatch):
    """Test ambiguity error when multiple objects match name."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        return {
            "success": False,
            "error": "Multiple objects match name 'Enemy'",
            "data": {
                "matches": [
                    {"path": "/Enemies/Enemy", "instance_id": 12345},
                    {"path": "/Player/Enemy", "instance_id": 12346},
                ],
                "hint": "Use full path or instance_id to specify which object",
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(ctx=DummyContext(), action="get", target="Enemy")
    assert resp["success"] is False
    assert "Multiple objects" in resp["error"]
    assert len(resp["data"]["matches"]) == 2


@pytest.mark.asyncio
async def test_scene_object_set_component_properties(monkeypatch):
    """Test setting component properties."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Updated object 'Player'",
            "data": {"path": "/Player", "changes": ["component:Rigidbody"]},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        component="Rigidbody",
        properties={"mass": 10, "useGravity": False},
    )
    assert resp["success"] is True
    assert captured["params"]["component"] == "Rigidbody"
    assert captured["params"]["properties"] == {"mass": 10, "useGravity": False}
