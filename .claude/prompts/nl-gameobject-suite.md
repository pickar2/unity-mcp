# Unity Scene Object API Test Suite

You are running inside CI for the `unity-mcp` repo. Use only the tools allowed by the workflow. Work autonomously; do not prompt the user. Do NOT spawn subagents.

**Print this once, verbatim, early in the run:**
AllowedTools: Write,mcp__UnityMCP__manage_editor,mcp__UnityMCP__scene_object,mcp__UnityMCP__manage_scene,mcp__UnityMCP__read_console

---

## Mission
1) Test the unified scene_object tool for all GameObject interactions
2) Execute GO tests GO-0..GO-10 in order
3) **Report**: write one `<testcase>` XML fragment per test to `reports/<TESTID>_results.xml`

**CRITICAL XML FORMAT REQUIREMENTS:**
- Each file must contain EXACTLY one `<testcase>` root element
- NO prologue, epilogue, code fences, or extra characters
- Use this exact shape:

<testcase name="GO-0 — List Objects" classname="UnityMCP.GO-T">
  <system-out><![CDATA[
(evidence of what was accomplished)
  ]]></system-out>
</testcase>

- If test fails, include: `<failure message="reason"/>`
- TESTID must be one of: GO-0, GO-1, GO-2, GO-3, GO-4, GO-5, GO-6, GO-7, GO-8, GO-9, GO-10

---

## Test Specs

### GO-0. List Objects
**Goal**: Verify scene_object list action returns paginated results with object metadata
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="list", page_size=10)`
- Verify response includes `objects` array with `path`, `name`, `instance_id`, `component_types`
- Verify pagination info (`total`, `page_size`, `has_more`)
- **Pass criteria**: Returns at least one object with non-empty component_types

### GO-1. List with Filters
**Goal**: Test filtering by component and tag
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="list", component="Camera")`
- Verify response contains at least one object with Camera component
- **Pass criteria**: Filtered list returns matching objects

### GO-2. Get Object Details
**Goal**: Test getting full object data
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="get", target="Main Camera", components=true)`
- Verify response includes: path, name, instance_id, transform, tag, layer, components
- Verify component data includes Camera, Transform, AudioListener
- **Pass criteria**: All expected fields present with component data

### GO-3. Create Object
**Goal**: Test creating a primitive GameObject
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="create", name="GO_Test_Object", primitive="Cube", position=[0, 1, 0])`
- Verify response includes path and instance_id
- Call `mcp__UnityMCP__scene_object(action="get", target="GO_Test_Object")` to verify it exists
- **Pass criteria**: Object created at correct position with MeshRenderer component

### GO-4. Set Properties and Add Components
**Goal**: Test modifying object properties and adding components
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="set", target="GO_Test_Object", add_components=["Rigidbody"], tag="TestTag", position=[5, 2, 0])`
- Verify changes list includes "add_component:Rigidbody", "tag", "position"
- Call `mcp__UnityMCP__scene_object(action="get", target="GO_Test_Object", components=true)` to verify state
- **Pass criteria**: Component added, tag set, position updated

### GO-5. Set Component Properties
**Goal**: Test setting properties on a component
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="set", target="GO_Test_Object", component="Rigidbody", properties={"mass": 5.0, "useGravity": false})`
- Verify the component properties were set
- **Pass criteria**: Rigidbody mass is 5.0 and useGravity is false

### GO-6. List by Tag
**Goal**: Test listing objects filtered by tag
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="list", tag="TestTag")`
- Verify response contains GO_Test_Object
- **Pass criteria**: Returns at least one object with tag "TestTag"

### GO-7. Duplicate Object
**Goal**: Test duplicating a GameObject
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="duplicate", target="GO_Test_Object", name="GO_Test_Copy", offset=[3, 0, 0])`
- Verify response includes source and duplicate info
- Call `mcp__UnityMCP__scene_object(action="get", target="GO_Test_Copy")` to verify it exists
- **Pass criteria**: Duplicate created with correct name and offset position

### GO-8. Remove Component
**Goal**: Test removing a component
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="set", target="GO_Test_Object", remove_components=["Rigidbody"])`
- Verify changes list includes "remove_component:Rigidbody"
- **Pass criteria**: Component successfully removed

### GO-9. List with Pagination
**Goal**: Test pagination
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="list", page_size=2)`
- If has_more is true, call again with cursor from next_cursor
- Verify second page returns different objects
- **Pass criteria**: Pagination works (next_cursor present when more results available)

### GO-10. Delete and Cleanup
**Goal**: Test delete action
**Actions**:
- Call `mcp__UnityMCP__scene_object(action="delete", target="GO_Test_Copy")`
- Call `mcp__UnityMCP__scene_object(action="delete", target="GO_Test_Object")`
- Verify both return success
- **Pass criteria**: Both objects successfully deleted

---

## Tool Reference

### scene_object
Unified tool for all GameObject interactions:
- `scene_object(action="list", tag?, component?, layer?, parent?, target_regex?, depth?, page_size?, cursor?)` - Find objects
- `scene_object(action="get", target, components?)` - Read object data
- `scene_object(action="set", target, ...)` - Modify properties, add/remove components
- `scene_object(action="create", name, primitive?, position?, ...)` - Create GameObjects
- `scene_object(action="delete", target)` - Delete single object
- `scene_object(action="delete", target_regex?)` - Batch delete
- `scene_object(action="duplicate", target, name?, offset?)` - Clone objects
- `scene_object(action="move_relative", target, reference, direction?, offset?)` - Relative positioning

### Resources
- `mcpforunity://scene/gameobject/{instanceID}` - Single GameObject data
- `mcpforunity://scene/gameobject/{instanceID}/components` - All components (paginated)
- `mcpforunity://scene/gameobject/{instanceID}/component/{componentName}` - Single component

---

## Transcript Minimization Rules
- Do not restate tool JSON; summarize in ≤ 2 short lines
- Per-test `system-out` ≤ 400 chars
- Console evidence: include ≤ 3 lines in the fragment

---
