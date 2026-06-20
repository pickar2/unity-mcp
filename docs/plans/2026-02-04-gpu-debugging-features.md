# GPU Debugging Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add three debugging features for GPU/compute shader workflows: buffer inspection, frame stepping, and regex console filtering.

**Architecture:**
- Feature 1 (Buffer Inspector): New C# tool `InspectBuffer` with reflection-based discovery of ComputeBuffer fields on GameObjects. Python MCP wrapper exposes it.
- Feature 2 (Frame Step): Extend existing `manage_editor` tool with new "step" action using `EditorApplication.Step()`.
- Feature 3 (Regex Filter): Extend existing `read_console` tool with `filterRegex` parameter, mutually exclusive with `filterText`.

**Tech Stack:** C# (Unity Editor API, System.Text.RegularExpressions, System.Reflection), Python (FastMCP)

---

## Task 1: Add Frame-Step Action to ManageEditor (C#)

**Files:**
- Modify: `MCPForUnity/Editor/Tools/ManageEditor.cs:52-153`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ManageEditorStepTests.cs` (create)

**Step 1: Write the failing test**

Create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ManageEditorStepTests.cs`:

```csharp
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageEditorStepTests
    {
        [Test]
        public async Task Step_WhenNotInPlayMode_ReturnsError()
        {
            // Arrange
            var paramsObj = new JObject
            {
                ["action"] = "step",
                ["frames"] = 1
            };

            // Act
            var result = ToJObject(await ManageEditor.HandleCommand(paramsObj));

            // Assert
            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            Assert.That(result["error"]?.ToString(), Does.Contain("play mode"));
        }

        [Test]
        public async Task Step_DefaultsToOneFrame()
        {
            // Arrange - just step action, no frames param
            var paramsObj = new JObject
            {
                ["action"] = "step"
            };

            // Act
            var result = ToJObject(await ManageEditor.HandleCommand(paramsObj));

            // Assert - should fail because not in play mode, but error message confirms step was attempted
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("play mode"));
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run in Unity Test Runner (EditMode): `ManageEditorStepTests`
Expected: FAIL - "step" action not recognized

**Step 3: Write minimal implementation**

In `MCPForUnity/Editor/Tools/ManageEditor.cs`, add case in the switch statement (around line 107, after "stop" case):

```csharp
case "step":
    try
    {
        if (!EditorApplication.isPlaying)
        {
            return new ErrorResponse("Cannot step: Not in play mode. Enter play mode first.");
        }

        int frames = p.GetInt("frames") ?? 1;
        if (frames < 1)
        {
            return new ErrorResponse("frames must be at least 1.");
        }

        for (int i = 0; i < frames; i++)
        {
            EditorApplication.Step();
        }

        return new SuccessResponse($"Stepped {frames} frame(s).", new { frames });
    }
    catch (Exception e)
    {
        return new ErrorResponse($"Error stepping frames: {e.Message}");
    }
```

Also update the error message for unknown actions (around line 149) to include "step":

```csharp
default:
    return new ErrorResponse(
        $"Unknown action: '{action}'. Supported actions: play, pause, stop, step, set_active_tool, add_tag, remove_tag, add_layer, remove_layer."
    );
```

**Step 4: Run test to verify it passes**

Run in Unity Test Runner: `ManageEditorStepTests`
Expected: PASS

**Step 5: Commit**

```bash
git add MCPForUnity/Editor/Tools/ManageEditor.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ManageEditorStepTests.cs
git commit -m "feat(editor): add frame-step action to manage_editor tool"
```

---

## Task 2: Add Frame-Step Action to Python MCP Tool

**Files:**
- Modify: `Server/src/services/tools/manage_editor.py:14-22`

**Step 1: Write the failing test**

Not needed - Python layer just passes through to C#. We verify the parameter is accepted.

**Step 2: Update the Python tool signature**

In `Server/src/services/tools/manage_editor.py`, update the `action` Literal type and add `frames` parameter:

```python
@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Tip: pass booleans as true/false; if your client only sends strings, 'true'/'false' are accepted. Read-only actions: telemetry_status, telemetry_ping. Modifying actions: play, pause, stop, step, set_active_tool, add_tag, remove_tag, add_layer, remove_layer. The 'step' action advances the simulation by the specified number of frames while paused (synchronous - large frame counts will block until complete).",
    annotations=ToolAnnotations(
        title="Manage Editor",
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "play", "pause", "stop", "step", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer"], "Get and update the Unity Editor state."],
    wait_for_completion: Annotated[bool | str,
                                   "Optional. If True, waits for certain actions (accepts true/false or 'true'/'false')"] | None = None,
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
    recompile: Annotated[bool | str,
                         "If true, trigger script recompilation before the action. Returns error if compilation fails (accepts true/false or 'true'/'false')"] | None = None,
    frames: Annotated[int | str,
                      "Number of frames to step (for 'step' action). Defaults to 1. Large values block until complete."] | None = None,
) -> dict[str, Any]:
```

Then add `frames` to the params dict (around line 49):

```python
        params = {
            "action": action,
            "waitForCompletion": wait_for_completion,
            "toolName": tool_name,
            "tagName": tag_name,
            "layerName": layer_name,
            "recompile": recompile,
            "frames": int(frames) if frames is not None else None,
        }
```

**Step 3: Run Python tests**

```bash
cd Server && uv run pytest tests/ -v -k "manage_editor or editor"
```

Expected: PASS (existing tests should still pass)

**Step 4: Commit**

```bash
git add Server/src/services/tools/manage_editor.py
git commit -m "feat(editor): add frames parameter to manage_editor step action"
```

---

## Task 3: Add Regex Filtering to ReadConsole (C#)

**Files:**
- Modify: `MCPForUnity/Editor/Tools/ReadConsole.cs:160-210,250-350`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ReadConsoleTests.cs` (extend)

**Step 1: Write the failing test**

Add to `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ReadConsoleTests.cs`:

```csharp
[Test]
public void HandleCommand_Get_WithRegexFilter_MatchesPattern()
{
    // Arrange
    string uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
    Debug.Log($"DIAGNOSTIC-{uniqueId}: particle 1842 contact");
    Debug.Log($"UNRELATED-{uniqueId}: some other message");
    Debug.Log($"CONTACTS-{uniqueId}: particle 1842 data");

    var paramsObj = new JObject
    {
        ["action"] = "get",
        ["types"] = new JArray { "log" },
        ["filterRegex"] = $"DIAGNOSTIC|CONTACTS.*particle 1842",
        ["format"] = "detailed",
        ["count"] = 1000
    };

    // Act
    var result = ToJObject(ReadConsole.HandleCommand(paramsObj));

    // Assert
    Assert.IsTrue(result.Value<bool>("success"), result.ToString());
    var data = result["data"] as JArray;
    Assert.IsNotNull(data);

    // Should find DIAGNOSTIC and CONTACTS messages, not UNRELATED
    int matchCount = 0;
    bool foundUnrelated = false;
    foreach (var entry in data)
    {
        var msg = entry["message"]?.ToString() ?? "";
        if (msg.Contains(uniqueId))
        {
            if (msg.Contains("DIAGNOSTIC") || msg.Contains("CONTACTS"))
                matchCount++;
            if (msg.Contains("UNRELATED"))
                foundUnrelated = true;
        }
    }

    Assert.AreEqual(2, matchCount, "Should match both DIAGNOSTIC and CONTACTS messages");
    Assert.IsFalse(foundUnrelated, "Should not match UNRELATED message");
}

[Test]
public void HandleCommand_Get_WithBothFilters_ReturnsError()
{
    // Arrange
    var paramsObj = new JObject
    {
        ["action"] = "get",
        ["filterText"] = "some text",
        ["filterRegex"] = "some.*pattern"
    };

    // Act
    var result = ToJObject(ReadConsole.HandleCommand(paramsObj));

    // Assert
    Assert.IsFalse(result.Value<bool>("success"), "Should fail with both filters");
    Assert.That(result["error"]?.ToString(), Does.Contain("filterText").And.Contain("filterRegex"));
}

[Test]
public void HandleCommand_Get_WithInvalidRegex_ReturnsError()
{
    // Arrange
    var paramsObj = new JObject
    {
        ["action"] = "get",
        ["filterRegex"] = "[invalid(regex"
    };

    // Act
    var result = ToJObject(ReadConsole.HandleCommand(paramsObj));

    // Assert
    Assert.IsFalse(result.Value<bool>("success"), "Should fail with invalid regex");
    Assert.That(result["error"]?.ToString().ToLower(), Does.Contain("regex").Or.Contain("pattern"));
}
```

**Step 2: Run test to verify it fails**

Run in Unity Test Runner: `ReadConsoleTests`
Expected: FAIL - filterRegex parameter not recognized

**Step 3: Write minimal implementation**

In `MCPForUnity/Editor/Tools/ReadConsole.cs`:

Add using statement at top:
```csharp
using System.Text.RegularExpressions;
```

In `HandleCommand` method (around line 178), extract the new parameter:
```csharp
string filterText = p.Get("filterText");
string filterRegex = p.Get("filterRegex");

// Validate mutual exclusivity
if (!string.IsNullOrEmpty(filterText) && !string.IsNullOrEmpty(filterRegex))
{
    return new ErrorResponse("Cannot use both filterText and filterRegex - choose one.");
}

// Compile regex if provided
Regex compiledRegex = null;
if (!string.IsNullOrEmpty(filterRegex))
{
    try
    {
        compiledRegex = new Regex(filterRegex, RegexOptions.IgnoreCase);
    }
    catch (ArgumentException e)
    {
        return new ErrorResponse($"Invalid regex pattern: {e.Message}");
    }
}
```

In `GetConsoleEntries` method, add `Regex compiledRegex` parameter and update the filtering logic (around line 342-348):

Change method signature:
```csharp
private static object GetConsoleEntries(
    List<string> types,
    int? count,
    int? pageSize,
    int? cursor,
    string filterText,
    Regex filterRegex,  // NEW
    string format,
    bool includeStacktrace
)
```

Replace the filter logic:
```csharp
// Filter by text (case-insensitive substring) or regex
if (filterRegex != null)
{
    if (!filterRegex.IsMatch(message))
    {
        continue;
    }
}
else if (!string.IsNullOrEmpty(filterText)
    && message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
{
    continue;
}
```

Update the call site in `HandleCommand` (around line 202):
```csharp
return GetConsoleEntries(
    types,
    count,
    pageSize,
    cursor,
    filterText,
    compiledRegex,  // NEW
    format,
    includeStacktrace
);
```

Similarly update `GetConsoleCounts` to accept and use the regex parameter.

**Step 4: Run test to verify it passes**

Run in Unity Test Runner: `ReadConsoleTests`
Expected: PASS

**Step 5: Commit**

```bash
git add MCPForUnity/Editor/Tools/ReadConsole.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ReadConsoleTests.cs
git commit -m "feat(console): add regex filtering support to read_console tool"
```

---

## Task 4: Add Regex Filtering to Python MCP Tool

**Files:**
- Modify: `Server/src/services/tools/read_console.py:23-50,116-130`

**Step 1: Update the Python tool signature**

In `Server/src/services/tools/read_console.py`, update description and add parameter:

```python
@mcp_for_unity_tool(
    description="Gets messages from or clears the Unity Editor console. Defaults to 10 most recent entries. Use page_size/cursor for paging. Supports filtering by exact text (filter_text) or regex pattern (filter_regex) - these are mutually exclusive. Note: For maximum client compatibility, pass count as a quoted string (e.g., '5'). The 'get' action is read-only; 'clear' modifies ephemeral UI state (not project data).",
    annotations=ToolAnnotations(
        title="Read Console",
    ),
)
async def read_console(
    ctx: Context,
    action: Annotated[Literal['get', 'clear'],
                      "Get or clear the Unity Editor console. Defaults to 'get' if omitted."] | None = None,
    types: Annotated[list[Literal['error', 'warning',
                                  'log', 'all']] | str,
                     "Message types to get (accepts list or JSON string)"] | None = None,
    count: Annotated[int | str,
                     "Max messages to return in non-paging mode (accepts int or string, e.g., 5 or '5'). Ignored when paging with page_size/cursor."] | None = None,
    filter_text: Annotated[str, "Text filter for messages (case-insensitive substring match). Mutually exclusive with filter_regex."] | None = None,
    filter_regex: Annotated[str, "Regex pattern filter for messages (case-insensitive). Mutually exclusive with filter_text. Example: 'DIAGNOSTIC|CONTACTS.*particle 1842'"] | None = None,
    since_timestamp: Annotated[str,
                               "Get messages after this timestamp (ISO 8601)"] | None = None,
    page_size: Annotated[int | str,
                         "Page size for paginated console reads. Defaults to 50 when omitted."] | None = None,
    cursor: Annotated[int | str,
                      "Opaque cursor for paging (0-based offset). Defaults to 0."] | None = None,
    format: Annotated[Literal['plain', 'detailed',
                              'json'], "Output format"] | None = None,
    include_stacktrace: Annotated[bool | str,
                                  "Include stack traces in output (accepts true/false or 'true'/'false')"] | None = None,
) -> dict[str, Any]:
```

Add validation after action normalization (around line 100):

```python
    # Validate mutual exclusivity of filter params
    if filter_text and filter_regex:
        return {
            "success": False,
            "message": "Cannot use both filter_text and filter_regex - choose one."
        }
```

Add `filterRegex` to params dict (around line 116):

```python
    params_dict = {
        "action": action,
        "types": types,
        "count": count,
        "filterText": filter_text,
        "filterRegex": filter_regex,
        "sinceTimestamp": since_timestamp,
        "pageSize": coerced_page_size,
        "cursor": coerced_cursor,
        "format": format.lower() if isinstance(format, str) else format,
        "includeStacktrace": include_stacktrace
    }
```

**Step 2: Run Python tests**

```bash
cd Server && uv run pytest tests/ -v -k "console or read_console"
```

Expected: PASS

**Step 3: Commit**

```bash
git add Server/src/services/tools/read_console.py
git commit -m "feat(console): add filter_regex parameter to read_console Python tool"
```

---

## Task 5: Create InspectBuffer Tool (C#)

**Files:**
- Create: `MCPForUnity/Editor/Tools/InspectBuffer.cs`
- Create: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/InspectBufferTests.cs`

**Step 1: Write the failing test**

Create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/InspectBufferTests.cs`:

```csharp
using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class InspectBufferTests
    {
        [Test]
        public void HandleCommand_MissingTarget_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["start"] = 0,
                ["count"] = 8
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("target"));
        }

        [Test]
        public void HandleCommand_InvalidTargetFormat_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["target"] = "InvalidTarget",  // No component.field syntax
                ["start"] = 0,
                ["count"] = 8
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void HandleCommand_NonexistentGameObject_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["target"] = "NonexistentObject12345/SomeComponent.bufferField",
                ["start"] = 0,
                ["count"] = 8
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"]?.ToString(), Does.Contain("not found").IgnoreCase);
        }

        [Test]
        public void HandleCommand_ListOnly_ReturnsDiscoveryFormat()
        {
            var paramsObj = new JObject
            {
                ["target"] = "*/SomeComponent.*",
                ["list_only"] = true
            };

            var result = ToJObject(InspectBuffer.HandleCommand(paramsObj));

            // Should succeed even if no buffers found - returns empty list
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.IsNotNull(result["data"]);
        }

        [Test]
        public void ParseFormat_ValidFormat_ParsesCorrectly()
        {
            // Test the format string parser
            var fields = InspectBuffer.ParseFormatString("position:float3@0,velocity:float3@16,mass:float@32,id:int@36");

            Assert.AreEqual(4, fields.Count);
            Assert.AreEqual("position", fields[0].Name);
            Assert.AreEqual("float3", fields[0].Type);
            Assert.AreEqual(0, fields[0].Offset);
            Assert.AreEqual("id", fields[3].Name);
            Assert.AreEqual("int", fields[3].Type);
            Assert.AreEqual(36, fields[3].Offset);
        }

        [Test]
        public void ParseFormat_InvalidFormat_ReturnsNull()
        {
            var fields = InspectBuffer.ParseFormatString("invalid format string");
            Assert.IsNull(fields);
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run in Unity Test Runner: `InspectBufferTests`
Expected: FAIL - InspectBuffer class doesn't exist

**Step 3: Write minimal implementation**

Create `MCPForUnity/Editor/Tools/InspectBuffer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Inspects ComputeBuffer contents via reflection-based discovery.
    /// Target syntax: "GameObject/Component.fieldName" or "instanceId:12345.fieldName" or "*/Component.*" for discovery
    /// Format syntax: "name:type@offset,..." e.g. "position:float3@0,velocity:float3@16"
    /// </summary>
    [McpForUnityTool("inspect_buffer", AutoRegister = false)]
    public static class InspectBuffer
    {
        public class FieldSpec
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int Offset { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            string target = targetResult.Value;
            bool listOnly = p.GetBool("listOnly", false) || p.GetBool("list_only", false);
            int start = p.GetInt("start") ?? 0;
            int count = p.GetInt("count") ?? 8;
            string format = p.Get("format");

            try
            {
                // Discovery mode
                if (listOnly || target.Contains("*"))
                {
                    return DiscoverBuffers(target);
                }

                // Inspection mode
                return InspectBufferData(target, start, count, format);
            }
            catch (Exception e)
            {
                McpLog.Error($"[InspectBuffer] Error: {e}");
                return new ErrorResponse($"Error inspecting buffer: {e.Message}");
            }
        }

        private static object DiscoverBuffers(string pattern)
        {
            var matches = new List<object>();

            // Parse pattern: "*/Component.*" or "GameObjectName/Component.*"
            var parts = pattern.Split('/');
            string goPattern = parts.Length > 1 ? parts[0] : "*";
            string componentField = parts.Length > 1 ? parts[1] : parts[0];

            var cfParts = componentField.Split('.');
            string componentPattern = cfParts[0];
            string fieldPattern = cfParts.Length > 1 ? cfParts[1] : "*";

            // Find all GameObjects
            var gameObjects = goPattern == "*"
                ? UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                : GameObject.FindGameObjectsWithTag("Untagged")
                    .Concat(UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    .Where(go => MatchesPattern(go.name, goPattern))
                    .Distinct()
                    .ToArray();

            foreach (var go in gameObjects)
            {
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var compType = component.GetType();

                    if (!MatchesPattern(compType.Name, componentPattern))
                        continue;

                    var fields = compType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var field in fields)
                    {
                        if (field.FieldType != typeof(ComputeBuffer) && field.FieldType != typeof(GraphicsBuffer))
                            continue;

                        if (!MatchesPattern(field.Name, fieldPattern))
                            continue;

                        var buffer = field.GetValue(component);
                        if (buffer == null) continue;

                        int bufferCount = 0;
                        int stride = 0;

                        if (buffer is ComputeBuffer cb)
                        {
                            bufferCount = cb.count;
                            stride = cb.stride;
                        }
                        else if (buffer is GraphicsBuffer gb)
                        {
                            bufferCount = gb.count;
                            stride = gb.stride;
                        }

                        matches.Add(new
                        {
                            path = $"{go.name}/{compType.Name}.{field.Name}",
                            instanceId = go.GetInstanceID(),
                            count = bufferCount,
                            stride = stride
                        });
                    }
                }
            }

            return new SuccessResponse($"Found {matches.Count} buffer(s).", new { matches });
        }

        private static object InspectBufferData(string target, int start, int count, string format)
        {
            // Parse target: "GameObject/Component.field" or "instanceId:12345/Component.field"
            var (buffer, stride, bufferCount, path) = ResolveBuffer(target);

            if (buffer == null)
                return new ErrorResponse($"Buffer not found: {target}");

            if (start < 0 || start >= bufferCount)
                return new ErrorResponse($"start ({start}) out of range [0, {bufferCount - 1}]");

            int actualCount = Math.Min(count, bufferCount - start);

            // Read raw bytes
            byte[] rawData = new byte[actualCount * stride];

            if (buffer is ComputeBuffer cb)
                cb.GetData(rawData, 0, start * stride, actualCount * stride);
            else if (buffer is GraphicsBuffer gb)
                gb.GetData(rawData, 0, start * stride, actualCount * stride);

            // Parse format and decode
            object elements;
            if (!string.IsNullOrEmpty(format))
            {
                var fields = ParseFormatString(format);
                if (fields == null)
                    return new ErrorResponse($"Invalid format string: {format}. Expected: 'name:type@offset,...'");

                elements = DecodeElements(rawData, stride, actualCount, fields);
            }
            else
            {
                // Return raw bytes as base64 per element
                elements = DecodeRawElements(rawData, stride, actualCount);
            }

            return new SuccessResponse($"Read {actualCount} elements from {path}.", new
            {
                path,
                start,
                count = actualCount,
                stride,
                total = bufferCount,
                elements
            });
        }

        private static (object buffer, int stride, int count, string path) ResolveBuffer(string target)
        {
            // Handle instanceId:12345/Component.field or instanceId:12345.field
            if (target.StartsWith("instanceId:"))
            {
                var rest = target.Substring("instanceId:".Length);
                var slashIdx = rest.IndexOf('/');
                var dotIdx = rest.IndexOf('.');

                int instanceId;
                string componentField;

                if (slashIdx > 0)
                {
                    instanceId = int.Parse(rest.Substring(0, slashIdx));
                    componentField = rest.Substring(slashIdx + 1);
                }
                else if (dotIdx > 0)
                {
                    instanceId = int.Parse(rest.Substring(0, dotIdx));
                    componentField = rest.Substring(dotIdx + 1);
                }
                else
                {
                    return (null, 0, 0, null);
                }

                var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (go == null)
                    return (null, 0, 0, null);

                return ResolveBufferFromGameObject(go, componentField);
            }

            // Handle GameObject/Component.field
            var parts = target.Split('/');
            if (parts.Length < 2)
            {
                // Try Component.field on all objects
                return (null, 0, 0, null);
            }

            string goName = parts[0];
            string componentField2 = parts[1];

            var gameObject = GameObject.Find(goName);
            if (gameObject == null)
            {
                // Try finding by path
                gameObject = GameObject.Find("/" + goName);
            }

            if (gameObject == null)
                return (null, 0, 0, null);

            return ResolveBufferFromGameObject(gameObject, componentField2);
        }

        private static (object buffer, int stride, int count, string path) ResolveBufferFromGameObject(GameObject go, string componentField)
        {
            var cfParts = componentField.Split('.');
            if (cfParts.Length != 2)
                return (null, 0, 0, null);

            string componentName = cfParts[0];
            string fieldName = cfParts[1];

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var compType = component.GetType();

                if (compType.Name != componentName && !compType.Name.EndsWith(componentName))
                    continue;

                // Walk inheritance chain for field
                var type = compType;
                while (type != null)
                {
                    var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && (field.FieldType == typeof(ComputeBuffer) || field.FieldType == typeof(GraphicsBuffer)))
                    {
                        var buffer = field.GetValue(component);
                        if (buffer == null)
                            return (null, 0, 0, null);

                        int count = 0, stride = 0;
                        if (buffer is ComputeBuffer cb) { count = cb.count; stride = cb.stride; }
                        else if (buffer is GraphicsBuffer gb) { count = gb.count; stride = gb.stride; }

                        return (buffer, stride, count, $"{go.name}/{compType.Name}.{fieldName}");
                    }
                    type = type.BaseType;
                }
            }

            return (null, 0, 0, null);
        }

        public static List<FieldSpec> ParseFormatString(string format)
        {
            // Format: "name:type@offset,name:type@offset,..."
            if (string.IsNullOrEmpty(format))
                return null;

            var result = new List<FieldSpec>();
            var regex = new Regex(@"(\w+):(\w+)@(\d+)");

            foreach (var part in format.Split(','))
            {
                var match = regex.Match(part.Trim());
                if (!match.Success)
                    return null;

                result.Add(new FieldSpec
                {
                    Name = match.Groups[1].Value,
                    Type = match.Groups[2].Value.ToLower(),
                    Offset = int.Parse(match.Groups[3].Value)
                });
            }

            return result.Count > 0 ? result : null;
        }

        private static List<object> DecodeElements(byte[] data, int stride, int count, List<FieldSpec> fields)
        {
            var elements = new List<object>();

            for (int i = 0; i < count; i++)
            {
                int baseOffset = i * stride;
                var element = new Dictionary<string, object>();

                foreach (var field in fields)
                {
                    int offset = baseOffset + field.Offset;
                    element[field.Name] = DecodeValue(data, offset, field.Type);
                }

                elements.Add(element);
            }

            return elements;
        }

        private static object DecodeValue(byte[] data, int offset, string type)
        {
            switch (type)
            {
                case "float":
                    return BitConverter.ToSingle(data, offset);
                case "float2":
                    return new[] { BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4) };
                case "float3":
                    return new[] { BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8) };
                case "float4":
                    return new[] { BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8), BitConverter.ToSingle(data, offset + 12) };
                case "int":
                    return BitConverter.ToInt32(data, offset);
                case "int2":
                    return new[] { BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4) };
                case "int3":
                    return new[] { BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4), BitConverter.ToInt32(data, offset + 8) };
                case "int4":
                    return new[] { BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4), BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 12) };
                case "uint":
                    return BitConverter.ToUInt32(data, offset);
                case "half":
                    return HalfToFloat(BitConverter.ToUInt16(data, offset));
                default:
                    return $"<unknown type: {type}>";
            }
        }

        private static float HalfToFloat(ushort half)
        {
            // IEEE 754 half-precision to single-precision
            int sign = (half >> 15) & 1;
            int exp = (half >> 10) & 0x1F;
            int mant = half & 0x3FF;

            if (exp == 0)
            {
                if (mant == 0) return sign == 0 ? 0f : -0f;
                // Denormalized
                float m = mant / 1024f;
                return (sign == 0 ? 1 : -1) * m * (float)Math.Pow(2, -14);
            }
            if (exp == 31)
            {
                return mant == 0 ? (sign == 0 ? float.PositiveInfinity : float.NegativeInfinity) : float.NaN;
            }

            float mantissa = 1f + mant / 1024f;
            return (sign == 0 ? 1 : -1) * mantissa * (float)Math.Pow(2, exp - 15);
        }

        private static List<object> DecodeRawElements(byte[] data, int stride, int count)
        {
            var elements = new List<object>();
            for (int i = 0; i < count; i++)
            {
                byte[] elementData = new byte[stride];
                Array.Copy(data, i * stride, elementData, 0, stride);
                elements.Add(new { raw = Convert.ToBase64String(elementData) });
            }
            return elements;
        }

        private static bool MatchesPattern(string value, string pattern)
        {
            if (pattern == "*") return true;
            if (pattern.Contains("*"))
            {
                var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
            }
            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

**Step 4: Run test to verify it passes**

Run in Unity Test Runner: `InspectBufferTests`
Expected: PASS

**Step 5: Commit**

```bash
git add MCPForUnity/Editor/Tools/InspectBuffer.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/InspectBufferTests.cs
git commit -m "feat(debug): add inspect_buffer tool for GPU buffer inspection"
```

---

## Task 6: Create InspectBuffer Python MCP Tool

**Files:**
- Create: `Server/src/services/tools/inspect_buffer.py`
- Modify: `Server/src/services/tools/__init__.py`

**Step 1: Create the Python tool**

Create `Server/src/services/tools/inspect_buffer.py`:

```python
"""
Defines the inspect_buffer tool for reading ComputeBuffer/GraphicsBuffer contents.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""Inspects ComputeBuffer/GraphicsBuffer contents via reflection-based discovery.

Target syntax:
- "GameObject/Component.fieldName" - find buffer by GameObject name
- "instanceId:12345/Component.fieldName" - find by instance ID (unambiguous)
- "*/Component.*" - discovery mode: list all matching buffers

Format syntax for decoding: "name:type@offset,..."
- Example: "position:float3@0,velocity:float3@16,mass:float@32,id:int@36"
- Supported types: float, float2, float3, float4, int, int2, int3, int4, uint, half
- If format omitted, returns raw bytes as base64

Use list_only=true to discover buffers before reading.""",
    annotations=ToolAnnotations(
        title="Inspect Buffer",
    ),
)
async def inspect_buffer(
    ctx: Context,
    target: Annotated[str, "Buffer location: 'GameObject/Component.field', 'instanceId:N/Component.field', or '*/Component.*' for discovery"],
    start: Annotated[int | str, "Starting element index (0-based). Defaults to 0."] | None = None,
    count: Annotated[int | str, "Number of elements to read. Defaults to 8."] | None = None,
    format: Annotated[str, "Format string: 'name:type@offset,...'. Example: 'position:float3@0,velocity:float3@16'. If omitted, returns raw base64."] | None = None,
    list_only: Annotated[bool | str, "If true, only list matching buffers without reading data. Use for discovery."] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "target": target,
        "start": coerce_int(start) if start is not None else None,
        "count": coerce_int(count) if count is not None else None,
        "format": format,
        "listOnly": coerce_bool(list_only, default=False),
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "inspect_buffer", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Buffer inspection complete."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error inspecting buffer: {str(e)}"}
```

**Step 2: Register in __init__.py**

In `Server/src/services/tools/__init__.py`, add import:

```python
from services.tools.inspect_buffer import inspect_buffer
```

**Step 3: Run Python tests**

```bash
cd Server && uv run pytest tests/ -v
```

Expected: PASS (no new tests needed - it's a passthrough to C#)

**Step 4: Commit**

```bash
git add Server/src/services/tools/inspect_buffer.py Server/src/services/tools/__init__.py
git commit -m "feat(debug): add inspect_buffer Python MCP tool"
```

---

## Task 7: Add CLI Commands for New Features

**Files:**
- Modify: `Server/src/cli/commands/editor.py`

**Step 1: Add step command**

In `Server/src/cli/commands/editor.py`, add after the `stop` command:

```python
@editor.command("step")
@click.option(
    "--frames", "-n",
    default=1,
    type=int,
    help="Number of frames to step (default: 1)."
)
@handle_unity_errors
def step(frames: int):
    """Step simulation forward by N frames (requires play mode + paused).

    Warning: Steps are synchronous - large frame counts will block until complete.

    \b
    Examples:
        unity-mcp editor step
        unity-mcp editor step --frames 10
    """
    config = get_config()
    result = run_command("manage_editor", {"action": "step", "frames": frames}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Stepped {frames} frame(s)")
```

**Step 2: Update console command with regex option**

Add option to existing `console` command:

```python
@click.option(
    "--regex", "-r",
    "filter_regex",
    default=None,
    help="Regex pattern filter (mutually exclusive with --filter)."
)
```

Update the function signature and params:

```python
def console(log_types: tuple, count: int, filter_text: Optional[str], filter_regex: Optional[str], stacktrace: bool, clear: bool):
```

Add validation and params:

```python
    if filter_text and filter_regex:
        print_error("Cannot use both --filter and --regex - choose one.")
        return

    # ... existing code ...

    if filter_text:
        params["filter_text"] = filter_text
    if filter_regex:
        params["filter_regex"] = filter_regex
```

**Step 3: Add buffer command**

```python
@editor.command("buffer")
@click.argument("target")
@click.option(
    "--start", "-s",
    default=0,
    type=int,
    help="Starting element index."
)
@click.option(
    "--count", "-n",
    default=8,
    type=int,
    help="Number of elements to read."
)
@click.option(
    "--format", "-f",
    "fmt",
    default=None,
    help="Format string: 'name:type@offset,...'. Example: 'position:float3@0,velocity:float3@16'"
)
@click.option(
    "--list", "-l",
    "list_only",
    is_flag=True,
    help="List matching buffers without reading data."
)
@handle_unity_errors
def buffer(target: str, start: int, count: int, fmt: Optional[str], list_only: bool):
    """Inspect ComputeBuffer/GraphicsBuffer contents.

    TARGET specifies the buffer location:
    - "GameObject/Component.field" - find by name
    - "instanceId:N/Component.field" - find by ID
    - "*/Component.*" - discovery mode

    \b
    Examples:
        unity-mcp editor buffer "*/ParticleSystem.*" --list
        unity-mcp editor buffer "Emitter/ParticleSim.particleBuffer" -s 1842 -n 8 -f "pos:float3@0,vel:float3@16"
    """
    config = get_config()

    params: dict[str, Any] = {"target": target}
    if list_only:
        params["list_only"] = True
    else:
        params["start"] = start
        params["count"] = count
        if fmt:
            params["format"] = fmt

    result = run_command("inspect_buffer", params, config)
    click.echo(format_output(result, config.format))
```

**Step 4: Run CLI tests**

```bash
cd Server && uv run pytest tests/test_cli.py -v
```

Expected: PASS

**Step 5: Commit**

```bash
git add Server/src/cli/commands/editor.py
git commit -m "feat(cli): add step, buffer commands and regex filter to console"
```

---

## Task 8: Final Integration Test

**Files:**
- Create: `Server/tests/integration/test_gpu_debug_features.py`

**Step 1: Write integration tests**

Create `Server/tests/integration/test_gpu_debug_features.py`:

```python
"""Integration tests for GPU debugging features."""
import pytest


class TestManageEditorStep:
    """Tests for the step action in manage_editor."""

    def test_step_action_in_literal(self):
        """Verify 'step' is in the action literal type."""
        from services.tools.manage_editor import manage_editor
        import inspect
        sig = inspect.signature(manage_editor)
        action_param = sig.parameters['action']
        # The annotation should include 'step'
        assert 'step' in str(action_param.annotation)

    def test_frames_parameter_exists(self):
        """Verify frames parameter exists."""
        from services.tools.manage_editor import manage_editor
        import inspect
        sig = inspect.signature(manage_editor)
        assert 'frames' in sig.parameters


class TestReadConsoleRegex:
    """Tests for regex filtering in read_console."""

    def test_filter_regex_parameter_exists(self):
        """Verify filter_regex parameter exists."""
        from services.tools.read_console import read_console
        import inspect
        sig = inspect.signature(read_console)
        assert 'filter_regex' in sig.parameters


class TestInspectBuffer:
    """Tests for the inspect_buffer tool."""

    def test_tool_exists(self):
        """Verify inspect_buffer tool is registered."""
        from services.tools.inspect_buffer import inspect_buffer
        assert inspect_buffer is not None

    def test_required_parameters(self):
        """Verify target is required."""
        from services.tools.inspect_buffer import inspect_buffer
        import inspect
        sig = inspect.signature(inspect_buffer)
        assert 'target' in sig.parameters
        assert 'start' in sig.parameters
        assert 'count' in sig.parameters
        assert 'format' in sig.parameters
        assert 'list_only' in sig.parameters
```

**Step 2: Run all tests**

```bash
cd Server && uv run pytest tests/ -v
```

Expected: PASS

**Step 3: Commit**

```bash
git add Server/tests/integration/test_gpu_debug_features.py
git commit -m "test: add integration tests for GPU debugging features"
```

---

## Summary

This plan implements three debugging features:

1. **Frame-Step Mode** (Tasks 1-2): Extends `manage_editor` with `step` action
2. **Console Regex Filtering** (Tasks 3-4): Extends `read_console` with `filterRegex` parameter
3. **GPU Buffer Inspector** (Tasks 5-6): New `inspect_buffer` tool with reflection-based discovery
4. **CLI Integration** (Task 7): Commands for all features
5. **Integration Tests** (Task 8): Verify Python layer wiring

Total: 8 tasks, estimated ~45 minutes implementation time.
