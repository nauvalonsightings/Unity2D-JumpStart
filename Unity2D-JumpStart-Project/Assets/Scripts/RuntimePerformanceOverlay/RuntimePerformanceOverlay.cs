using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Profiling;

namespace Unity2DJumpStart
{
    public sealed class RuntimePerformanceOverlay : MonoBehaviour
    {
        [Header("UI Targets")]
        [SerializeField] private Text fpsText;
        [SerializeField] private Text vsyncText;
        [SerializeField] private Text memoryText;

        [Header("Update")]
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;

        private float _nextRefreshTime;
        private float _smoothedDeltaTime = 0.016666667f;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private static readonly MethodInfo ProcessWorkingSetMethod = typeof(Process).GetProperty("WorkingSet64", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
        private static readonly MethodInfo ProcessPrivateMemoryMethod = typeof(Process).GetProperty("PrivateMemorySize64", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();

        private void OnEnable()
        {
            _stopwatch.Reset();
            _stopwatch.Start();
            _nextRefreshTime = 0f;
            RefreshNow();
        }

        private void OnDisable()
        {
            _stopwatch.Stop();
        }

        private void Update()
        {
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            _smoothedDeltaTime += (unscaledDeltaTime - _smoothedDeltaTime) * 0.1f;

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshInterval;
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (fpsText != null)
            {
                fpsText.text = FormatFps();
            }

            if (vsyncText != null)
            {
                vsyncText.text = FormatVSync();
            }

            if (memoryText != null)
            {
                memoryText.text = FormatMemory();
            }
        }

        private string FormatFps()
        {
            float fps = _smoothedDeltaTime > 0f ? 1f / _smoothedDeltaTime : 0f;
            return $"FPS: {fps:0.0}";
        }

        private string FormatVSync()
        {
            int vSyncCount = QualitySettings.vSyncCount;
            string state = vSyncCount <= 0 ? "Off" : $"On x{vSyncCount}";
            return $"VSync: {state} | Target FPS: {Application.targetFrameRate}";
        }

        private string FormatMemory()
        {
            long allocated = Profiler.GetTotalAllocatedMemoryLong();
            long reserved = Profiler.GetTotalReservedMemoryLong();
            long unusedReserved = Profiler.GetTotalUnusedReservedMemoryLong();
            long gcManaged = System.GC.GetTotalMemory(false);
            long workingSet = TryGetWorkingSetBytes();
            long privateBytes = TryGetPrivateBytes();

            if (workingSet > 0)
            {
                return $"Memory: {FormatBytes(workingSet)} RAM | Unity Alloc: {FormatBytes(allocated)} | GC: {FormatBytes(gcManaged)}";
            }

            if (privateBytes > 0)
            {
                return $"Memory: {FormatBytes(privateBytes)} Private | Unity Alloc: {FormatBytes(allocated)} | GC: {FormatBytes(gcManaged)}";
            }

            return $"Unity Alloc: {FormatBytes(allocated)} | Reserved: {FormatBytes(reserved)} | Unused: {FormatBytes(unusedReserved)} | GC: {FormatBytes(gcManaged)}";
        }

        private static long TryGetWorkingSetBytes()
        {
            MethodInfo getter = ProcessWorkingSetMethod;
            if (getter == null)
            {
                return 0L;
            }

            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    object value = getter.Invoke(process, null);
                    return value is long bytes ? bytes : 0L;
                }
            }
            catch
            {
                return 0L;
            }
        }

        private static long TryGetPrivateBytes()
        {
            MethodInfo getter = ProcessPrivateMemoryMethod;
            if (getter == null)
            {
                return 0L;
            }

            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    object value = getter.Invoke(process, null);
                    return value is long bytes ? bytes : 0L;
                }
            }
            catch
            {
                return 0L;
            }
        }

        private static string FormatBytes(long bytes)
        {
            double value = bytes;
            string unit = "B";

            if (value >= 1024d)
            {
                value /= 1024d;
                unit = "KB";
            }

            if (value >= 1024d)
            {
                value /= 1024d;
                unit = "MB";
            }

            if (value >= 1024d)
            {
                value /= 1024d;
                unit = "GB";
            }

            return $"{value:0.0} {unit}";
        }
    }
}
