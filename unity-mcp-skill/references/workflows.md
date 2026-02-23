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

```python
# 1. Create script
create_script(
    path="Assets/Scripts/EnemyAI.cs",
    contents='''using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public float speed = 5f;
    public Transform target;
    
    void Update()
    {
        if (target != null)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            transform.position += direction * speed * Time.deltaTime;
        }
    }
}'''
)

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
# 1. Get current SHA
sha_info = get_sha(uri="mcpforunity://path/Assets/Scripts/PlayerController.cs")

# 2. Find the method to edit
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
# 1. Write test first
create_script(
    path="Assets/Tests/Editor/PlayerTests.cs",
    contents='''using NUnit.Framework;
using UnityEngine;

public class PlayerTests
{
    [Test]
    public void TestPlayerStartsAtOrigin()
    {
        var player = new GameObject("TestPlayer");
        Assert.AreEqual(Vector3.zero, player.transform.position);
        Object.DestroyImmediate(player);
    }
}'''
)

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
try:
    apply_text_edits(uri=script_uri, edits=[...], precondition_sha256=old_sha)
except Exception as e:
    if "stale_file" in str(e):
        # Re-fetch SHA
        new_sha = get_sha(uri=script_uri)
        # Retry with new SHA
        apply_text_edits(uri=script_uri, edits=[...], precondition_sha256=new_sha["sha256"])
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
