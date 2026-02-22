using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Singleton service that captures Unity log messages into a ring buffer.
    /// Auto-initializes on domain load via InitializeOnLoad.
    /// On initialization, backfills existing Unity console entries via reflection
    /// so that compile errors from before the hook registered are visible.
    /// </summary>
    [InitializeOnLoad]
    public static class LogCaptureService
    {
        private static LogBuffer _buffer;
        private static bool _isSubscribed = false;
        private static readonly object _initLock = new object();

        public const int DefaultCapacity = 10000;

        // Reflection members for backfilling from Unity's internal LogEntries
        private static MethodInfo _startGettingEntriesMethod;
        private static MethodInfo _endGettingEntriesMethod;
        private static MethodInfo _getCountMethod;
        private static MethodInfo _getEntryMethod;
        private static FieldInfo _modeField;
        private static FieldInfo _messageField;
        private static Type _logEntryType;
        private static bool _reflectionInitialized;

        // Mode bit constants for LogEntry.mode
        private const int ModeBitError = 1 << 0;
        private const int ModeBitAssert = 1 << 1;
        private const int ModeBitWarning = 1 << 2;
        private const int ModeBitException = 1 << 4;
        private const int ModeBitScriptingError = 1 << 9;
        private const int ModeBitScriptingWarning = 1 << 10;
        private const int ModeBitScriptingException = 1 << 18;
        private const int ModeBitScriptingAssertion = 1 << 22;

        static LogCaptureService()
        {
            Initialize();
        }

        public static void Initialize(int capacity = DefaultCapacity)
        {
            lock (_initLock)
            {
                bool freshBuffer = _buffer == null;
                if (freshBuffer)
                {
                    _buffer = new LogBuffer(capacity);
                }

                if (!_isSubscribed)
                {
                    Application.logMessageReceived += OnLogMessageReceived;
                    _isSubscribed = true;
                }

                // Backfill existing console entries on fresh buffer (domain reload)
                if (freshBuffer)
                {
                    BackfillFromUnityConsole();
                }
            }
        }

        private static void InitializeReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null) return;

                BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", staticFlags);
                _endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", staticFlags);
                _getCountMethod = logEntriesType.GetMethod("GetCount", staticFlags);
                _getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", staticFlags);

                _logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                if (_logEntryType != null)
                {
                    _modeField = _logEntryType.GetField("mode", instanceFlags);
                    _messageField = _logEntryType.GetField("message", instanceFlags);
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[LogCaptureService] Reflection init failed: {e.Message}");
            }
        }

        private static void BackfillFromUnityConsole()
        {
            InitializeReflection();

            if (_startGettingEntriesMethod == null || _getEntryMethod == null ||
                _getCountMethod == null || _logEntryType == null ||
                _modeField == null || _messageField == null)
            {
                return;
            }

            try
            {
                _startGettingEntriesMethod.Invoke(null, null);
                int count = (int)_getCountMethod.Invoke(null, null);
                if (count == 0)
                {
                    _endGettingEntriesMethod?.Invoke(null, null);
                    return;
                }

                object logEntryInstance = Activator.CreateInstance(_logEntryType);

                for (int i = 0; i < count; i++)
                {
                    _getEntryMethod.Invoke(null, new object[] { i, logEntryInstance });

                    int mode = (int)_modeField.GetValue(logEntryInstance);
                    string message = (string)_messageField.GetValue(logEntryInstance) ?? "";

                    // For compiler diagnostics, trust message content over mode bits.
                    // Unity can set both error and warning mode bits on compiler output,
                    // causing warnings to be misclassified as errors by mode bits alone.
                    LogType type = InferTypeFromMessage(message) ?? GetLogTypeFromMode(mode);

                    // Extract first line only
                    int newlineIdx = message.IndexOf('\n');
                    if (newlineIdx > 0)
                        message = message.Substring(0, newlineIdx);

                    _buffer.Add(type, message, null);
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[LogCaptureService] Backfill failed: {e.Message}");
            }
            finally
            {
                try { _endGettingEntriesMethod?.Invoke(null, null); }
                catch { /* ignore */ }
            }
        }

        /// <summary>
        /// For compiler diagnostics (e.g. "error CS0246", "warning CS0414"),
        /// the message content is more reliable than mode bits.
        /// Returns null if the message doesn't match a known compiler pattern.
        /// </summary>
        private static LogType? InferTypeFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            // Compiler diagnostics: "warning CSxxxx" / "error CSxxxx"
            if (message.IndexOf(": warning CS", StringComparison.Ordinal) >= 0 ||
                message.IndexOf(": warning ", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Warning;
            if (message.IndexOf(": error CS", StringComparison.Ordinal) >= 0 ||
                message.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) >= 0)
                return LogType.Error;

            return null;
        }

        private static LogType GetLogTypeFromMode(int mode)
        {
            if ((mode & (ModeBitException | ModeBitScriptingException)) != 0) return LogType.Exception;
            if ((mode & (ModeBitError | ModeBitScriptingError)) != 0) return LogType.Error;
            if ((mode & (ModeBitAssert | ModeBitScriptingAssertion)) != 0) return LogType.Assert;
            if ((mode & (ModeBitWarning | ModeBitScriptingWarning)) != 0) return LogType.Warning;
            return LogType.Log;
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            // Extract just the first line for the message (stack trace is separate)
            string firstLine = message;
            int newlineIdx = message.IndexOf('\n');
            if (newlineIdx > 0)
            {
                firstLine = message.Substring(0, newlineIdx);
            }

            _buffer?.Add(type, firstLine, stackTrace);
        }

        public static LogBuffer Buffer
        {
            get
            {
                Initialize();
                return _buffer;
            }
        }

        public static void Clear()
        {
            _buffer?.Clear();
        }

        public static List<LogBuffer.LogEntry> Query(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterRegex = null,
            int? count = null)
        {
            Initialize();
            return _buffer.Query(types, sinceSequenceId, sinceTimestamp, filterRegex, count);
        }

        public static LogBuffer.PagedResult QueryPaged(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterRegex = null,
            int pageSize = 50,
            int cursor = 0)
        {
            Initialize();
            return _buffer.QueryPaged(types, sinceSequenceId, sinceTimestamp, filterRegex, pageSize, cursor);
        }

        public static LogBuffer.BufferStats GetStats(string filterRegex = null)
        {
            Initialize();
            return _buffer.GetStats(filterRegex);
        }

        public static long LatestSequenceId
        {
            get
            {
                Initialize();
                return _buffer.LatestSequenceId;
            }
        }
    }
}
