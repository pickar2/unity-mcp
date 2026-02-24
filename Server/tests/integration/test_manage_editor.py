"""Tests for manage_editor tool, focusing on deferred recompile flow."""

import asyncio
import os

import pytest

from .test_helpers import DummyContext
import services.tools.manage_editor as mod


@pytest.mark.asyncio
async def test_play_without_recompile_passes_through(monkeypatch):
    """Non-recompile play action forwards normally."""
    captured = {}

    async def fake_send(_send_fn, _inst, _cmd, params, **kw):
        captured["params"] = params
        return {
            "success": True,
            "message": "Entered play mode.",
            "data": {"recompiled": False},
        }

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "Test@dummy")

    resp = await mod.manage_editor(ctx=ctx, action="play")
    assert resp["success"] is True
    assert captured["params"]["action"] == "play"
    assert "recompile" not in captured["params"]


@pytest.mark.asyncio
async def test_play_with_recompile_deferred_success(monkeypatch):
    """When Unity returns pending=recompile, Python waits and enters play mode."""
    call_log = []

    async def fake_send(_send_fn, _inst, cmd, params, **kw):
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

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    # Stub wait_for_editor_ready to return immediately (bypass actual polling)
    async def fake_wait(ctx, timeout_s=30.0):
        return (True, 3.0)

    monkeypatch.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)

    # Stub _check_compilation_failed
    async def fake_check(ctx):
        return False

    monkeypatch.setattr(mod, "_check_compilation_failed", fake_check)

    # Reduce sleep delay for test speed
    monkeypatch.setattr(mod, "_RELOAD_START_DELAY_S", 0.0)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "Test@dummy")

    resp = await mod.manage_editor(ctx=ctx, action="play", recompile=True)

    assert resp["success"] is True
    assert "Recompile succeeded" in resp["message"]
    assert resp["data"]["recompiled"] is True

    # Verify two calls: initial recompile + follow-up play
    editor_calls = [(c, p) for c, p in call_log if c == "manage_editor"]
    assert len(editor_calls) == 2
    assert editor_calls[0][1].get("recompile") is True
    assert editor_calls[1][1].get("action") == "play"


@pytest.mark.asyncio
async def test_play_with_recompile_compilation_failure(monkeypatch):
    """When compilation fails after reload, return error with console entries."""

    async def fake_send(_send_fn, _inst, cmd, params, **kw):
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

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    async def fake_wait(ctx, timeout_s=30.0):
        return (True, 5.0)

    monkeypatch.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)

    async def fake_check(ctx):
        return True  # Compilation failed

    monkeypatch.setattr(mod, "_check_compilation_failed", fake_check)
    monkeypatch.setattr(mod, "_RELOAD_START_DELAY_S", 0.0)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "Test@dummy")

    resp = await mod.manage_editor(ctx=ctx, action="play", recompile=True)

    assert resp["success"] is False
    assert "Compilation failed" in resp["message"]
    assert resp["data"]["compilation_failed"] is True
    assert len(resp["data"]["errors"]) == 1


@pytest.mark.asyncio
async def test_play_with_recompile_timeout(monkeypatch):
    """When editor never becomes ready after reload, return timeout error."""

    async def fake_send(_send_fn, _inst, cmd, params, **kw):
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "data": {
                    "pending": "recompile",
                    "enterPlayMode": True,
                    "paused": False,
                },
            }
        return {"success": True}

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    async def fake_wait(ctx, timeout_s=30.0):
        return (False, timeout_s)  # Timed out

    monkeypatch.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)
    monkeypatch.setattr(mod, "_RELOAD_START_DELAY_S", 0.0)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "Test@dummy")

    resp = await mod.manage_editor(ctx=ctx, action="play", recompile=True)

    assert resp["success"] is False
    assert "Timed out" in resp["message"]


@pytest.mark.asyncio
async def test_play_with_recompile_paused(monkeypatch):
    """Paused flag is forwarded through the deferred recompile flow."""
    call_log = []

    async def fake_send(_send_fn, _inst, cmd, params, **kw):
        call_log.append((cmd, dict(params)))
        if cmd == "manage_editor" and params.get("recompile") is True:
            return {
                "success": True,
                "data": {"pending": "recompile", "enterPlayMode": True, "paused": True},
            }
        if cmd == "manage_editor" and params.get("action") == "play":
            return {"success": True, "message": "Entered play mode (paused)."}
        return {"success": True, "data": {}}

    monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

    async def fake_wait(ctx, timeout_s=30.0):
        return (True, 2.0)

    monkeypatch.setattr("services.tools.refresh_unity.wait_for_editor_ready", fake_wait)

    async def fake_check(ctx):
        return False

    monkeypatch.setattr(mod, "_check_compilation_failed", fake_check)
    monkeypatch.setattr(mod, "_RELOAD_START_DELAY_S", 0.0)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "Test@dummy")

    resp = await mod.manage_editor(ctx=ctx, action="play", recompile=True, paused=True)

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
async def test_non_recompile_actions_unchanged(monkeypatch):
    """stop, pause, step actions pass through without deferred logic."""
    for action_name in ("stop", "pause"):
        captured = {}

        async def fake_send(_send_fn, _inst, cmd, params, **kw):
            captured["params"] = params
            return {"success": True, "message": f"{action_name} done."}

        monkeypatch.setattr(mod, "send_with_unity_instance", fake_send)

        ctx = DummyContext()
        ctx.set_state("unity_instance", "Test@dummy")

        resp = await mod.manage_editor(ctx=ctx, action=action_name)
        assert resp["success"] is True
        assert captured["params"]["action"] == action_name
