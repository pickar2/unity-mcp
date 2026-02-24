import pytest

from .test_helpers import DummyContext
import services.tools.manage_scene as manage_scene_mod


@pytest.mark.asyncio
async def test_manage_scene_get_hierarchy_paging_params_pass_through(monkeypatch):
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_scene_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="get_hierarchy",
        parent="Player",
        page_size="10",
        cursor="20",
        max_nodes="1000",
        max_depth="6",
        max_children_per_node="200",
        include_transform="true",
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "get_hierarchy"
    assert p["parent"] == "Player"
    assert p["pageSize"] in (10, "10")
    assert p["cursor"] in (20, "20")
    assert p["maxNodes"] in (1000, "1000")
    assert p["maxDepth"] in (6, "6")
    assert p["maxChildrenPerNode"] in (200, "200")
    assert p["includeTransform"] in (True, "true")


@pytest.mark.asyncio
async def test_manage_scene_screenshot_synchronous_param(monkeypatch):
    """Test that synchronous param is forwarded for screenshots."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"path": "Assets/Screenshots/test.png", "isAsync": False},
        }

    monkeypatch.setattr(
        manage_scene_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
        screenshot_file_name="test",
        synchronous=True,
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "screenshot"
    assert p["fileName"] == "test"
    assert p["synchronous"] is True


@pytest.mark.asyncio
async def test_manage_scene_screenshot_without_synchronous(monkeypatch):
    """Test that synchronous param is omitted when not specified."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"path": "Assets/Screenshots/test.png", "isAsync": True},
        }

    monkeypatch.setattr(
        manage_scene_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
    )

    assert resp.get("success") is True
    assert "synchronous" not in captured["params"]
