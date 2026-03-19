using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Ensures HTTP transports resume after domain reloads similar to the legacy stdio bridge.
    /// </summary>
    [InitializeOnLoad]
    internal static class HttpBridgeReloadHandler
    {
        private static readonly TimeSpan[] ResumeRetrySchedule =
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        private static CancellationTokenSource _retryCts;

        static HttpBridgeReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += CancelRetries;
        }

        private static void CancelRetries()
        {
            try { _retryCts?.Cancel(); } catch { }
        }

        private static void OnBeforeAssemblyReload()
        {
            // Cancel any in-flight retry loop before the next reload.
            CancelRetries();

            try
            {
                var transport = MCPServiceLocator.TransportManager;
                bool shouldResume = transport.IsRunning(TransportMode.Http);

                if (shouldResume)
                {
                    EditorPrefs.SetBool(EditorPrefKeys.ResumeHttpAfterReload, true);
                }
                else
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }

                if (shouldResume)
                {
                    // beforeAssemblyReload is synchronous; force a synchronous teardown so we do not
                    // leave an orphaned socket due to an unfinished async close handshake.
                    transport.ForceStop(TransportMode.Http);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to evaluate HTTP bridge reload state: {ex.Message}");
            }
        }

        private static void OnAfterAssemblyReload()
        {
            bool resume = false;
            try
            {
                // Only resume HTTP if it is still the selected transport.
                bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
                resume = useHttp && EditorPrefs.GetBool(EditorPrefKeys.ResumeHttpAfterReload, false);
                if (resume)
                {
                    EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read HTTP bridge reload flag: {ex.Message}");
                resume = false;
            }

            if (!resume)
            {
                return;
            }

            // If the editor is not compiling, attempt an immediate restart without relying on editor focus.
            bool isCompiling = EditorApplication.isCompiling;
            try
            {
                var pipeline = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                var prop = pipeline?.GetProperty("isCompiling", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null) isCompiling |= (bool)prop.GetValue(null);
            }
            catch { }

            if (!isCompiling)
            {
                _ = ResumeHttpWithRetriesAsync();
                return;
            }

            // Fallback when compiling: schedule on the editor loop
            EditorApplication.delayCall += () =>
            {
                _ = ResumeHttpWithRetriesAsync();
            };
        }

        private static async Task ResumeHttpWithRetriesAsync()
        {
            // Cancel any previous retry loop and create a fresh token.
            CancelRetries();
            var cts = _retryCts = new CancellationTokenSource();
            var token = cts.Token;

            Exception lastException = null;

            for (int i = 0; i < ResumeRetrySchedule.Length; i++)
            {
                if (token.IsCancellationRequested) return;

                int attempt = i + 1;
                McpLog.Debug($"[HTTP Reload] Resume attempt {attempt}/{ResumeRetrySchedule.Length}");

                TimeSpan delay = ResumeRetrySchedule[i];
                if (delay > TimeSpan.Zero)
                {
                    McpLog.Debug($"[HTTP Reload] Waiting {delay.TotalSeconds:0.#}s before resume attempt {attempt}");
                    try { await Task.Delay(delay, token); }
                    catch (OperationCanceledException) { return; }
                }

                // Abort retries if the user switched transports while we were waiting.
                if (!EditorConfigurationCache.Instance.UseHttpTransport)
                {
                    try { EditorPrefs.DeleteKey(EditorPrefKeys.ResumeHttpAfterReload); } catch { }
                    return;
                }

                try
                {
                    bool started = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                    if (started)
                    {
                        McpLog.Debug($"[HTTP Reload] Resume succeeded on attempt {attempt}");
                        MCPForUnityEditorWindow.RequestHealthVerification();
                        return;
                    }

                    var state = MCPServiceLocator.TransportManager.GetState(TransportMode.Http);
                    string reason = string.IsNullOrWhiteSpace(state?.Error) ? "no error detail" : state.Error;
                    McpLog.Debug($"[HTTP Reload] Resume attempt {attempt} failed: {reason}");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    McpLog.Debug($"[HTTP Reload] Resume attempt {attempt} threw: {ex.Message}");
                }
            }

            // All retries exhausted — schedule an idle-based fallback that keeps trying
            // every few seconds until the transport comes back or the user switches away.
            McpLog.Debug("HTTP resume retries exhausted; scheduling idle fallback.");
            ScheduleIdleFallback();
        }

        private static double _nextIdleRetryTime;

        private static void ScheduleIdleFallback()
        {
            _nextIdleRetryTime = EditorApplication.timeSinceStartup + 5.0;
            EditorApplication.update -= IdleFallbackTick;
            EditorApplication.update += IdleFallbackTick;
        }

        private static void IdleFallbackTick()
        {
            if (EditorApplication.timeSinceStartup < _nextIdleRetryTime)
                return;

            // Stop if transport mode changed or editor is compiling
            if (!EditorConfigurationCache.Instance.UseHttpTransport || EditorApplication.isCompiling)
            {
                EditorApplication.update -= IdleFallbackTick;
                return;
            }

            // Already connected — stop polling
            if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http))
            {
                EditorApplication.update -= IdleFallbackTick;
                return;
            }

            _nextIdleRetryTime = EditorApplication.timeSinceStartup + 10.0;
            _ = TryReconnectOnceAsync();
        }

        private static async Task TryReconnectOnceAsync()
        {
            try
            {
                bool started = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                if (started)
                {
                    McpLog.Debug("[HTTP Reload] Idle fallback reconnected.");
                    EditorApplication.update -= IdleFallbackTick;
                    MCPForUnityEditorWindow.RequestHealthVerification();
                }
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[HTTP Reload] Idle fallback attempt failed: {ex.Message}");
            }
        }
    }
}
