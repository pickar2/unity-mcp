import pytest

from .test_helpers import DummyContext, setup_script_tools


@pytest.mark.asyncio
async def test_validate_script_returns_counts(monkeypatch):
    tools = setup_script_tools()
    validate_script = tools["validate_script"]

    async def fake_send(send_fn, instance, cmd, params, **kwargs):
        return {
            "success": True,
            "data": {
                "diagnostics": [
                    {"severity": "warning"},
                    {"severity": "error"},
                    {"severity": "fatal"},
                ]
            },
        }

    import services.tools.manage_script as ms_mod

    monkeypatch.setattr(ms_mod, "send_with_unity_instance", fake_send)

    resp = await validate_script(
        DummyContext(), uri="mcpforunity://path/Assets/Scripts/A.cs"
    )
    assert resp == {"success": True, "data": {"warnings": 1, "errors": 2}}
