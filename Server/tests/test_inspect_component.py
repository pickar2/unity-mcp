"""Tests for inspect_component tool."""

import inspect

import pytest

from services.tools.inspect_component import inspect_component


class TestInspectComponentSignature:
    """Tests for inspect_component parameter signatures."""

    def test_action_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "action" in sig.parameters

    def test_search_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "search" in sig.parameters

    def test_category_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "category" in sig.parameters

    def test_type_name_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "type_name" in sig.parameters

    def test_include_enum_values_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "include_enum_values" in sig.parameters

    def test_include_defaults_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "include_defaults" in sig.parameters

    def test_page_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "page" in sig.parameters

    def test_page_size_parameter_exists(self):
        sig = inspect.signature(inspect_component)
        assert "page_size" in sig.parameters

    def test_optional_params_default_to_none(self):
        sig = inspect.signature(inspect_component)
        for name in [
            "search",
            "category",
            "type_name",
            "include_enum_values",
            "include_defaults",
            "page",
            "page_size",
        ]:
            assert sig.parameters[name].default is None, (
                f"{name} should default to None"
            )

    def test_tool_has_read_only_annotation(self):
        from services.registry import get_registered_tools

        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "inspect_component"), None)
        assert tool is not None
        annotations = tool.get("kwargs", {}).get("annotations")
        assert annotations is not None
        assert annotations.readOnlyHint is True


class TestInspectComponentForwarding:
    """Tests that parameters are correctly forwarded to Unity."""

    @pytest.mark.asyncio
    async def test_list_action_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"types": [], "total": 0}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(ctx=_DummyContext(), action="list", search="Light")
        assert captured["params"]["action"] == "list"
        assert captured["params"]["search"] == "Light"

    @pytest.mark.asyncio
    async def test_list_with_category(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"types": [], "total": 0}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(
            ctx=_DummyContext(), action="list", category="Physics 2D"
        )
        assert captured["params"]["category"] == "Physics 2D"

    @pytest.mark.asyncio
    async def test_list_with_pagination(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"types": [], "total": 0}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(
            ctx=_DummyContext(), action="list", page=2, page_size=25
        )
        assert captured["params"]["page"] == 2
        assert captured["params"]["pageSize"] == 25

    @pytest.mark.asyncio
    async def test_schema_action_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"properties": []}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(
            ctx=_DummyContext(), action="schema", type_name="Rigidbody"
        )
        assert captured["params"]["action"] == "schema"
        assert captured["params"]["typeName"] == "Rigidbody"

    @pytest.mark.asyncio
    async def test_schema_with_enum_and_defaults_flags(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"properties": []}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(
            ctx=_DummyContext(),
            action="schema",
            type_name="Light",
            include_enum_values=False,
            include_defaults=False,
        )
        assert captured["params"]["includeEnumValues"] is False
        assert captured["params"]["includeDefaults"] is False

    @pytest.mark.asyncio
    async def test_no_optional_params_not_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"types": [], "total": 0}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(ctx=_DummyContext(), action="list")
        assert "search" not in captured["params"]
        assert "category" not in captured["params"]
        assert "page" not in captured["params"]
        assert "pageSize" not in captured["params"]
        assert "typeName" not in captured["params"]

    @pytest.mark.asyncio
    async def test_string_coercion_for_page_params(self, monkeypatch):
        """String values for page/page_size should be coerced to int."""
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"types": [], "total": 0}}

        import services.tools.inspect_component as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_component(
            ctx=_DummyContext(), action="list", page="1", page_size="20"
        )
        assert captured["params"]["page"] == 1
        assert captured["params"]["pageSize"] == 20
