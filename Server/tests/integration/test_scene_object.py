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


@pytest.mark.asyncio
async def test_scene_object_duplicate_basic(monkeypatch):
    """Test duplicate action with default settings."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Duplicated 'Player' as 'Player_Copy'.",
            "data": {
                "source": {"path": "/Player", "instance_id": 100},
                "duplicate": {
                    "path": "/Player_Copy",
                    "name": "Player_Copy",
                    "instance_id": 200,
                },
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(ctx=DummyContext(), action="duplicate", target="Player")
    assert resp["success"] is True
    assert captured["params"]["action"] == "duplicate"
    assert captured["params"]["target"] == "Player"


@pytest.mark.asyncio
async def test_scene_object_duplicate_with_name_and_offset(monkeypatch):
    """Test duplicate action with custom name and offset."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "source": {"path": "/Player", "instance_id": 100},
                "duplicate": {
                    "path": "/Player2",
                    "name": "Player2",
                    "instance_id": 201,
                },
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="duplicate",
        target="/Player",
        name="Player2",
        offset=[5, 0, 0],
        parent="/Team",
    )
    assert resp["success"] is True
    assert captured["params"]["name"] == "Player2"
    assert captured["params"]["offset"] == [5, 0, 0]
    assert captured["params"]["parent"] == "/Team"


@pytest.mark.asyncio
async def test_scene_object_duplicate_with_position(monkeypatch):
    """Test duplicate action with absolute position."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "source": {"path": "/Player", "instance_id": 100},
                "duplicate": {
                    "path": "/Player_Copy",
                    "name": "Player_Copy",
                    "instance_id": 202,
                },
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="duplicate",
        target="Player",
        position=[10, 0, 5],
    )
    assert resp["success"] is True
    assert captured["params"]["position"] == [10, 0, 5]


@pytest.mark.asyncio
async def test_scene_object_move_relative_direction(monkeypatch):
    """Test move_relative action with direction."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Moved 'Chair' relative to 'Table'.",
            "data": {
                "path": "/Chair",
                "instance_id": 300,
                "new_position": {"x": 2, "y": 0, "z": 0},
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="move_relative",
        target="Chair",
        reference="Table",
        direction="right",
        distance=2.0,
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "move_relative"
    assert captured["params"]["target"] == "Chair"
    assert captured["params"]["reference"] == "Table"
    assert captured["params"]["direction"] == "right"
    assert captured["params"]["distance"] == 2.0


@pytest.mark.asyncio
async def test_scene_object_move_relative_offset(monkeypatch):
    """Test move_relative action with custom offset."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "path": "/Lamp",
                "instance_id": 301,
                "new_position": {"x": 1, "y": 0.5, "z": 0},
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="move_relative",
        target="Lamp",
        reference="Desk",
        offset=[1, 0.5, 0],
    )
    assert resp["success"] is True
    assert captured["params"]["offset"] == [1, 0.5, 0]
    assert captured["params"]["reference"] == "Desk"


@pytest.mark.asyncio
async def test_scene_object_move_relative_local_space(monkeypatch):
    """Test move_relative action with local space flag."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "data": {"path": "/NPC", "instance_id": 302}}

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="move_relative",
        target="NPC",
        reference="Player",
        direction="forward",
        distance=5.0,
        world_space=False,
    )
    assert resp["success"] is True
    assert captured["params"]["world_space"] is False
    assert captured["params"]["direction"] == "forward"
    assert captured["params"]["distance"] == 5.0


@pytest.mark.asyncio
async def test_scene_object_set_add_components(monkeypatch):
    """Test set action with add_components."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "path": "/Player",
                "changes": ["add_component:Rigidbody", "add_component:BoxCollider"],
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        add_components=["Rigidbody", "BoxCollider"],
    )
    assert resp["success"] is True
    assert captured["params"]["add_components"] == ["Rigidbody", "BoxCollider"]


@pytest.mark.asyncio
async def test_scene_object_set_remove_components(monkeypatch):
    """Test set action with remove_components."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"path": "/Player", "changes": ["remove_component:BoxCollider"]},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        remove_components=["BoxCollider"],
    )
    assert resp["success"] is True
    assert captured["params"]["remove_components"] == ["BoxCollider"]


@pytest.mark.asyncio
async def test_scene_object_set_component_properties_dict(monkeypatch):
    """Test set action with component_properties dict for multiple components."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "path": "/Player",
                "changes": ["component:Rigidbody", "component:Collider"],
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    comp_props = {
        "Rigidbody": {"mass": 10, "useGravity": True},
        "Collider": {"isTrigger": True},
    }

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        component_properties=comp_props,
    )
    assert resp["success"] is True
    assert captured["params"]["component_properties"] == comp_props


@pytest.mark.asyncio
async def test_scene_object_list_layer_filter(monkeypatch):
    """Test list action with layer filter."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "objects": [{"path": "/Water", "name": "Water", "instance_id": 400}],
                "total": 1,
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="list",
        layer="Water",
    )
    assert resp["success"] is True
    assert captured["params"]["layer"] == "Water"


@pytest.mark.asyncio
async def test_scene_object_list_layer_filter_numeric(monkeypatch):
    """Test list action with numeric layer filter."""
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
        layer=4,
    )
    assert resp["success"] is True
    assert captured["params"]["layer"] == 4


@pytest.mark.asyncio
async def test_scene_object_set_tag_passthrough(monkeypatch):
    """Test that tag is passed through for auto-creation on C# side."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"path": "/Player", "changes": ["tag"]},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        tag="CustomNewTag",
    )
    assert resp["success"] is True
    assert captured["params"]["tag"] == "CustomNewTag"


@pytest.mark.asyncio
async def test_scene_object_circular_parent_error(monkeypatch):
    """Test that C# returns error for circular parenting."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        return {
            "success": False,
            "error": "Cannot parent 'Parent' to 'Child', as it would create a hierarchy loop.",
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Parent",
        parent="Child",
    )
    assert resp["success"] is False
    assert "hierarchy loop" in resp["error"]


@pytest.mark.asyncio
async def test_scene_object_create_with_tag_and_layer(monkeypatch):
    """Test create action passes tag and layer for auto-creation."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"path": "/Enemy", "name": "Enemy", "instance_id": 500},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="create",
        name="Enemy",
        tag="EnemyTag",
        layer="Water",
    )
    assert resp["success"] is True
    assert captured["params"]["tag"] == "EnemyTag"
    assert captured["params"]["layer"] == "Water"


@pytest.mark.asyncio
async def test_scene_object_set_reparent(monkeypatch):
    """Test set action with reparent."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"path": "/NewParent/Player", "changes": ["parent"]},
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="set",
        target="Player",
        parent="/NewParent",
    )
    assert resp["success"] is True
    assert captured["params"]["parent"] == "/NewParent"


@pytest.mark.asyncio
async def test_scene_object_move_relative_missing_direction_and_offset(monkeypatch):
    """Test that move_relative passes params even without direction/offset (C# will validate)."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": False,
            "error": "Either 'direction' or 'offset' parameter is required for 'move_relative' action.",
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="move_relative",
        target="Chair",
        reference="Table",
    )
    assert resp["success"] is False
    assert "direction" in resp["error"] or "offset" in resp["error"]


@pytest.mark.asyncio
async def test_scene_object_get_component_filter(monkeypatch):
    """Test get action with component filter implies components=true."""
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
                "components": [
                    {"typeName": "SpriteRenderer", "properties": {"color": {"r": 1}}}
                ],
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="get",
        target="/Player",
        component="SpriteRenderer",
    )
    assert resp["success"] is True
    # component filter should cause components=true to be sent
    assert captured["params"]["components"] is True
    assert captured["params"]["component"] == "SpriteRenderer"


@pytest.mark.asyncio
async def test_scene_object_get_component_and_properties(monkeypatch):
    """Test get action with both component and properties filter."""
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
                "components": [
                    {"typeName": "SpriteRenderer", "properties": {"color": {"r": 1}}}
                ],
            },
        }

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="get",
        target="/Player",
        component="SpriteRenderer",
        properties={"sprite": None, "color": None},
    )
    assert resp["success"] is True
    assert captured["params"]["component"] == "SpriteRenderer"
    assert captured["params"]["properties"] is not None


@pytest.mark.asyncio
async def test_scene_object_get_components_true_still_works(monkeypatch):
    """Test that components=true still works without component filter."""
    tools = setup_scene_object_tools()
    scene_object = tools["scene_object"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "data": {"path": "/Player", "components": []}}

    import services.tools.scene_object as scene_object_mod

    monkeypatch.setattr(scene_object_mod, "send_with_unity_instance", fake_send)

    resp = await scene_object(
        ctx=DummyContext(),
        action="get",
        target="/Player",
        components=True,
    )
    assert resp["success"] is True
    assert captured["params"]["components"] is True
    assert "component" not in captured["params"]
