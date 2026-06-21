"""Tests for manage_editor tool."""
import asyncio
import inspect
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.resources.editor_state import EditorStateCompilation
from services.tools.manage_editor import manage_editor
import services.tools.manage_editor as manage_editor_mod
from services.registry import get_registered_tools

# ── Fixture ──────────────────────────────────────────────────────────


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_editor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_editor.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── Undo/Redo ────────────────────────────────────────────────────────


def test_undo_forwards_to_unity(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="undo"))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "undo"
    assert mock_unity["tool_name"] == "manage_editor"


def test_redo_forwards_to_unity(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="redo"))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "redo"


# ── All Unity-forwarded actions ──────────────────────────────────────

UNITY_FORWARDED_ACTIONS = [
    "play", "pause", "stop", "set_active_tool",
    "add_tag", "remove_tag", "add_layer", "remove_layer",
    "deploy_package", "restore_package",
    "undo", "redo",
]


@pytest.mark.parametrize("action_name", UNITY_FORWARDED_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    result = asyncio.run(manage_editor(SimpleNamespace(), action=action_name))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == action_name


# ── Python-only actions ──────────────────────────────────────────────


def test_telemetry_status_handled_python_side(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="telemetry_status"))
    assert result["success"] is True
    assert "telemetry_enabled" in result
    assert "params" not in mock_unity


def test_telemetry_ping_handled_python_side(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="telemetry_ping"))
    assert result["success"] is True
    assert "params" not in mock_unity


# ── None params omitted ─────────────────────────────────────────────


def test_undo_omits_none_params(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="undo"))
    assert result["success"] is True
    params = mock_unity["params"]
    assert "toolName" not in params
    assert "tagName" not in params
    assert "layerName" not in params
    assert "recompile" not in params
    assert "paused" not in params


# ── paused / recompile params ────────────────────────────────────────


def test_play_forwards_paused_param(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="play", paused=True))
    assert result["success"] is True
    assert mock_unity["params"]["paused"] is True


def test_play_forwards_recompile_param(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="play", recompile=True))
    assert result["success"] is True
    assert mock_unity["params"]["recompile"] is True


def test_play_paused_accepts_string_truthy(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="play", paused="true"))
    assert result["success"] is True
    assert mock_unity["params"]["paused"] is True


# ── Deferred recompile flow ──────────────────────────────────────────


@pytest.fixture
def mock_recompile_env(monkeypatch):
    """Shared setup for deferred-recompile tests: mock send, readiness, compilation check."""
    monkeypatch.setattr(manage_editor_mod, "_RELOAD_START_DELAY_S", 0.0)
    monkeypatch.setattr(
        "services.tools.manage_editor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )

    async def fake_wait(ctx, timeout_s=30.0):
        return (True, 3.0)

    monkeypatch.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)

    async def fake_check(ctx):
        return False

    monkeypatch.setattr(manage_editor_mod, "_check_compilation_failed", fake_check)
    return monkeypatch


@pytest.fixture
def mock_recompile_env_no_check(monkeypatch):
    """Like mock_recompile_env but does NOT mock `_check_compilation_failed`.

    For tests that exercise the real detection path: they mock the underlying
    `editor_state.get_editor_state` resource instead, so `_check_compilation_failed`
    runs for real.
    """
    monkeypatch.setattr(manage_editor_mod, "_RELOAD_START_DELAY_S", 0.0)
    monkeypatch.setattr(
        "services.tools.manage_editor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )

    async def fake_wait(ctx, timeout_s=30.0):
        return (True, 3.0)

    monkeypatch.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)
    return monkeypatch


@pytest.mark.asyncio
async def test_play_without_recompile_passes_through(mock_recompile_env):
    """Non-recompile play action forwards normally (no deferred flow)."""
    captured = {}

    async def fake_send(_send_fn, _inst, _cmd, params):
        captured["params"] = params
        return {"success": True, "message": "Entered play mode."}

    mock_recompile_env.setattr("services.tools.manage_editor.send_with_unity_instance", fake_send)

    resp = await manage_editor(SimpleNamespace(), action="play")
    assert resp["success"] is True
    assert captured["params"]["action"] == "play"
    assert "recompile" not in captured["params"]


@pytest.mark.asyncio
async def test_play_with_recompile_deferred_success(mock_recompile_env):
    """When Unity returns pending=recompile, Python waits and enters play mode."""
    call_log = []

    async def fake_send(_send_fn, _inst, cmd, params):
        call_log.append((cmd, dict(params)))
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "message": "Recompile initiated.",
                "data": {
                    "pending": "recompile",
                    "enterPlayMode": True,
                    "paused": False,
                },
            }
        if (
            cmd == "manage_editor"
            and params.get("action") == "play"
            and "recompile" not in params
        ):
            return {"success": True, "message": "Entered play mode."}
        if cmd == "read_console":
            return {"success": True, "data": {"entries": []}}
        return {"success": True, "data": {}}

    mock_recompile_env.setattr("services.tools.manage_editor.send_with_unity_instance", fake_send)

    resp = await manage_editor(SimpleNamespace(), action="play", recompile=True)

    assert resp["success"] is True
    assert "Recompile succeeded" in resp["message"]
    assert resp["data"]["recompiled"] is True

    # Verify two calls: initial recompile + follow-up play
    editor_calls = [(c, p) for c, p in call_log if c == "manage_editor"]
    assert len(editor_calls) == 2
    assert editor_calls[0][1].get("recompile") is True
    assert editor_calls[1][1].get("action") == "play"


@pytest.mark.asyncio
async def test_play_with_recompile_compilation_failure(mock_recompile_env_no_check):
    """When script_compilation_failed=True is emitted in editor state, return error with console entries.

    This test does NOT mock `_check_compilation_failed`; it exercises the real
    detection path by mocking the underlying `editor_state.get_editor_state`
    resource to return `script_compilation_failed=True` (mimicking what
    `EditorStateCache` emits when `EditorUtility.scriptCompilationFailed` is
    true on the C# side).

    Would fail if: the field name changed on either side, the field disappeared
    from the producer/consumer, or `_check_compilation_failed` stopped looking
    it up.
    """
    from models import MCPResponse
    from services.resources import editor_state as editor_state_mod

    async def fake_get_editor_state(ctx):
        return MCPResponse(
            success=True,
            message="Retrieved editor state.",
            data={
                "schema_version": "unity-mcp/editor_state@2",
                "observed_at_unix_ms": 0,
                "sequence": 1,
                "compilation": {
                    "is_compiling": False,
                    "is_domain_reload_pending": False,
                    "script_compilation_failed": True,
                },
            },
        )

    mock_recompile_env_no_check.setattr(
        editor_state_mod, "get_editor_state", fake_get_editor_state
    )

    async def fake_send(_send_fn, _inst, cmd, params):
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "message": "Recompile initiated.",
                "data": {
                    "pending": "recompile",
                    "enterPlayMode": True,
                    "paused": False,
                },
            }
        if cmd == "read_console":
            return {
                "success": True,
                "data": {
                    "entries": [
                        {
                            "message": "Assets/Scripts/Foo.cs(10,5): error CS1002: ; expected",
                            "type": "error",
                        }
                    ]
                },
            }
        return {"success": True}

    mock_recompile_env_no_check.setattr(
        "services.tools.manage_editor.send_with_unity_instance", fake_send
    )

    resp = await manage_editor(SimpleNamespace(), action="play", recompile=True)

    assert resp["success"] is False
    assert "Compilation failed" in resp["message"]
    assert resp["data"]["compilation_failed"] is True
    assert len(resp["data"]["errors"]) == 1


@pytest.mark.asyncio
async def test_play_with_recompile_check_error_falls_back_to_console(
    mock_recompile_env_no_check,
):
    """When editor_state lookup itself raises, fall back to console-error scan.

    Would fail if `_check_compilation_failed` swallowed the exception and
    returned False (the previous behavior): the test would then enter play
    mode instead of returning a compilation-failed response.
    """
    from services.resources import editor_state as editor_state_mod

    async def failing_get_editor_state(ctx):
        raise RuntimeError("transport hiccup")

    mock_recompile_env_no_check.setattr(
        editor_state_mod, "get_editor_state", failing_get_editor_state
    )

    async def fake_send(_send_fn, _inst, cmd, params):
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "data": {"pending": "recompile", "enterPlayMode": True, "paused": False},
            }
        if cmd == "read_console":
            return {
                "success": True,
                "data": {
                    "entries": [
                        {"message": "Assets/Scripts/Foo.cs(3,1): error CS0116:", "type": "error"}
                    ]
                },
            }
        return {"success": True}

    mock_recompile_env_no_check.setattr(
        "services.tools.manage_editor.send_with_unity_instance", fake_send
    )

    resp = await manage_editor(SimpleNamespace(), action="play", recompile=True)

    # Console errors present after a check failure → treated as compilation failed,
    # with the underlying check error surfaced in the response payload.
    assert resp["success"] is False
    assert "Compilation failed" in resp["message"]
    assert resp["data"]["compilation_failed"] is True
    assert "transport hiccup" in resp["data"]["compilation_check_error"]


@pytest.mark.asyncio
async def test_play_with_recompile_timeout(mock_recompile_env):
    """When editor never becomes ready after reload, return timeout error."""
    async def fake_wait(ctx, timeout_s=30.0):
        return (False, timeout_s)

    mock_recompile_env.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)

    async def fake_send(_send_fn, _inst, cmd, params):
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "data": {"pending": "recompile", "enterPlayMode": True, "paused": False},
            }
        return {"success": True}

    mock_recompile_env.setattr("services.tools.manage_editor.send_with_unity_instance", fake_send)

    resp = await manage_editor(SimpleNamespace(), action="play", recompile=True)

    assert resp["success"] is False
    assert "Timed out" in resp["message"]


@pytest.mark.asyncio
async def test_play_with_recompile_paused(mock_recompile_env):
    """Paused flag is forwarded through the deferred recompile flow."""
    call_log = []

    async def fake_send(_send_fn, _inst, cmd, params):
        call_log.append((cmd, dict(params)))
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "data": {"pending": "recompile", "enterPlayMode": True, "paused": True},
            }
        if cmd == "manage_editor" and params.get("action") == "play":
            return {"success": True, "message": "Entered play mode (paused)."}
        return {"success": True, "data": {}}

    mock_recompile_env.setattr("services.tools.manage_editor.send_with_unity_instance", fake_send)

    resp = await manage_editor(SimpleNamespace(), action="play", recompile=True, paused=True)

    assert resp["success"] is True
    assert resp["data"]["paused"] is True

    # Verify follow-up play call (without recompile) included paused param
    play_calls = [
        (c, p)
        for c, p in call_log
        if c == "manage_editor" and p.get("action") == "play" and "recompile" not in p
    ]
    assert len(play_calls) == 1
    assert play_calls[0][1].get("paused") is True


@pytest.mark.asyncio
async def test_non_recompile_actions_unchanged(mock_recompile_env):
    """stop/pause actions pass through without deferred logic."""
    for action_name in ("stop", "pause"):
        captured = {}

        async def fake_send(_send_fn, _inst, cmd, params):
            captured["params"] = params
            return {"success": True, "message": f"{action_name} done."}

        mock_recompile_env.setattr("services.tools.manage_editor.send_with_unity_instance", fake_send)

        resp = await manage_editor(SimpleNamespace(), action=action_name)
        assert resp["success"] is True
        assert captured["params"]["action"] == action_name


# ── EditorStateCompilation schema (mirrors EditorStateCache.cs DTO) ──


def test_editor_state_compilation_round_trips_script_compilation_failed():
    """The script_compilation_failed field must round-trip through the pydantic
    model that mirrors the C# EditorStateCompilation DTO.

    Would fail if the field disappeared from EditorStateCompilation — without
    it, pydantic validation silently strips the field and manage_editor's
    compilation-failure detection treats every failed recompile as success.
    """
    # Field must be declared on the model class.
    assert "script_compilation_failed" in EditorStateCompilation.model_fields, (
        "EditorStateCompilation must declare script_compilation_failed so it "
        "survives pydantic validation."
    )

    # Field must round-trip through serialization.
    model = EditorStateCompilation(script_compilation_failed=True)
    dumped = model.model_dump()
    assert dumped["script_compilation_failed"] is True

    # Default must be None (not False) so consumers can distinguish "explicitly
    # false" from "field absent" — the latter would indicate the producer did
    # not emit it.
    assert EditorStateCompilation().script_compilation_failed is None

