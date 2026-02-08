using System;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Tests;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Starts a Unity Test Runner run asynchronously and returns a job id immediately.
    /// Use get_test_job(job_id) to poll status/results.
    /// </summary>
    [McpForUnityTool("run_tests", AutoRegister = false,
        Description = "Starts a Unity test run asynchronously and returns a job_id immediately. " +
        "Poll with get_test_job for progress. " +
        "Set recompile=true to trigger script recompilation before running (returns error if compilation fails). " +
        "Filter options: test_names (exact full names like 'Namespace.Class.Method'), " +
        "group_names (regex patterns like '.*MethodName.*'), " +
        "category_names (NUnit categories), assembly_names (assembly filter).")]
    public static class RunTests
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
            try
            {
                // Check for clear_stuck action first (allowed in play mode)
                if (ParamCoercion.CoerceBool(@params?["clear_stuck"], false))
                {
                    bool wasCleared = TestJobManager.ClearStuckJob();
                    return new SuccessResponse(
                        wasCleared ? "Stuck job cleared." : "No running job to clear.",
                        new { cleared = wasCleared }
                    );
                }

                if (EditorApplication.isPlaying)
                {
                    return new ErrorResponse("Cannot run tests in play mode. Exit play mode first.");
                }

                string modeStr = @params?["mode"]?.ToString();
                if (string.IsNullOrWhiteSpace(modeStr))
                {
                    modeStr = "EditMode";
                }

                if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
                {
                    return new ErrorResponse(parseError);
                }

                var recompile = ParamCoercion.CoerceBool(
                    @params?["recompile"], false);

                if (recompile)
                {
                    var compileError = await RecompileHelper.RecompileAndWaitAsync().ConfigureAwait(true);
                    if (compileError != null) return compileError;
                }

                bool includeDetails = ParamCoercion.CoerceBool(
                    @params?["includeDetails"] ?? @params?["include_details"], false);
                bool includeFailedTests = ParamCoercion.CoerceBool(
                    @params?["includeFailedTests"] ?? @params?["include_failed_tests"], false);

                var filterOptions = GetFilterOptions(@params);
                string jobId = TestJobManager.StartJob(parsedMode.Value, filterOptions);

                return new SuccessResponse("Test job started.", new
                {
                    job_id = jobId,
                    status = "running",
                    mode = parsedMode.Value.ToString(),
                    include_details = includeDetails,
                    include_failed_tests = includeFailedTests
                });
            }
            catch (Exception ex)
            {
                // Normalize the already-running case to a stable error token.
                if (ex.Message != null && ex.Message.IndexOf("already in progress", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ErrorResponse("tests_running", new { reason = "tests_running", retry_after_ms = 5000 });
                }
                return new ErrorResponse($"Failed to start test job: {ex.Message}");
            }
        }

        private static TestFilterOptions GetFilterOptions(JObject @params)
        {
            if (@params == null)
            {
                return null;
            }

            string[] ParseStringArray(string camelCaseKey)
            {
                var token = @params[camelCaseKey];
                if (token == null)
                {
                    var snakeKey = StringCaseUtility.ToSnakeCase(camelCaseKey);
                    token = @params[snakeKey];
                }
                if (token == null) return null;
                // Handle double-serialized arrays: the MCP bridge may send a JSON array
                // string (e.g. "[\"name\"]") as a single string element inside an outer array.
                // Unwrap by attempting to parse string values that look like JSON arrays.
                string[] UnwrapValues(string[] raw)
                {
                    if (raw == null) return null;
                    var unwrapped = new System.Collections.Generic.List<string>();
                    foreach (var s in raw)
                    {
                        if (s != null && s.StartsWith("[") && s.EndsWith("]"))
                        {
                            try
                            {
                                var inner = JArray.Parse(s);
                                foreach (var item in inner.Values<string>())
                                {
                                    if (!string.IsNullOrWhiteSpace(item))
                                        unwrapped.Add(item);
                                }
                                continue;
                            }
                            catch { }
                        }
                        if (!string.IsNullOrWhiteSpace(s))
                            unwrapped.Add(s);
                    }
                    return unwrapped.Count > 0 ? unwrapped.ToArray() : null;
                }

                if (token.Type == JTokenType.String)
                {
                    var value = token.ToString();
                    if (string.IsNullOrWhiteSpace(value)) return null;
                    // Try to parse as JSON array in case of double-serialization
                    if (value.StartsWith("[") && value.EndsWith("]"))
                    {
                        try
                        {
                            var inner = JArray.Parse(value);
                            var innerValues = inner.Values<string>()
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToArray();
                            if (innerValues.Length > 0) return innerValues;
                        }
                        catch { }
                    }
                    return new[] { value };
                }
                if (token.Type == JTokenType.Array)
                {
                    var array = token as JArray;
                    if (array == null || array.Count == 0) return null;
                    var values = array
                        .Values<string>()
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                    return UnwrapValues(values);
                }
                return null;
            }

            var testNames = ParseStringArray("testNames");
            var groupNames = ParseStringArray("groupNames");
            var categoryNames = ParseStringArray("categoryNames");
            var assemblyNames = ParseStringArray("assemblyNames");

            if (testNames == null && groupNames == null && categoryNames == null && assemblyNames == null)
            {
                return null;
            }

            return new TestFilterOptions
            {
                TestNames = testNames,
                GroupNames = groupNames,
                CategoryNames = categoryNames,
                AssemblyNames = assemblyNames
            };
        }
    }
}
