"""Tests for list_menu_items tool."""

import inspect

import pytest

from services.tools.list_menu_items import list_menu_items


class TestListMenuItemsSignature:
    """Tests for list_menu_items parameter signatures."""

    def test_path_prefix_parameter_exists(self):
        sig = inspect.signature(list_menu_items)
        assert "path_prefix" in sig.parameters

    def test_search_parameter_exists(self):
        sig = inspect.signature(list_menu_items)
        assert "search" in sig.parameters

    def test_path_prefix_is_optional(self):
        sig = inspect.signature(list_menu_items)
        assert sig.parameters["path_prefix"].default is None

    def test_search_is_optional(self):
        sig = inspect.signature(list_menu_items)
        assert sig.parameters["search"].default is None

    def test_tool_has_read_only_annotation(self):
        from services.registry import get_registered_tools

        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "list_menu_items"), None)
        assert tool is not None
        annotations = tool.get("kwargs", {}).get("annotations")
        assert annotations is not None
        assert annotations.readOnlyHint is True


class TestListMenuItemsForwarding:
    """Tests that parameters are correctly forwarded to Unity."""

    @pytest.mark.asyncio
    async def test_path_prefix_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"items": [], "total": 0}}

        import services.tools.list_menu_items as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await list_menu_items(ctx=_DummyContext(), path_prefix="GameObject/Light")
        assert captured["params"]["pathPrefix"] == "GameObject/Light"

    @pytest.mark.asyncio
    async def test_search_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"items": [], "total": 0}}

        import services.tools.list_menu_items as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await list_menu_items(ctx=_DummyContext(), search="Point Light")
        assert captured["params"]["search"] == "Point Light"

    @pytest.mark.asyncio
    async def test_no_params_sends_empty(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"items": [], "total": 0}}

        import services.tools.list_menu_items as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await list_menu_items(ctx=_DummyContext())
        assert "pathPrefix" not in captured["params"]
        assert "search" not in captured["params"]

    @pytest.mark.asyncio
    async def test_both_params_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"items": [], "total": 0}}

        import services.tools.list_menu_items as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await list_menu_items(
            ctx=_DummyContext(), path_prefix="GameObject", search="Light"
        )
        assert captured["params"]["pathPrefix"] == "GameObject"
        assert captured["params"]["search"] == "Light"
