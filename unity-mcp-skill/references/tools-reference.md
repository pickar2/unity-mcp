# Unity-MCP Tools Reference

Complete reference for all MCP tools. Each tool includes parameters, types, and usage examples.

## Table of Contents

- [Infrastructure Tools](#infrastructure-tools)
- [Scene Tools](#scene-tools)
- [GameObject Tools](#gameobject-tools)
- [Script Tools](#script-tools)
- [Asset Tools](#asset-tools)
- [Material & Shader Tools](#material--shader-tools)
- [Shader, ScriptableObject & VFX Tools](#shader-scriptableobject--vfx-tools)
- [Editor Control Tools](#editor-control-tools)
- [Testing Tools](#testing-tools)
- [GPU Debugging Tools](#gpu-debugging-tools)
- [Play Mode Restrictions](#play-mode-restrictions)

---

## Infrastructure Tools

### batch_execute

Execute multiple MCP commands in a single batch (10-100x faster).

```python
batch_execute(
    commands=[                    # list[dict], required, max 25
        {"tool": "tool_name", "params": {...}},
        ...
    ],
    parallel=False,              # bool, optional - run read-only ops in parallel
    fail_fast=False,             # bool, optional - stop on first failure
    max_parallelism=None         # int, optional - max parallel workers
)
```

### set_active_instance

Route commands to a specific Unity instance (multi-instance workflows).

```python
set_active_instance(
    instance="ProjectName@abc123"  # str, required - Name@hash or hash prefix
)
```

### refresh_unity

Refresh asset database and trigger script compilation.

```python
refresh_unity(
    mode="if_dirty",             # "if_dirty" | "force"
    scope="all",                 # "assets" | "scripts" | "all"
    compile="none",              # "none" | "request"
    wait_for_ready=True          # bool - wait until editor ready
)
```

**Play mode note:** `compile="request"` is blocked in play mode (would trigger domain reload and exit play mode).

---

## Scene Tools

### manage_scene

Scene CRUD operations and hierarchy queries.

```python
# Get hierarchy (paginated)
manage_scene(
    action="get_hierarchy",
    page_size=50,                # int, default 50, max 500
    cursor=0,                    # int, pagination cursor
    parent=None,                 # str|int, optional - filter by parent
    include_transform=False      # bool - include local transforms
)

# Screenshot
manage_scene(action="screenshot")  # Returns base64 PNG

# Other actions
manage_scene(action="get_active")        # Current scene info
manage_scene(action="get_build_settings") # Build settings
manage_scene(action="create", name="NewScene", path="Assets/Scenes/")  # blocked in play mode
manage_scene(action="load", path="Assets/Scenes/Main.unity")           # blocked in play mode
manage_scene(action="save")                                             # blocked in play mode
```

---

## Scene Object Tool

### scene_object

Unified tool for all GameObject interactions — list, get, set, create, delete, duplicate, move.

```python
# List objects with filtering
scene_object(action="list", tag="Enemy", depth=0, page_size=50)
scene_object(action="list", component="Rigidbody", layer="Water")
scene_object(action="list", parent="Canvas", target_regex=".*Button")

# Get object details
scene_object(action="get", target="Player")                    # by name
scene_object(action="get", target="Canvas/Panel/Button")       # by path
scene_object(action="get", target=12345, components=True)      # by instance ID, with component data

# Create
scene_object(
    action="create",
    name="MyCube",
    primitive="Cube",            # "Cube"|"Sphere"|"Capsule"|"Cylinder"|"Plane"|"Quad"
    position=[0, 1, 0],         # local coordinates
    rotation=[0, 45, 0],
    scale=[1, 1, 1],
    components=["Rigidbody", "BoxCollider"],
    tag="Player",               # auto-creates missing tags
    parent="Environment"
)

# Set properties (single object)
scene_object(
    action="set",
    target="Player",             # name, path, or instance ID
    position=[10, 0, 0],
    active=True,
    tag="Player",
    layer="Player",
    add_components=["AudioSource"],
    remove_components=["OldComponent"],
    component_properties={
        "Rigidbody": {"mass": 10.0, "useGravity": True}
    }
)

# Set properties (batch)
scene_object(action="set", target_regex=".*Enemy", active=False)
scene_object(action="set", tag="Temp", active=False)

# Delete
scene_object(action="delete", target="OldObject")
scene_object(action="delete", target_regex=".*Temp.*")         # batch delete

# Duplicate
scene_object(
    action="duplicate",
    target="Player",
    name="Player2",
    offset=[5, 0, 0]            # position offset from original
)

# Move relative
scene_object(
    action="move_relative",
    target="Player",
    reference="Enemy",
    direction="left",            # "left"|"right"|"up"|"down"|"forward"|"back"
    distance=5.0,
    world_space=True
)
```

---

## Script Tools

### script_apply_edits

Apply structured edits to C# scripts (safer than raw text edits). This is the recommended tool for script modifications.

```python
script_apply_edits(
    name="MyScript",             # script name (no .cs)
    path="Assets/Scripts",       # folder path
    edits=[
        # Replace entire method
        {
            "op": "replace_method",
            "methodName": "Update",
            "replacement": "void Update() { transform.Rotate(Vector3.up); }"
        },
        # Insert new method
        {
            "op": "insert_method",
            "afterMethod": "Start",
            "code": "void OnEnable() { Debug.Log(\"Enabled\"); }"
        },
        # Delete method
        {
            "op": "delete_method",
            "methodName": "OldMethod"
        },
        # Anchor-based insert
        {
            "op": "anchor_insert",
            "anchor": "void Start()",
            "position": "before",  # "before" | "after"
            "text": "// Called before Start\n"
        },
        # Regex replace
        {
            "op": "regex_replace",
            "pattern": "Debug\\.Log\\(",
            "text": "Debug.LogWarning("
        },
        # Prepend/append to file
        {"op": "prepend", "text": "// File header\n"},
        {"op": "append", "text": "\n// File footer"}
    ]
)
```

### validate_script

Check script for syntax/semantic errors.

```python
validate_script(
    uri="mcpforunity://path/Assets/Scripts/MyScript.cs",
    level="basic",               # "basic" | "standard"
    include_diagnostics=True     # include full error details
)
```

**Note:** For creating or deleting scripts, use standard file operations directly. Unity will auto-detect changes and compile.

---

## Asset Tools

### manage_asset

Asset operations: search, import, create, modify, delete.

```python
# Search assets (paginated)
manage_asset(
    action="search",
    path="Assets",               # search scope
    search_pattern="*.prefab",   # glob or "t:MonoScript" filter
    filter_type="Prefab",        # optional type filter
    page_size=25,                # keep small to avoid large payloads
    page_number=1,               # 1-based
    generate_preview=False       # avoid base64 bloat
)

# Get asset info
manage_asset(action="get_info", path="Assets/Prefabs/Player.prefab")

# Create asset
manage_asset(
    action="create",
    path="Assets/Materials/NewMaterial.mat",
    asset_type="Material",
    properties={"color": [1, 0, 0, 1]}
)

# Duplicate/move/rename
manage_asset(action="duplicate", path="Assets/A.prefab", destination="Assets/B.prefab")
manage_asset(action="move", path="Assets/A.prefab", destination="Assets/Prefabs/A.prefab")
manage_asset(action="rename", path="Assets/A.prefab", destination="Assets/B.prefab")

# Create folder
manage_asset(action="create_folder", path="Assets/NewFolder")

# Delete
manage_asset(action="delete", path="Assets/OldAsset.asset")
```

### manage_prefabs

Headless prefab operations.

```python
# Get prefab info
manage_prefabs(action="get_info", prefab_path="Assets/Prefabs/Player.prefab")

# Get prefab hierarchy
manage_prefabs(action="get_hierarchy", prefab_path="Assets/Prefabs/Player.prefab")

# Create prefab from scene GameObject
manage_prefabs(
    action="create_from_gameobject",
    target="Player",             # GameObject in scene
    prefab_path="Assets/Prefabs/Player.prefab",
    allow_overwrite=False
)

# Modify prefab contents (headless)
manage_prefabs(
    action="modify_contents",
    prefab_path="Assets/Prefabs/Player.prefab",
    target="ChildObject",        # object within prefab
    position=[0, 1, 0],
    components_to_add=["AudioSource"]
)
```

---

## Material & Shader Tools

### manage_material

Create and modify materials.

```python
# Create material
manage_material(
    action="create",
    material_path="Assets/Materials/Red.mat",
    shader="Standard",
    properties={"_Color": [1, 0, 0, 1]}
)

# Get material info
manage_material(action="get_material_info", material_path="Assets/Materials/Red.mat")

# Set shader property
manage_material(
    action="set_material_shader_property",
    material_path="Assets/Materials/Red.mat",
    property="_Metallic",
    value=0.8
)

# Set color
manage_material(
    action="set_material_color",
    material_path="Assets/Materials/Red.mat",
    property="_BaseColor",
    color=[0, 1, 0, 1]           # RGBA
)

# Assign to renderer
manage_material(
    action="assign_material_to_renderer",
    target="MyCube",
    material_path="Assets/Materials/Red.mat",
    slot=0                       # material slot index
)

# Set renderer color directly
manage_material(
    action="set_renderer_color",
    target="MyCube",
    color=[1, 0, 0, 1],
    mode="instance"              # "shared"|"instance"|"property_block"
)
```

### manage_texture

Create procedural textures.

```python
manage_texture(
    action="create",
    path="Assets/Textures/Checker.png",
    width=64,
    height=64,
    fill_color=[255, 255, 255, 255]  # or [1.0, 1.0, 1.0, 1.0]
)

# Apply pattern
manage_texture(
    action="apply_pattern",
    path="Assets/Textures/Checker.png",
    pattern="checkerboard",      # "checkerboard"|"stripes"|"dots"|"grid"|"brick"
    palette=[[0,0,0,255], [255,255,255,255]],
    pattern_size=8
)

# Apply gradient
manage_texture(
    action="apply_gradient",
    path="Assets/Textures/Gradient.png",
    gradient_type="linear",      # "linear"|"radial"
    gradient_angle=45,
    palette=[[255,0,0,255], [0,0,255,255]]
)
```

## Shader, ScriptableObject & VFX Tools

### manage_shader

CRUD operations for shader scripts. Write actions **blocked in play mode**.

```python
manage_shader(
    action="create",             # "create"|"read"|"update"|"delete"
    name="MyShader",             # shader name (no extension)
    path="Assets/Shaders",      # asset folder path
    contents="Shader \"Custom/MyShader\" { ... }"  # shader code (create/update)
)

# Read shader source
manage_shader(action="read", name="MyShader", path="Assets/Shaders")
```

### manage_scriptable_object

Create and modify ScriptableObject assets.

```python
# Create
manage_scriptable_object(
    action="create",
    type_name="MyNamespace.GameConfig",  # namespace-qualified type
    folder_path="Assets/Data",
    asset_name="DefaultConfig",
    overwrite=False,
    patches=[                    # property patches to apply
        {"propertyPath": "maxHealth", "value": 100},
        {"propertyPath": "speed", "value": 5.0}
    ]
)

# Modify existing
manage_scriptable_object(
    action="modify",
    target={"path": "Assets/Data/DefaultConfig.asset"},  # or {"guid": "..."}
    patches=[{"propertyPath": "maxHealth", "value": 200}],
    dry_run=False                # validate without applying
)
```

### manage_vfx

Manage VFX components: ParticleSystem, VisualEffect, LineRenderer, TrailRenderer.

```python
manage_vfx(
    action="particle_play",      # prefix: particle_*, vfx_*, line_*, trail_*
    target="MyParticles",        # GameObject name/path/id
    search_method="by_name",     # "by_name"|"by_path"|"by_id"|"by_tag"|"by_layer"
    properties={...}             # action-specific parameters
)

# Common actions:
# particle_play, particle_stop, particle_pause, particle_restart, particle_clear
# particle_read, particle_write, particle_enable_module, particle_add_burst
# vfx_play, vfx_stop, vfx_pause, vfx_reinit, vfx_read, vfx_set_*
# line_read, line_write
# trail_read, trail_write
```

---

## Editor Control Tools

### manage_editor

Control Unity Editor state.

```python
# Play mode control
manage_editor(action="play")                          # Enter play mode
manage_editor(action="play", recompile=True)           # Compile, check errors, then play
manage_editor(action="play", paused=True)              # Enter play mode paused
manage_editor(action="pause")                          # Pause play mode
manage_editor(action="stop")                           # Exit play mode
manage_editor(action="step")                           # Single frame step (while paused)

# Editor tools
manage_editor(action="set_active_tool", tool_name="Move")  # Move/Rotate/Scale/etc.

# Tags and layers
manage_editor(action="add_tag", tag_name="Enemy")
manage_editor(action="remove_tag", tag_name="OldTag")
manage_editor(action="add_layer", layer_name="Projectiles")
manage_editor(action="remove_layer", layer_name="OldLayer")
```

**Play mode notes:**
- `recompile=true` while already in play mode returns an error (can't recompile during play)
- Returns error if `scriptCompilationFailed` and trying to enter play mode

### execute_menu_item

Execute any Unity menu item.

```python
execute_menu_item(menu_path="File/Save Project")
execute_menu_item(menu_path="GameObject/3D Object/Cube")
execute_menu_item(menu_path="Window/General/Console")
```

### read_console

Read or clear Unity console messages. Uses a ring buffer with sequence IDs for efficient polling.

```python
# Get recent messages
read_console(
    action="get",                # "get" | "clear"
    types=["error", "warning", "log"],  # or ["all"] - default: all three
    count=100,                   # max messages (default 100, ignored with paging)
    since_sequence_id=2790,      # only entries after this ID (for polling)
    # after_sequence_id=2790,    # alias for since_sequence_id
    since_timestamp="2024-01-01T00:00:00Z",  # optional time filter
    filter_text="NullReference", # substring filter (case-insensitive)
    filter_regex="CS\\d{4}",     # regex filter (mutually exclusive with filter_text)
    page_size=50,
    cursor=0,
    count_only=False,            # return only counts by type, no entries
    include_stacktrace=False
)
# Returns: {entries: [...], latestSequenceId: 2795, totalMatched: 5, ...}

# Poll for new entries since last read
read_console(since_sequence_id=2795, types=["error"])

# Clear console
read_console(action="clear")
```

---

## Testing Tools

### run_tests

Start async test execution. **Blocked in play mode** - exit play mode first.

```python
result = run_tests(
    mode="EditMode",             # "EditMode"|"PlayMode"
    recompile=False,             # bool - trigger recompilation before running
    test_names=["MyTests.TestA", "MyTests.TestB"],  # specific tests
    group_names=["Integration*"],  # regex patterns
    category_names=["Unit"],     # NUnit categories
    assembly_names=["Tests"],    # assembly filter
    include_failed_tests=True,   # include failure details
    include_details=False        # include all test details
)
# Returns: {"job_id": "abc123", ...}
```

### get_test_job

Poll test job status.

```python
result = get_test_job(
    job_id="abc123",
    wait_timeout=60,             # wait up to N seconds
    include_failed_tests=True,
    include_details=False
)
# Returns: {"status": "complete"|"running"|"failed", "results": {...}}
```

---

## GPU Debugging Tools

### inspect_buffer

Inspect ComputeBuffer/GraphicsBuffer contents via reflection. **Requires play mode.**

```python
# Discovery: list all buffers on matching components
inspect_buffer(
    target="*/ParticleCompute.*",    # wildcard discovery
    list_only=True
)

# Read buffer data with structured format
inspect_buffer(
    target="ParticleManager/ParticleCompute.positionBuffer",  # GameObject/Component.field
    # target="instanceId:12345/ParticleCompute.positionBuffer",  # by instance ID
    start=0,                     # start element (0-based, default 0)
    count=8,                     # elements to read (default 8)
    format="position:float3@0,velocity:float3@16,mass:float@32"
    # Format: "name:type@byteOffset,..." - supported types: float, float2, float3,
    #   float4, int, int2, int3, int4, uint, half. Omit for raw base64.
)
```

---

## Search Tools

### find_in_file

Search file contents with regex.

```python
find_in_file(
    uri="mcpforunity://path/Assets/Scripts/MyScript.cs",
    pattern="public void \\w+",  # regex pattern
    max_results=200,
    ignore_case=True
)
# Returns: line numbers, content excerpts, match positions
```

---

## Custom Tools

### execute_custom_tool

Execute project-specific custom tools.

```python
execute_custom_tool(
    tool_name="my_custom_tool",
    parameters={"param1": "value", "param2": 42}
)
```

Discover available custom tools via `mcpforunity://custom-tools` resource.

---

## Play Mode Restrictions

Some tools are blocked or behave differently during play mode:

| Tool | Restriction |
|------|-------------|
| `run_tests` | Blocked entirely (except `clear_stuck`) |
| `script_apply_edits` | Blocked (would trigger compilation and exit play mode) |
| `manage_shader` | Write actions blocked (create/update/delete) |
| `manage_scene` | create/load/save blocked |
| `refresh_unity` | `compile="request"` blocked |
| `manage_editor` | `recompile=true` blocked while already playing |
| `inspect_buffer` | **Requires** play mode (blocked outside play mode) |

Read-only operations (get_hierarchy, scene_object list/get, read_console, resources, etc.) work in both modes.
