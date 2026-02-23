"""Tests for manage_prefabs tool - component and component_properties parameters."""

import inspect

import pytest

from services.tools.manage_prefabs import manage_prefabs


class TestManagePrefabsComponentProperties:
    """Tests for the component_properties parameter on manage_prefabs."""

    def test_component_properties_parameter_exists(self):
        """The manage_prefabs tool should have a component_properties parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "component_properties" in sig.parameters

    def test_component_properties_parameter_is_optional(self):
        """component_properties should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["component_properties"]
        assert param.default is None

    def test_tool_description_mentions_component_properties(self):
        """The tool description should mention component_properties."""
        from services.registry import get_registered_tools

        tools = get_registered_tools()
        prefab_tool = next((t for t in tools if t["name"] == "manage_prefabs"), None)
        assert prefab_tool is not None
        # Description is stored at top level or in kwargs depending on how the decorator stores it
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get(
            "description", ""
        )
        assert "component_properties" in desc

    def test_required_params_include_modify_contents(self):
        """modify_contents should be a valid action requiring prefab_path."""
        from services.tools.manage_prefabs import REQUIRED_PARAMS

        assert "modify_contents" in REQUIRED_PARAMS
        assert "prefab_path" in REQUIRED_PARAMS["modify_contents"]


class TestManagePrefabsSingleComponentProperties:
    """Tests for the component + properties shorthand on manage_prefabs."""

    def test_component_parameter_exists(self):
        """The manage_prefabs tool should have a component parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "component" in sig.parameters

    def test_properties_parameter_exists(self):
        """The manage_prefabs tool should have a properties parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "properties" in sig.parameters

    def test_component_parameter_is_optional(self):
        """component should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["component"]
        assert param.default is None

    def test_properties_parameter_is_optional(self):
        """properties should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["properties"]
        assert param.default is None

    def test_tool_description_mentions_component_properties_shorthand(self):
        """The tool description should mention the component + properties shorthand."""
        from services.registry import get_registered_tools

        tools = get_registered_tools()
        prefab_tool = next((t for t in tools if t["name"] == "manage_prefabs"), None)
        assert prefab_tool is not None
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get(
            "description", ""
        )
        assert "component=" in desc or "component +" in desc


class TestManagePrefabsModifyContentsForwarding:
    """Tests that component/properties params are forwarded to Unity correctly."""

    @pytest.mark.asyncio
    async def test_single_component_properties_forwarded(self, monkeypatch):
        """component + properties should be forwarded as 'component' and 'properties' keys."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok"}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        resp = await manage_prefabs(
            ctx=_DummyContext(),
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            target="Root",
            component="Rigidbody",
            properties={"mass": 5.0, "useGravity": False},
        )
        assert resp["success"] is True
        assert captured["params"]["component"] == "Rigidbody"
        assert captured["params"]["properties"] == {"mass": 5.0, "useGravity": False}

    @pytest.mark.asyncio
    async def test_component_properties_dict_forwarded(self, monkeypatch):
        """component_properties dict should be forwarded as 'componentProperties'."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok"}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        resp = await manage_prefabs(
            ctx=_DummyContext(),
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            component_properties={"Health": {"maxHP": 50}, "Mover": {"speed": 2.0}},
        )
        assert resp["success"] is True
        assert captured["params"]["componentProperties"] == {
            "Health": {"maxHP": 50},
            "Mover": {"speed": 2.0},
        }

    @pytest.mark.asyncio
    async def test_component_without_properties_not_forwarded(self, monkeypatch):
        """component alone (without properties) should not be forwarded."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok"}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        resp = await manage_prefabs(
            ctx=_DummyContext(),
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            component="Rigidbody",
        )
        assert resp["success"] is True
        assert captured["params"].get("component") == "Rigidbody"
        assert "properties" not in captured["params"]


class TestManagePrefabsComponentsParam:
    """Tests for the components parameter on get_info/get_hierarchy."""

    def test_components_parameter_exists(self):
        """The manage_prefabs tool should have a components parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "components" in sig.parameters

    def test_components_parameter_is_optional(self):
        """components should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["components"]
        assert param.default is None

    @pytest.mark.asyncio
    async def test_components_bool_true_forwarded(self, monkeypatch):
        """components=True should be forwarded to Unity."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_info",
            prefab_path="Assets/Prefabs/Test.prefab",
            components=True,
        )
        assert captured["params"]["components"] is True

    @pytest.mark.asyncio
    async def test_components_list_forwarded(self, monkeypatch):
        """components=['Rigidbody', 'Light'] should be forwarded as a list."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_info",
            prefab_path="Assets/Prefabs/Test.prefab",
            components=["Rigidbody", "Light"],
        )
        assert captured["params"]["components"] == ["Rigidbody", "Light"]

    @pytest.mark.asyncio
    async def test_components_string_true_forwarded_as_bool(self, monkeypatch):
        """components='true' (string) should be forwarded as bool True."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_info",
            prefab_path="Assets/Prefabs/Test.prefab",
            components="true",
        )
        assert captured["params"]["components"] is True

    @pytest.mark.asyncio
    async def test_components_json_string_parsed(self, monkeypatch):
        """components='["Rigidbody"]' (JSON string) should be parsed to a list."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_info",
            prefab_path="Assets/Prefabs/Test.prefab",
            components='["Rigidbody"]',
        )
        assert captured["params"]["components"] == ["Rigidbody"]

    @pytest.mark.asyncio
    async def test_components_none_not_forwarded(self, monkeypatch):
        """components=None should not add a components key to params."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_info",
            prefab_path="Assets/Prefabs/Test.prefab",
        )
        assert "components" not in captured["params"]

    @pytest.mark.asyncio
    async def test_components_with_hierarchy_action(self, monkeypatch):
        """components should work with get_hierarchy action too."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_hierarchy",
            prefab_path="Assets/Prefabs/Test.prefab",
            components=True,
        )
        assert captured["params"]["components"] is True
        assert captured["params"]["action"] == "get_hierarchy"

    @pytest.mark.asyncio
    async def test_components_with_target(self, monkeypatch):
        """target + components should both be forwarded for get_info."""
        from tests.integration.conftest import _DummyContext

        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.manage_prefabs as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(mod, "preflight", lambda *a, **kw: _always_none())

        await manage_prefabs(
            ctx=_DummyContext(),
            action="get_info",
            prefab_path="Assets/Prefabs/Test.prefab",
            target="Child",
            components=["MyScript"],
        )
        assert captured["params"]["target"] == "Child"
        assert captured["params"]["components"] == ["MyScript"]


async def _always_none():
    return None
