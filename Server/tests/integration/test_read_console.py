"""
Integration tests for read_console tool.

Validates the full-stack behavior: Python parameter handling -> C# command
dispatching -> response parsing, using monkeypatched send functions to
simulate the C# side's LogCaptureService responses.
"""
import pytest

from .test_helpers import DummyContext, DummyMCP


def setup_console_tools():
    """Setup console-related tools for testing."""
    mcp = DummyMCP()
    import services.tools.read_console
    from services.registry import get_registered_tools
    for tool_info in get_registered_tools():
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['read_console', 'console']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


@pytest.mark.asyncio
async def test_read_console_get(monkeypatch):
    """Test basic console read with default parameters."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "entries": [
                    {"sequenceId": 1, "timestamp": "2026-01-01T00:00:00Z", "type": "error", "message": "test error", "stackTrace": None},
                    {"sequenceId": 2, "timestamp": "2026-01-01T00:00:01Z", "type": "warning", "message": "test warning", "stackTrace": None},
                    {"sequenceId": 3, "timestamp": "2026-01-01T00:00:02Z", "type": "log", "message": "test log", "stackTrace": None},
                ],
                "latestSequenceId": 3,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(
        ctx=DummyContext(),
        action="get",
        types=["error", "warning", "log"],
        count=10,
    )

    assert resp["success"] is True
    assert "entries" in resp["data"]
    assert len(resp["data"]["entries"]) == 3
    assert captured["params"]["types"] == ["error", "warning", "log"]
    assert captured["params"]["count"] == 10


@pytest.mark.asyncio
async def test_read_console_has_timestamps(monkeypatch):
    """Test that entries from LogCaptureService include timestamps and sequenceIds."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        return {
            "success": True,
            "data": {
                "entries": [
                    {"sequenceId": 10, "timestamp": "2026-02-01T12:30:00Z", "type": "log", "message": "hello", "stackTrace": None},
                    {"sequenceId": 11, "timestamp": "2026-02-01T12:30:01Z", "type": "log", "message": "world", "stackTrace": None},
                ],
                "latestSequenceId": 11,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(ctx=DummyContext(), action="get", types=["log"], count=5)
    assert resp["success"] is True

    entries = resp["data"]["entries"]
    assert len(entries) == 2

    # Every entry must have timestamp and sequenceId
    for entry in entries:
        assert "timestamp" in entry, "Entry missing timestamp field"
        assert "sequenceId" in entry, "Entry missing sequenceId field"

    # Verify ordering: sequenceIds should be ascending
    assert entries[0]["sequenceId"] < entries[1]["sequenceId"]

    # latestSequenceId should match the last entry
    assert resp["data"]["latestSequenceId"] == entries[-1]["sequenceId"]


@pytest.mark.asyncio
async def test_read_console_since_sequence_id(monkeypatch):
    """Test filtering by sinceSequenceId for efficient polling."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    call_log = []

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        call_log.append(params)
        since = params.get("sinceSequenceId", 0)
        # Simulate: first call returns 3 entries, second call with since=3 returns 1 new entry
        if since == 0 or since is None:
            return {
                "success": True,
                "data": {
                    "entries": [
                        {"sequenceId": 1, "timestamp": "2026-01-01T00:00:00Z", "type": "log", "message": "first", "stackTrace": None},
                        {"sequenceId": 2, "timestamp": "2026-01-01T00:00:01Z", "type": "log", "message": "second", "stackTrace": None},
                        {"sequenceId": 3, "timestamp": "2026-01-01T00:00:02Z", "type": "log", "message": "third", "stackTrace": None},
                    ],
                    "latestSequenceId": 3,
                },
            }
        else:
            return {
                "success": True,
                "data": {
                    "entries": [
                        {"sequenceId": 4, "timestamp": "2026-01-01T00:00:03Z", "type": "log", "message": "fourth", "stackTrace": None},
                    ],
                    "latestSequenceId": 4,
                },
            }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    # First call: get initial entries
    result1 = await read_console(ctx=DummyContext(), action="get", types=["log"], count=10)
    assert result1["success"] is True
    latest_id = result1["data"]["latestSequenceId"]
    assert latest_id == 3

    # Second call: poll for new entries since last known ID
    result2 = await read_console(ctx=DummyContext(), action="get", types=["log"], since_sequence_id=latest_id)
    assert result2["success"] is True

    entries = result2["data"]["entries"]
    assert isinstance(entries, list)
    assert len(entries) == 1
    assert entries[0]["sequenceId"] == 4
    assert entries[0]["message"] == "fourth"

    # Verify sinceSequenceId was forwarded correctly in the params
    assert call_log[1]["sinceSequenceId"] == 3


@pytest.mark.asyncio
async def test_read_console_count_only(monkeypatch):
    """Test count_only mode returns counts by type without entries."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "error": 3,
                "warning": 7,
                "log": 42,
                "total": 52,
                "latestSequenceId": 100,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(ctx=DummyContext(), action="get", count_only=True)
    assert resp["success"] is True
    assert captured["params"]["countOnly"] is True

    data = resp["data"]
    assert "total" in data
    assert "error" in data
    assert "warning" in data
    assert "log" in data
    assert data["total"] == 52
    assert data["error"] == 3
    assert data["warning"] == 7
    assert data["log"] == 42


@pytest.mark.asyncio
async def test_read_console_empty_result(monkeypatch):
    """Test that an empty console returns success with empty entries."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        return {
            "success": True,
            "data": {
                "entries": [],
                "latestSequenceId": 0,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(ctx=DummyContext(), action="get", types=["error"], count=10)
    assert resp["success"] is True
    assert resp["data"]["entries"] == []
    assert resp["data"]["latestSequenceId"] == 0


@pytest.mark.asyncio
async def test_read_console_since_timestamp(monkeypatch):
    """Test filtering by sinceTimestamp parameter."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "entries": [
                    {"sequenceId": 50, "timestamp": "2026-02-01T14:00:00Z", "type": "error", "message": "late error", "stackTrace": None},
                ],
                "latestSequenceId": 50,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(
        ctx=DummyContext(),
        action="get",
        types=["error"],
        since_timestamp="2026-02-01T13:00:00Z",
    )
    assert resp["success"] is True
    assert captured["params"]["sinceTimestamp"] == "2026-02-01T13:00:00Z"
    assert len(resp["data"]["entries"]) == 1


@pytest.mark.asyncio
async def test_read_console_filter_text(monkeypatch):
    """Test text filtering of console entries."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "entries": [
                    {"sequenceId": 5, "timestamp": "2026-01-01T00:00:00Z", "type": "error", "message": "NullReferenceException", "stackTrace": None},
                ],
                "latestSequenceId": 5,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(
        ctx=DummyContext(),
        action="get",
        filter_text="NullReference",
    )
    assert resp["success"] is True
    assert captured["params"]["filterText"] == "NullReference"


@pytest.mark.asyncio
async def test_read_console_filter_regex(monkeypatch):
    """Test regex filtering of console entries is forwarded to C#."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "entries": [
                    {"sequenceId": 7, "timestamp": "2026-01-01T00:00:00Z", "type": "error", "message": "NullReferenceException", "stackTrace": None},
                ],
                "latestSequenceId": 7,
            },
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    resp = await read_console(
        ctx=DummyContext(),
        action="get",
        filter_regex="Null.*Exception",
    )
    assert resp["success"] is True
    assert captured["params"]["filterRegex"] == "Null.*Exception"
    # filterText must NOT be sent when filter_regex is used
    assert "filterText" not in captured["params"]


@pytest.mark.asyncio
async def test_read_console_polling_workflow(monkeypatch):
    """Test a realistic polling workflow: initial read, then incremental poll."""
    tools = setup_console_tools()
    read_console = tools["read_console"]

    call_count = 0

    async def fake_send(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        nonlocal call_count
        call_count += 1
        since = params.get("sinceSequenceId")

        if since is None:
            # Initial read
            return {
                "success": True,
                "data": {
                    "entries": [
                        {"sequenceId": i, "timestamp": f"2026-01-01T00:00:{i:02d}Z", "type": "log", "message": f"msg {i}", "stackTrace": None}
                        for i in range(1, 4)
                    ],
                    "latestSequenceId": 3,
                },
            }
        elif since == 3:
            # No new entries yet
            return {
                "success": True,
                "data": {
                    "entries": [],
                    "latestSequenceId": 3,
                },
            }
        elif since == 3 and call_count > 2:
            # New entries appeared
            return {
                "success": True,
                "data": {
                    "entries": [
                        {"sequenceId": 4, "timestamp": "2026-01-01T00:00:04Z", "type": "error", "message": "new error", "stackTrace": None},
                    ],
                    "latestSequenceId": 4,
                },
            }
        return {"success": True, "data": {"entries": [], "latestSequenceId": since}}

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)

    # Step 1: Initial read
    resp1 = await read_console(ctx=DummyContext(), action="get", count=10)
    assert resp1["success"] is True
    assert len(resp1["data"]["entries"]) == 3
    seq = resp1["data"]["latestSequenceId"]

    # Step 2: Poll with since_sequence_id - no new entries
    resp2 = await read_console(ctx=DummyContext(), action="get", since_sequence_id=seq)
    assert resp2["success"] is True
    assert resp2["data"]["entries"] == []
    # latestSequenceId stays the same when no new entries
    assert resp2["data"]["latestSequenceId"] == seq
