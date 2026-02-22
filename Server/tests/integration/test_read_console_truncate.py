import pytest

from .test_helpers import DummyContext, DummyMCP


def setup_console_tools():
    """Setup console-related tools for testing."""
    mcp = DummyMCP()
    import services.tools.read_console
    from services.registry import get_registered_tools

    for tool_info in get_registered_tools():
        tool_name = tool_info["name"]
        if any(keyword in tool_name for keyword in ["read_console", "console"]):
            mcp.tools[tool_name] = tool_info["func"]
    return mcp.tools


@pytest.mark.asyncio
async def test_read_console_full_default(monkeypatch):
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "entries": [
                    {
                        "sequenceId": 1,
                        "timestamp": "2026-01-01T00:00:00Z",
                        "type": "error",
                        "message": "oops",
                        "stackTrace": None,
                    }
                ],
                "latestSequenceId": 1,
            },
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    resp = await read_console(ctx=DummyContext(), action="get", count=10)
    assert resp["success"] is True
    assert resp["data"]["entries"][0]["message"] == "oops"
    assert captured["params"]["count"] == 10
    assert captured["params"]["includeStacktrace"] is False


@pytest.mark.asyncio
async def test_read_console_passes_include_stacktrace(monkeypatch):
    """Stacktrace inclusion is now handled entirely by the C# side."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "entries": [
                    {
                        "sequenceId": 1,
                        "timestamp": "2026-01-01T00:00:00Z",
                        "type": "error",
                        "message": "oops",
                        "stackTrace": "at Foo.Bar()",
                    }
                ],
                "latestSequenceId": 1,
            },
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    # With include_stacktrace=True, the param is forwarded to C#; Python does not strip
    resp = await read_console(
        ctx=DummyContext(), action="get", count=10, include_stacktrace=True
    )
    assert resp["success"] is True
    assert captured["params"]["includeStacktrace"] is True
    # The response is passed through as-is from C#
    assert resp["data"]["entries"][0]["stackTrace"] == "at Foo.Bar()"

    # With include_stacktrace=False, the param is forwarded to C#; Python does not strip
    captured.clear()
    resp = await read_console(
        ctx=DummyContext(), action="get", count=10, include_stacktrace=False
    )
    assert resp["success"] is True
    assert captured["params"]["includeStacktrace"] is False


@pytest.mark.asyncio
async def test_read_console_default_count(monkeypatch):
    """Test that read_console defaults to count=100 when not specified."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"entries": [], "latestSequenceId": 0},
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    # Call without specifying count - should default to 100
    resp = await read_console(ctx=DummyContext(), action="get")
    assert resp["success"] is True
    assert captured["params"]["count"] == 100


@pytest.mark.asyncio
async def test_read_console_default_count_not_applied_when_paging(monkeypatch):
    """Test that default count is not applied when page_size is specified."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"entries": [], "latestSequenceId": 0},
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    # With page_size, count should NOT be defaulted
    resp = await read_console(ctx=DummyContext(), action="get", page_size=20)
    assert resp["success"] is True
    assert "count" not in captured["params"]
    assert captured["params"]["pageSize"] == 20


@pytest.mark.asyncio
async def test_read_console_paging(monkeypatch):
    """Test that read_console paging works with page_size and cursor."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        page_size = params.get("pageSize", 50)
        cursor_val = params.get("cursor", 0)
        all_entries = [
            {
                "sequenceId": i,
                "timestamp": f"2026-01-01T00:00:{i:02d}Z",
                "type": "error",
                "message": f"error {i}",
                "stackTrace": None,
            }
            for i in range(25)
        ]
        start = cursor_val
        end = min(start + page_size, len(all_entries))
        entries = all_entries[start:end]

        return {
            "success": True,
            "data": {
                "entries": entries,
                "cursor": cursor_val,
                "pageSize": page_size,
                "nextCursor": end if end < len(all_entries) else None,
                "totalMatches": len(all_entries),
                "hasMore": end < len(all_entries),
                "latestSequenceId": 24,
            },
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    # First page
    resp = await read_console(ctx=DummyContext(), action="get", page_size=5, cursor=0)
    assert resp["success"] is True
    assert captured["params"]["pageSize"] == 5
    assert captured["params"]["cursor"] == 0
    assert len(resp["data"]["entries"]) == 5
    assert resp["data"]["hasMore"] is True
    assert resp["data"]["nextCursor"] == 5

    # Last page
    resp = await read_console(ctx=DummyContext(), action="get", page_size=5, cursor=20)
    assert resp["success"] is True
    assert len(resp["data"]["entries"]) == 5
    assert resp["data"]["hasMore"] is False
    assert resp["data"]["nextCursor"] is None


@pytest.mark.asyncio
async def test_read_console_since_sequence_id(monkeypatch):
    """Test that since_sequence_id is forwarded to C# as sinceSequenceId."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"entries": [], "latestSequenceId": 42},
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    resp = await read_console(ctx=DummyContext(), action="get", since_sequence_id=42)
    assert resp["success"] is True
    assert captured["params"]["sinceSequenceId"] == 42


@pytest.mark.asyncio
async def test_read_console_count_only(monkeypatch):
    """Test that count_only mode works."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "error": 3,
                "warning": 5,
                "log": 10,
                "total": 18,
                "latestSequenceId": 42,
            },
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    resp = await read_console(ctx=DummyContext(), action="get", count_only=True)
    assert resp["success"] is True
    assert captured["params"]["countOnly"] is True
    assert resp["data"]["total"] == 18


@pytest.mark.asyncio
async def test_read_console_types_json_string(monkeypatch):
    """Test that read_console handles types parameter as JSON string (fixes issue #561)."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"entries": [], "latestSequenceId": 0},
        }

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    # Test with types as JSON string (the problematic case from issue #561)
    resp = await read_console(
        ctx=DummyContext(), action="get", types='["error", "warning", "all"]'
    )
    assert resp["success"] is True
    # Verify types was parsed correctly and sent as a list
    assert isinstance(captured["params"]["types"], list)
    assert captured["params"]["types"] == ["error", "warning", "all"]

    # Test case normalization to lowercase
    captured.clear()
    resp = await read_console(
        ctx=DummyContext(), action="get", types='["ERROR", "Warning", "LOG"]'
    )
    assert resp["success"] is True
    assert captured["params"]["types"] == ["error", "warning", "log"]

    # Test with types as actual list (should still work)
    captured.clear()
    resp = await read_console(
        ctx=DummyContext(), action="get", types=["error", "warning"]
    )
    assert resp["success"] is True
    assert isinstance(captured["params"]["types"], list)
    assert captured["params"]["types"] == ["error", "warning"]


@pytest.mark.asyncio
async def test_read_console_types_validation(monkeypatch):
    """Test that read_console validates types entries and rejects invalid values."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "data": {"entries": []}}

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    # Invalid entry in list should return a clear error and not send.
    captured.clear()
    resp = await read_console(
        ctx=DummyContext(), action="get", types='["error", "nope"]'
    )
    assert resp["success"] is False
    assert "invalid types entry" in resp["message"]
    assert captured == {}

    # Non-string entry should return a clear error and not send.
    captured.clear()
    resp = await read_console(ctx=DummyContext(), action="get", types='[1, "error"]')
    assert resp["success"] is False
    assert "types entries must be strings" in resp["message"]
    assert captured == {}


@pytest.mark.asyncio
async def test_read_console_clear_action(monkeypatch):
    """Test that clear action works."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "Console cleared successfully."}

    import services.tools.read_console as read_console_mod

    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send,
    )

    resp = await read_console(ctx=DummyContext(), action="clear")
    assert resp["success"] is True
    assert captured["params"]["action"] == "clear"


@pytest.mark.asyncio
async def test_read_console_no_format_param(monkeypatch):
    """Test that the old format parameter is no longer accepted."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    import inspect

    sig = inspect.signature(read_console)
    param_names = list(sig.parameters.keys())
    assert "format" not in param_names, "format parameter should have been removed"
