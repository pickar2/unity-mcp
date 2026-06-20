"""Tests for inspect_buffer tool."""

import inspect

import pytest

from services.tools.inspect_buffer import inspect_buffer


class TestInspectBufferSignature:
    """Tests for inspect_buffer parameter signatures."""

    def test_target_parameter_exists(self):
        sig = inspect.signature(inspect_buffer)
        assert "target" in sig.parameters

    def test_start_parameter_exists(self):
        sig = inspect.signature(inspect_buffer)
        assert "start" in sig.parameters

    def test_count_parameter_exists(self):
        sig = inspect.signature(inspect_buffer)
        assert "count" in sig.parameters

    def test_format_parameter_exists(self):
        sig = inspect.signature(inspect_buffer)
        assert "format" in sig.parameters

    def test_list_only_parameter_exists(self):
        sig = inspect.signature(inspect_buffer)
        assert "list_only" in sig.parameters

    def test_optional_params_default_to_none(self):
        sig = inspect.signature(inspect_buffer)
        for name in ["start", "count", "format", "list_only"]:
            assert sig.parameters[name].default is None, (
                f"{name} should default to None"
            )

    def test_tool_has_title_annotation(self):
        from services.registry import get_registered_tools

        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "inspect_buffer"), None)
        assert tool is not None
        annotations = tool.get("kwargs", {}).get("annotations")
        assert annotations is not None
        assert annotations.title == "Inspect Buffer"


class TestInspectBufferForwarding:
    """Tests that parameters are correctly forwarded to Unity."""

    @pytest.mark.asyncio
    async def test_list_only_forwarded_true(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"matches": []}}

        import services.tools.inspect_buffer as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_buffer(
            ctx=_DummyContext(), target="*/SomeComponent.*", list_only=True
        )
        assert captured["params"]["target"] == "*/SomeComponent.*"
        assert captured["params"]["listOnly"] is True

    @pytest.mark.asyncio
    async def test_start_and_count_coerced_from_strings(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.inspect_buffer as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_buffer(
            ctx=_DummyContext(),
            target="instanceId:12345/SomeComponent.buffer",
            start="4",
            count="16",
        )
        assert captured["params"]["start"] == 4
        assert captured["params"]["count"] == 16
        # list_only defaults to False, still forwarded explicitly
        assert captured["params"]["listOnly"] is False

    @pytest.mark.asyncio
    async def test_format_forwarded(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {}}

        import services.tools.inspect_buffer as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        fmt = "position:float3@0,velocity:float3@16"
        await inspect_buffer(
            ctx=_DummyContext(),
            target="instanceId:1/SomeComponent.buffer",
            format=fmt,
        )
        assert captured["params"]["format"] == fmt

    @pytest.mark.asyncio
    async def test_none_optionals_omitted(self, monkeypatch):
        captured = {}

        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            captured["params"] = params
            return {"success": True, "message": "ok", "data": {"matches": []}}

        import services.tools.inspect_buffer as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        await inspect_buffer(ctx=_DummyContext(), target="*/X.*", list_only=True)
        # start/count/format are None and should NOT be forwarded
        assert "start" not in captured["params"]
        assert "count" not in captured["params"]
        assert "format" not in captured["params"]

    @pytest.mark.asyncio
    async def test_success_response_passthrough(self, monkeypatch):
        async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kw):
            return {
                "success": True,
                "message": "Read 8 elements.",
                "data": {"elements": [{"x": 1}]},
            }

        import services.tools.inspect_buffer as mod

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        from tests.integration.conftest import _DummyContext

        resp = await inspect_buffer(
            ctx=_DummyContext(), target="*/X.*", list_only=True
        )
        assert resp["success"] is True
        assert resp["message"] == "Read 8 elements."
        assert resp["data"] == {"elements": [{"x": 1}]}
