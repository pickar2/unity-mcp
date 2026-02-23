# Unity-MCP Workflow Patterns

Common workflows and patterns for effective Unity-MCP usage.

## Table of Contents

- [Setup & Verification](#setup--verification)
- [Scene Creation Workflows](#scene-creation-workflows)
- [Script Development Workflows](#script-development-workflows)
- [Asset Management Workflows](#asset-management-workflows)
- [Testing Workflows](#testing-workflows)
- [Debugging Workflows](#debugging-workflows)
- [GPU Debugging Workflows](#gpu-debugging-workflows)
- [UI Creation Workflows](#ui-creation-workflows)
- [Batch Operations](#batch-operations)

---

## Setup & Verification

### Initial Connection Verification

```python
# 1. Check editor state
# Read mcpforunity://editor/state

# 2. Verify ready_for_tools == true
# If false, wait for recommended_retry_after_ms

# 3. Check active scene
# Read mcpforunity://editor/state → active_scene

# 4. List available instances (multi-instance)
# Read mcpforunity://instances
```

### Before Any Operation

```python
# Quick readiness check pattern:
editor_state = read_resource("mcpforunity://editor/state")

if not editor_state["ready_for_tools"]:
    # Check blocking_reasons
    # Wait recommended_retry_after_ms
    pass

if editor_state["is_compiling"]:
    # Wait for compilation to complete
    pass
```

---

## Scene Creation Workflows

### Create Complete Scene from Scratch

```python
# 1. Create new scene
manage_scene(action="create", name="GameLevel", path="Assets/Scenes/")

# 2. Batch create environment objects
batch_execute(commands=[
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Ground", "primitive": "Plane",
        "position": [0, 0, 0], "scale": [10, 1, 10]
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Light"
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Player", "primitive": "Capsule",
        "position": [0, 1, 0]
    }}
])

# 3. Add light component
scene_object(action="set", target="Light",
    add_components=["Light"],
    remove_components=["MeshRenderer", "MeshFilter", "BoxCollider"],
    component_properties={"Light": {"type": 1}})  # 1 = Directional

# 4. Set up camera
scene_object(action="set", target="Main Camera", position=[0, 5, -10],
    rotation=[30, 0, 0])

# 5. Verify with screenshot
manage_scene(action="screenshot")

# 6. Save scene
manage_scene(action="save")
```

### Populate Scene with Grid of Objects

```python
# Create 5x5 grid of cubes using batch
commands = []
for x in range(5):
    for z in range(5):
commands.append({
            "tool": "scene_object",
            "params": {
                "action": "create",
                "name": f"Cube_{x}_{z}",
                "primitive": "Cube",
                "position": [x * 2, 0, z * 2]
            }
        })

# Execute in batches of 25
batch_execute(commands=commands[:25], parallel=True)
```

### Clone and Arrange Objects

```python
# Find template object
result = scene_object(action="list", target_regex="Template")
template_path = result["data"]["objects"][0]["path"]

# Duplicate in a line
for i in range(10):
    scene_object(
        action="duplicate",
        target=template_path,
        name=f"Instance_{i}",
        offset=[i * 2, 0, 0]
    )
```

---

## Script Development Workflows

### Create New Script and Attach

Write C# scripts directly to the filesystem. Unity auto-detects changes and compiles.

```python
# 1. Write the script file to disk (use your file writing tool)
# Path: Assets/Scripts/EnemyAI.cs

# 2. CRITICAL: Refresh and compile
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 3. Check for errors
console = read_console(types=["error"], count=10)
if console["entries"]:
    # Handle compilation errors
    print("Compilation errors:", console["entries"])
else:
    # 4. Attach to GameObject
    scene_object(action="set", target="Enemy",
        add_components=["EnemyAI"],
        component_properties={"EnemyAI": {"speed": 10.0}}
    )
```

### Edit Existing Script Safely

```python
# 1. Find the method to edit
matches = find_in_file(
    uri="mcpforunity://path/Assets/Scripts/PlayerController.cs",
    pattern="void Update\\(\\)"
)

# 3. Apply structured edit
script_apply_edits(
    name="PlayerController",
    path="Assets/Scripts",
    edits=[{
        "op": "replace_method",
        "methodName": "Update",
        "replacement": '''void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        transform.Translate(new Vector3(h, 0, v) * speed * Time.deltaTime);
    }'''
    }]
)

# 4. Validate
validate_script(
    uri="mcpforunity://path/Assets/Scripts/PlayerController.cs",
    level="standard"
)

# 5. Refresh
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 6. Check console
read_console(types=["error"], count=10)
```

### Add Method to Existing Class

```python
script_apply_edits(
    name="GameManager",
    path="Assets/Scripts",
    edits=[
        {
            "op": "insert_method",
            "afterMethod": "Start",
            "code": '''
    public void ResetGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }'''
        },
        {
            "op": "anchor_insert",
            "anchor": "using UnityEngine;",
            "position": "after",
            "text": "\nusing UnityEngine.SceneManagement;"
        }
    ]
)
```

---

## Asset Management Workflows

### Create and Apply Material

```python
# 1. Create material
manage_material(
    action="create",
    material_path="Assets/Materials/PlayerMaterial.mat",
    shader="Standard",
    properties={
        "_Color": [0.2, 0.5, 1.0, 1.0],
        "_Metallic": 0.5,
        "_Glossiness": 0.8
    }
)

# 2. Assign to renderer
manage_material(
    action="assign_material_to_renderer",
    target="Player",
    material_path="Assets/Materials/PlayerMaterial.mat",
    slot=0
)

# 3. Verify visually
manage_scene(action="screenshot")
```

### Create Procedural Texture

```python
# 1. Create base texture
manage_texture(
    action="create",
    path="Assets/Textures/Checkerboard.png",
    width=256,
    height=256,
    fill_color=[255, 255, 255, 255]
)

# 2. Apply checkerboard pattern
manage_texture(
    action="apply_pattern",
    path="Assets/Textures/Checkerboard.png",
    pattern="checkerboard",
    palette=[[0, 0, 0, 255], [255, 255, 255, 255]],
    pattern_size=32
)

# 3. Create material with texture
manage_material(
    action="create",
    material_path="Assets/Materials/CheckerMaterial.mat",
    shader="Standard"
)

# 4. Assign texture to material (via manage_material set_material_shader_property)
```

### Organize Assets into Folders

```python
# 1. Create folder structure
batch_execute(commands=[
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Prefabs"}},
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Materials"}},
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Scripts"}},
    {"tool": "manage_asset", "params": {"action": "create_folder", "path": "Assets/Textures"}}
])

# 2. Move existing assets
manage_asset(action="move", path="Assets/MyMaterial.mat", destination="Assets/Materials/MyMaterial.mat")
manage_asset(action="move", path="Assets/MyScript.cs", destination="Assets/Scripts/MyScript.cs")
```

### Search and Process Assets

```python
# Find all prefabs
result = manage_asset(
    action="search",
    path="Assets",
    search_pattern="*.prefab",
    page_size=50,
    generate_preview=False
)

# Process each prefab
for asset in result["assets"]:
    prefab_path = asset["path"]
    # Get prefab info
    info = manage_prefabs(action="get_info", prefab_path=prefab_path)
    print(f"Prefab: {prefab_path}, Children: {info['childCount']}")
```

---

## Testing Workflows

### Run Specific Tests

```python
# 1. List available tests
# Read mcpforunity://tests/EditMode

# 2. Run specific tests
result = run_tests(
    mode="EditMode",
    test_names=["MyTests.TestPlayerMovement", "MyTests.TestEnemySpawn"],
    include_failed_tests=True
)
job_id = result["job_id"]

# 3. Wait for results
final_result = get_test_job(
    job_id=job_id,
    wait_timeout=60,
    include_failed_tests=True
)

# 4. Check results
if final_result["status"] == "complete":
    for test in final_result.get("failed_tests", []):
        print(f"FAILED: {test['name']}: {test['message']}")
```

### Run Tests by Category

```python
# Run all unit tests
result = run_tests(
    mode="EditMode",
    category_names=["Unit"],
    include_failed_tests=True
)

# Poll until complete
while True:
    status = get_test_job(job_id=result["job_id"], wait_timeout=30)
    if status["status"] in ["complete", "failed"]:
        break
```

### Test-Driven Development Pattern

```python
# 1. Write test file to disk (use your file writing tool)
# Path: Assets/Tests/Editor/PlayerTests.cs

# 2. Refresh
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 3. Run test (expect pass for this simple test)
result = run_tests(mode="EditMode", test_names=["PlayerTests.TestPlayerStartsAtOrigin"])
get_test_job(job_id=result["job_id"], wait_timeout=30)
```

---

## Debugging Workflows

### Diagnose Compilation Errors

```python
# 1. Check console for errors
errors = read_console(types=["error"], count=20, include_stacktrace=True)

# 2. For each error, find the file and line
for entry in errors["entries"]:
    # Parse error message for file:line info (e.g., "Assets/Scripts/Foo.cs(10,5): error CS1002")
    # Use find_in_file to locate the problematic code
    pass

# 3. After fixing, refresh and check again
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 4. Poll for new errors since last read
read_console(types=["error"], since_sequence_id=errors["latestSequenceId"])
```

### Investigate Missing References

```python
# 1. Find the GameObject and its components
result = scene_object(action="get", target="Player", components=True)

# 2. Check for null references in serialized fields
# Look for fields with null/missing values in component data

# 3. Find the referenced object
target = scene_object(action="list", target_regex="Target")

# 4. Set the reference
scene_object(
    action="set",
    target="Player",
    component="PlayerController",
    properties={"target": {"instanceID": target["data"]["objects"][0]["instance_id"]}}
)
```

### Check Scene State

```python
# 1. Get hierarchy
hierarchy = manage_scene(action="get_hierarchy", page_size=100, include_transform=True)

# 2. Find objects at unexpected positions
for item in hierarchy["data"]["items"]:
    if item.get("transform", {}).get("position", [0,0,0])[1] < -100:
        print(f"Object {item['name']} fell through floor!")

# 3. Visual verification
manage_scene(action="screenshot")
```

---

## GPU Debugging Workflows

### Inspect Compute Shader Output

```python
# 1. Enter play mode
manage_editor(action="play", recompile=True)

# 2. Discover available buffers
inspect_buffer(target="*/ParticleCompute.*", list_only=True)

# 3. Inspect buffer with structured format
result = inspect_buffer(
    target="ParticleManager/ParticleCompute.positionBuffer",
    count=16,
    format="position:float3@0,velocity:float3@12"
)

# 4. Step frame and inspect again
manage_editor(action="step")
inspect_buffer(
    target="ParticleManager/ParticleCompute.positionBuffer",
    count=16,
    format="position:float3@0,velocity:float3@12"
)
```

### Monitor Console During Play Mode

```python
# 1. Enter play mode
manage_editor(action="play")

# 2. Read initial state
state = read_console(types=["error", "warning", "log"], count=50)
last_seq = state["latestSequenceId"]

# 3. After some gameplay, poll for new entries only
new_entries = read_console(since_sequence_id=last_seq)
```

## UI Creation Workflows

Unity UI (Canvas-based UGUI) requires specific component hierarchies. Use `batch_execute` with `fail_fast=True` to create complete UI elements in a single call.

> **Template warning:** This section is a skill template library, not a guaranteed source of truth. Examples may be inaccurate for your Unity version, package setup, or project conventions.
> **Use safely:**
> 1. Validate component/property names against the current project.
> 2. Prefer targeting by instance ID or full path over generic names.
> 3. Assume complex controls (Slider/Toggle/TMP Input) may need extra reference wiring.
> 4. Treat numeric enum values as placeholders and verify before reuse.

### Create Canvas (Foundation for All UI)

Every UI element must be under a Canvas. A Canvas requires three components: `Canvas`, `CanvasScaler`, and `GraphicRaycaster`.

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "scene_object", "params": {
        "action": "create", "name": "MainCanvas",
        "components": ["Canvas", "CanvasScaler", "GraphicRaycaster"],
        "component_properties": {
            "Canvas": {"renderMode": 0},
            "CanvasScaler": {"uiScaleMode": 1, "referenceResolution": [1920, 1080]}
        }
    }}
])
# renderMode: 0=ScreenSpaceOverlay, 1=ScreenSpaceCamera, 2=WorldSpace
```

### Create EventSystem (Required Once Per Scene for UI Interaction)

If no EventSystem exists in the scene, buttons and other interactive UI elements won't respond to input. Create one alongside your first Canvas.

```python
scene_object(
    action="create", name="EventSystem",
    components=[
        "UnityEngine.EventSystems.EventSystem",
        "UnityEngine.InputSystem.UI.InputSystemUIInputModule"
    ]
)
```

> **Note:** For projects using legacy Input Manager instead of Input System, use `"UnityEngine.EventSystems.StandaloneInputModule"` instead.

### Create Panel (Background Container)

A Panel is an Image component used as a background/container for other UI elements.

```python
scene_object(
    action="create", name="MenuPanel", parent="MainCanvas",
    components=["Image"],
    component_properties={
        "Image": {"color": [0.1, 0.1, 0.1, 0.8]}
    }
)
```

### Create Text (TextMeshPro)

TextMeshProUGUI automatically adds a RectTransform when added to a child of a Canvas.

```python
scene_object(
    action="create", name="TitleText", parent="MenuPanel",
    components=["TextMeshProUGUI"],
    component_properties={
        "TextMeshProUGUI": {
            "text": "My Game Title",
            "fontSize": 48,
            "alignment": 514,
            "color": [1, 1, 1, 1]
        }
    }
)
```

> **TextMeshPro alignment values:** 257=TopLeft, 258=TopCenter, 260=TopRight, 513=MiddleLeft, 514=MiddleCenter, 516=MiddleRight, 1025=BottomLeft, 1026=BottomCenter, 1028=BottomRight.

### Create Button (With Label)

A Button needs an `Image` (visual) + `Button` (interaction) on the parent, and a child with `TextMeshProUGUI` for the label.

```python
batch_execute(fail_fast=True, commands=[
    # Button container with Image + Button components
    {"tool": "scene_object", "params": {
        "action": "create", "name": "StartButton", "parent": "MenuPanel",
        "components": ["Image", "Button"],
        "component_properties": {
            "Image": {"color": [0.2, 0.6, 1.0, 1.0]}
        }
    }},
    # Child text label
    {"tool": "scene_object", "params": {
        "action": "create", "name": "StartButton_Label", "parent": "StartButton",
        "components": ["TextMeshProUGUI"],
        "component_properties": {
            "TextMeshProUGUI": {"text": "Start Game", "fontSize": 24, "alignment": 514}
        }
    }}
])
```

### Create Slider

A Slider requires a specific hierarchy: the slider root, a background, a fill area with fill, and a handle area with handle.

```python
batch_execute(fail_fast=True, commands=[
    # Slider root
    {"tool": "scene_object", "params": {
        "action": "create", "name": "HealthSlider", "parent": "MainCanvas",
        "components": ["Slider", "Image"]
    }},
    # Background
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Background", "parent": "HealthSlider",
        "components": ["Image"],
        "component_properties": {
            "Image": {"color": [0.3, 0.3, 0.3, 1.0]}
        }
    }},
    # Fill Area + Fill
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Fill Area", "parent": "HealthSlider"
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Fill", "parent": "Fill Area",
        "components": ["Image"],
        "component_properties": {
            "Image": {"color": [0.2, 0.8, 0.2, 1.0]}
        }
    }},
    # Handle Area + Handle
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Handle Slide Area", "parent": "HealthSlider"
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Handle", "parent": "Handle Slide Area",
        "components": ["Image"]
    }}
])
```

### Create Input Field (TextMeshPro)

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "scene_object", "params": {
        "action": "create", "name": "NameInput", "parent": "MenuPanel",
        "components": ["Image", "TMP_InputField"]
    }},
    # Text area child
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Text Area", "parent": "NameInput",
        "components": ["RectMask2D"]
    }},
    # Placeholder
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Placeholder", "parent": "Text Area",
        "components": ["TextMeshProUGUI"],
        "component_properties": {
            "TextMeshProUGUI": {"text": "Enter name...", "fontStyle": 2, "color": [0.5, 0.5, 0.5, 0.5]}
        }
    }},
    # Actual text
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Text", "parent": "Text Area",
        "components": ["TextMeshProUGUI"]
    }}
])
```

### Create Toggle (Checkbox)

```python
batch_execute(fail_fast=True, commands=[
    {"tool": "scene_object", "params": {
        "action": "create", "name": "SoundToggle", "parent": "MenuPanel",
        "components": ["Toggle"]
    }},
    # Background box
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Background", "parent": "SoundToggle",
        "components": ["Image"]
    }},
    # Checkmark
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Checkmark", "parent": "Background",
        "components": ["Image"]
    }},
    # Label
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Label", "parent": "SoundToggle",
        "components": ["TextMeshProUGUI"],
        "component_properties": {
            "TextMeshProUGUI": {"text": "Sound Effects", "fontSize": 18, "alignment": 513}
        }
    }}
])
```

### Add Layout Group (Vertical/Horizontal/Grid)

Layout groups auto-arrange child elements. Add to any container.

```python
# Vertical layout for a menu panel
scene_object(
    action="set", target="MenuPanel",
    add_components=["VerticalLayoutGroup", "ContentSizeFitter"],
    component_properties={
        "VerticalLayoutGroup": {
            "spacing": 10,
            "childAlignment": 1,
            "childForceExpandWidth": True,
            "childForceExpandHeight": False
        },
        "ContentSizeFitter": {
            "verticalFit": 2
        }
    }
)
```

> **childAlignment values:** 0=UpperLeft, 1=UpperCenter, 2=UpperRight, 3=MiddleLeft, 4=MiddleCenter, 5=MiddleRight, 6=LowerLeft, 7=LowerCenter, 8=LowerRight.
> **ContentSizeFitter fit modes:** 0=Unconstrained, 1=MinSize, 2=PreferredSize.

### Complete Example: Main Menu Screen

Combines multiple templates into a full menu screen in two batch calls (default 25 command limit per batch, configurable in Unity MCP Tools window up to 100).

```python
# Batch 1: Canvas + EventSystem + Panel + Title
batch_execute(fail_fast=True, commands=[
    # Canvas
    {"tool": "scene_object", "params": {
        "action": "create", "name": "MenuCanvas",
        "components": ["Canvas", "CanvasScaler", "GraphicRaycaster"],
        "component_properties": {
            "Canvas": {"renderMode": 0},
            "CanvasScaler": {"uiScaleMode": 1, "referenceResolution": [1920, 1080]}
        }
    }},
    # EventSystem
    {"tool": "scene_object", "params": {
        "action": "create", "name": "EventSystem",
        "components": ["UnityEngine.EventSystems.EventSystem", "UnityEngine.EventSystems.StandaloneInputModule"]
    }},
    # Panel with layout
    {"tool": "scene_object", "params": {
        "action": "create", "name": "MenuPanel", "parent": "MenuCanvas",
        "components": ["Image", "VerticalLayoutGroup"],
        "component_properties": {
            "Image": {"color": [0.1, 0.1, 0.15, 0.9]},
            "VerticalLayoutGroup": {"spacing": 20, "childAlignment": 4, "childForceExpandWidth": True, "childForceExpandHeight": False}
        }
    }},
    # Title
    {"tool": "scene_object", "params": {
        "action": "create", "name": "Title", "parent": "MenuPanel",
        "components": ["TextMeshProUGUI"],
        "component_properties": {
            "TextMeshProUGUI": {"text": "My Game", "fontSize": 64, "alignment": 514, "color": [1, 1, 1, 1]}
        }
    }}
])

# Batch 2: Buttons
batch_execute(fail_fast=True, commands=[
    # Play Button
    {"tool": "scene_object", "params": {
        "action": "create", "name": "PlayButton", "parent": "MenuPanel",
        "components": ["Image", "Button"],
        "component_properties": {"Image": {"color": [0.2, 0.6, 1.0, 1.0]}}
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "PlayButton_Label", "parent": "PlayButton",
        "components": ["TextMeshProUGUI"],
        "component_properties": {"TextMeshProUGUI": {"text": "Play", "fontSize": 32, "alignment": 514}}
    }},
    # Settings Button
    {"tool": "scene_object", "params": {
        "action": "create", "name": "SettingsButton", "parent": "MenuPanel",
        "components": ["Image", "Button"],
        "component_properties": {"Image": {"color": [0.3, 0.3, 0.35, 1.0]}}
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "SettingsButton_Label", "parent": "SettingsButton",
        "components": ["TextMeshProUGUI"],
        "component_properties": {"TextMeshProUGUI": {"text": "Settings", "fontSize": 32, "alignment": 514}}
    }},
    # Quit Button
    {"tool": "scene_object", "params": {
        "action": "create", "name": "QuitButton", "parent": "MenuPanel",
        "components": ["Image", "Button"],
        "component_properties": {"Image": {"color": [0.8, 0.2, 0.2, 1.0]}}
    }},
    {"tool": "scene_object", "params": {
        "action": "create", "name": "QuitButton_Label", "parent": "QuitButton",
        "components": ["TextMeshProUGUI"],
        "component_properties": {"TextMeshProUGUI": {"text": "Quit", "fontSize": 32, "alignment": 514}}
    }}
])
```

### UI Component Quick Reference

| UI Element | Required Components | Notes |
| ---------- | ------------------- | ----- |
| **Canvas** | Canvas + CanvasScaler + GraphicRaycaster | Root for all UI. One per screen. |
| **EventSystem** | EventSystem + StandaloneInputModule (or InputSystemUIInputModule) | One per scene. Required for interaction. |
| **Panel** | Image | Container. Set color for background. |
| **Text** | TextMeshProUGUI | Auto-adds RectTransform under Canvas. |
| **Button** | Image + Button + child(TextMeshProUGUI) | Image = visual, Button = click handler. |
| **Image** | Image | Set sprite property for custom graphics. |
| **Slider** | Slider + Image + children(Background, Fill Area/Fill, Handle Slide Area/Handle) | Complex hierarchy. |
| **Toggle** | Toggle + children(Background/Checkmark, Label) | Checkbox/radio button. |
| **Input Field** | Image + TMP_InputField + children(Text Area/Placeholder/Text) | Text input. |
| **Scroll View** | ScrollRect + Image + children(Viewport/Content, Scrollbar) | Scrollable container. |
| **Dropdown** | Image + TMP_Dropdown + children(Label, Arrow, Template) | Selection menu. |
| **Layout Group** | VerticalLayoutGroup / HorizontalLayoutGroup / GridLayoutGroup | Add to any container to auto-arrange children. |


---

## Batch Operations

### Mass Property Update

```python
# Batch update all enemies at once using tag selector
scene_object(action="set", tag="Enemy",
    component="EnemyHealth",
    properties={"maxHealth": 100})
```

### Mass Object Creation with Variations

```python
import random

commands = []
for i in range(20):
commands.append({
        "tool": "scene_object",
        "params": {
            "action": "create",
            "name": f"Tree_{i}",
            "primitive": "Capsule",
            "position": [random.uniform(-50, 50), 0, random.uniform(-50, 50)],
            "scale": [1, random.uniform(2, 5), 1]
        }
    })

batch_execute(commands=commands, parallel=True)
```

### Cleanup Pattern

```python
# Delete all temporary objects using regex (no need to find first)
scene_object(action="delete", target_regex="Temp_.*")
```

---

## Error Recovery Patterns

### Stale File Recovery

```python
# If script_apply_edits fails with "stale_file", the file changed since last read.
# Re-read the file, re-apply your edits, and retry.
script_apply_edits(
    name="MyScript",
    path="Assets/Scripts",
    edits=[{"op": "replace_method", "methodName": "Update", "replacement": "void Update() { }"}]
)
```

### Domain Reload Recovery

```python
# After domain reload, connection may be lost
# Wait and retry pattern:
import time

max_retries = 5
for attempt in range(max_retries):
    try:
        editor_state = read_resource("mcpforunity://editor/state")
        if editor_state["ready_for_tools"]:
            break
    except:
        time.sleep(2 ** attempt)  # Exponential backoff
```

### Compilation Block Recovery

```python
# If tools fail due to compilation:
# 1. Check console for errors
errors = read_console(types=["error"], count=20)

# 2. Fix the script errors
# ... edit scripts ...

# 3. Force refresh
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)

# 4. Verify clean console
errors = read_console(types=["error"], count=5)
if not errors["entries"]:
    # Safe to proceed with tools
    pass
```

### Play Mode Awareness

```python
# Preferred: use manage_editor with recompile for compile + error check + play
manage_editor(action="play", recompile=True)
# Returns error if compilation fails, otherwise enters play mode

# Write operations (scripts, shaders, scenes) are blocked in play mode.
# Exit play mode first:
manage_editor(action="stop")

# Then proceed with modifications
manage_script(action="update", name="MyScript", contents="...")
refresh_unity(compile="request", wait_for_ready=True)

# Re-enter play mode
manage_editor(action="play", recompile=True)
```
