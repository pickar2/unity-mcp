using System;
using System.Threading.Tasks;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
	internal static class RecompileHelper
	{
		private const int DefaultTimeoutSeconds = 60;

		// Returns null on success, or an ErrorResponse if compilation failed or timed out.
		public static async Task<object> RecompileAndWaitAsync(int timeoutSeconds = DefaultTimeoutSeconds)
		{
			try
			{
				AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
			}
			catch (Exception ex)
			{
				return new ErrorResponse("recompile_refresh_failed", new
				{
					reason = "asset_refresh_failed",
					message = ex.Message,
				});
			}

			try
			{
				await WaitForCompilationAsync(TimeSpan.FromSeconds(timeoutSeconds)).ConfigureAwait(true);
			}
			catch (TimeoutException)
			{
				return new ErrorResponse("recompile_timeout", new
				{
					reason = "compilation_timeout",
					timeout_seconds = timeoutSeconds,
				});
			}

			if (EditorUtility.scriptCompilationFailed)
			{
				return new ErrorResponse("compilation_failed", new
				{
					reason = "compilation_failed",
					hint = "Check read_console with types=[\"error\"] for compiler diagnostics.",
				});
			}

			return null;
		}

		private static Task WaitForCompilationAsync(TimeSpan timeout)
		{
			if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
			{
				return Task.CompletedTask;
			}

			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var start = DateTime.UtcNow;

			void Tick()
			{
				try
				{
					if (tcs.Task.IsCompleted)
					{
						EditorApplication.update -= Tick;
						return;
					}

					if ((DateTime.UtcNow - start) > timeout)
					{
						EditorApplication.update -= Tick;
						tcs.TrySetException(new TimeoutException());
						return;
					}

					if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
					{
						EditorApplication.update -= Tick;
						tcs.TrySetResult(true);
					}
				}
				catch (Exception ex)
				{
					EditorApplication.update -= Tick;
					tcs.TrySetException(ex);
				}
			}

			EditorApplication.update += Tick;
			try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
			return tcs.Task;
		}
	}
}
