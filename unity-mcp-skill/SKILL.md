---
name: unity-mcp-orchestrator
description: Orchestrate Unity Editor via MCP (Model Context Protocol) tools and resources. Use when working with Unity projects through MCP for Unity - creating/modifying GameObjects, editing scripts, managing scenes, running tests, or any Unity Editor automation. Provides best practices, tool schemas, and workflow patterns for effective Unity-MCP integration.
---

# Unity-MCP Operator Guide

This skill helps you effectively use the Unity Editor with MCP tools and resources.

## Quick Start: Resource-First Workflow

**Always read relevant resources before using tools.** This prevents errors and provides the necessary context.

```
1. Check editor state     → mcpforunity://editor/state
2. Understand the scene   → mcpforunity://scene/gameobject-api
3. Find what you need     → scene_object(action="list") or resources
4. Take action            → tools (scene_object, script_apply_edits, validate_script, manage_prefabs, etc.)
5. Verify results         → read_console, capture_screenshot (in manage_scene), resources
```

## Critical Best Practices

### 1. After Writing/Editing Scripts: Always Refresh and Check Console

```python
# After script_apply_edits or writing scripts to disk:
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
read_console(types=["error"], count=10, include_stacktrace=True)
```

**Why:** Unity must compile scripts before they're usable. Compilation errors block all tool execution.

### 2. Use `batch_execute` for Multiple Operations

```python
# 10-100x faster than sequential calls
batch_execute(
    commands=[
        {"tool": "scene_object", "params": {"action": "create", "name": "Cube1", "primitive": "Cube"}},
        {"tool": "scene_object", "params": {"action": "create", "name": "Cube2", "primitive": "Cube"}},
        {"tool": "scene_object", "params": {"action": "create", "name": "Cube3", "primitive": "Cube"}}
    ],
    parallel=True  # Read-only operations can run in parallel
)
```

**Max 25 commands per batch.** Use `fail_fast=True` for dependent operations.

### 3. Use `screenshot` in manage_scene to Verify Visual Results

```python
# Via manage_scene
manage_scene(action="screenshot")  # Returns base64 image

# After creating/modifying objects, verify visually:
# 1. Create objects
# 2. capture screenshot
# 3. Analyze if result matches intent
```

### 4. Check Console After Major Changes

```python
read_console(
    action="get",
    types=["error", "warning"],  # Focus on problems
    count=10,
    format="detailed"
)
```

### 5. Always Check `editor_state` Before Complex Operations

```python
# Read mcpforunity://editor/state to check:
# - is_compiling: Wait if true
# - is_domain_reload_pending: Wait if true  
# - ready_for_tools: Only proceed if true
# - blocking_reasons: Why tools might fail
```

## Parameter Type Conventions

### Vectors (position, rotation, scale, color)
```python
# Both forms accepted:
position=[1.0, 2.0, 3.0]        # List
position="[1.0, 2.0, 3.0]"     # JSON string
```

### Booleans
```python
# Both forms accepted:
include_inactive=True           # Boolean
include_inactive="true"         # String
```

### Colors
```python
# Auto-detected format:
color=[255, 0, 0, 255]         # 0-255 range
color=[1.0, 0.0, 0.0, 1.0]    # 0.0-1.0 normalized (auto-converted)
```

### Paths
```python
# Assets-relative (default):
path="Assets/Scripts/MyScript.cs"

# URI forms:
uri="mcpforunity://path/Assets/Scripts/MyScript.cs"
uri="file:///full/path/to/file.cs"
```

## Core Tool Categories

| Category | Key Tools | Use For |
|----------|-----------|---------|
| **Scene** | `manage_scene`, `scene_object` | Scene operations, finding objects |
| **Objects** | `scene_object` | Creating/modifying/deleting GameObjects |
| **Scripts** | `script_apply_edits`, `validate_script`, `refresh_unity` | C# code management |
| **Assets** | `manage_asset`, `manage_prefabs` | Asset operations |
| **Editor** | `manage_editor`, `execute_menu_item`, `read_console` | Editor control |
| **Testing** | `run_tests`, `get_test_job` | Unity Test Framework |
| **Batch** | `batch_execute` | Parallel/bulk operations |

## Common Workflows

### Creating a New Script and Using It

Write C# scripts directly to the filesystem. Unity auto-detects changes and compiles.

```python
# 1. Write the script file to disk (use your file writing tool)
# Path: Assets/Scripts/PlayerController.cs

# 2. CRITICAL: Refresh and wait for compilation
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 3. Check for compilation errors
read_console(types=["error"], count=10)

# 4. Only then attach to GameObject
scene_object(action="set", target="Player", add_components=["PlayerController"])
```

### Finding and Modifying GameObjects

```python
# 1. Find by tag (returns paths, names, IDs)
result = scene_object(action="list", tag="Enemy", page_size=50)

# 2. Get full data for a specific object
scene_object(action="get", target="Enemy", components=True)

# 3. Modify using name, path, or instance ID
scene_object(action="set", target="Enemy", position=[10, 0, 0])
```

### Running and Monitoring Tests

```python
# 1. Start test run (async)
result = run_tests(mode="EditMode", test_names=["MyTests.TestSomething"])
job_id = result["job_id"]

# 2. Poll for completion
result = get_test_job(job_id=job_id, wait_timeout=60, include_failed_tests=True)
```

## Pagination Pattern

Large queries return paginated results. Always follow `next_cursor`:

```python
cursor = 0
all_items = []
while True:
    result = manage_scene(action="get_hierarchy", page_size=50, cursor=cursor)
    all_items.extend(result["data"]["items"])
    if not result["data"].get("next_cursor"):
        break
    cursor = result["data"]["next_cursor"]
```

## Multi-Instance Workflow

When multiple Unity Editors are running:

```python
# 1. List instances via resource: mcpforunity://instances
# 2. Set active instance
set_active_instance(instance="MyProject@abc123")
# 3. All subsequent calls route to that instance
```

## Error Recovery

| Symptom | Cause | Solution |
|---------|-------|----------|
| Tools return "busy" | Compilation in progress | Wait, check `editor_state` |
| "stale_file" error | File changed during edit | Re-read file and retry |
| Connection lost | Domain reload | Wait ~5s, reconnect |
| Commands fail silently | Wrong instance | Check `set_active_instance` |

## Reference Files

For detailed schemas and examples:

- **[tools-reference.md](references/tools-reference.md)**: Complete tool documentation with all parameters
- **[resources-reference.md](references/resources-reference.md)**: All available resources and their data
- **[workflows.md](references/workflows.md)**: Extended workflow examples and patterns
