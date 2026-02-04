using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Singleton service that captures Unity log messages into a ring buffer.
    /// Auto-initializes on domain load via InitializeOnLoad.
    /// </summary>
    [InitializeOnLoad]
    public static class LogCaptureService
    {
        private static LogBuffer _buffer;
        private static bool _isSubscribed = false;
        private static readonly object _initLock = new object();

        public const int DefaultCapacity = 10000;

        static LogCaptureService()
        {
            Initialize();
        }

        public static void Initialize(int capacity = DefaultCapacity)
        {
            lock (_initLock)
            {
                if (_buffer == null)
                {
                    _buffer = new LogBuffer(capacity);
                }

                if (!_isSubscribed)
                {
                    Application.logMessageReceived += OnLogMessageReceived;
                    _isSubscribed = true;
                }
            }
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
            string filterText = null,
            string filterRegex = null,
            int? count = null)
        {
            Initialize();
            return _buffer.Query(types, sinceSequenceId, sinceTimestamp, filterText, filterRegex, count);
        }

        public static LogBuffer.PagedResult QueryPaged(
            LogType[] types = null,
            long? sinceSequenceId = null,
            DateTime? sinceTimestamp = null,
            string filterText = null,
            string filterRegex = null,
            int pageSize = 50,
            int cursor = 0)
        {
            Initialize();
            return _buffer.QueryPaged(types, sinceSequenceId, sinceTimestamp, filterText, filterRegex, pageSize, cursor);
        }

        public static LogBuffer.BufferStats GetStats(string filterText = null, string filterRegex = null)
        {
            Initialize();
            return _buffer.GetStats(filterText, filterRegex);
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
