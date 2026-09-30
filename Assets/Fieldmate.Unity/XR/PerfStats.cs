using System.Globalization;

namespace Fieldmate.XR;

/// <summary>One sample of the numbers the perf budget (design.md §6) is measured in, and their two text forms.</summary>
public readonly struct PerfStats
{
    public PerfStats(float frameMs, float cpuMs, float gpuMs, long drawCalls, long batches, long setPassCalls, long triangles, long systemMemoryBytes, long gcReservedBytes)
    {
        FrameMs = frameMs;
        CpuMs = cpuMs;
        GpuMs = gpuMs;
        DrawCalls = drawCalls;
        Batches = batches;
        SetPassCalls = setPassCalls;
        Triangles = triangles;
        SystemMemoryBytes = systemMemoryBytes;
        GcReservedBytes = gcReservedBytes;
    }

    public float FrameMs { get; }
    public float CpuMs { get; }
    public float GpuMs { get; }
    public long DrawCalls { get; }
    public long Batches { get; }
    public long SetPassCalls { get; }
    public long Triangles { get; }
    public long SystemMemoryBytes { get; }
    public long GcReservedBytes { get; }

    public float Fps => FrameMs > 0f ? 1000f / FrameMs : 0f;

    /// <summary>Three short lines for the in-app overlay.</summary>
    public string Overlay()
    {
        var c = CultureInfo.InvariantCulture;
        var gpu = GpuMs > 0f ? GpuMs.ToString("0.0", c) : "–";
        return string.Format(c, "{0:0} fps · {1:0.0} ms  (cpu {2:0.0} / gpu {3})\nDraws {4} · Batches {5} · SetPass {6} · Tris {7:0.#}k\nMem {8:0} MB · GC {9:0} MB",
            Fps, FrameMs, CpuMs, gpu, DrawCalls, Batches, SetPassCalls, Triangles / 1000f, SystemMemoryBytes / 1048576f, GcReservedBytes / 1048576f);
    }

    /// <summary>One logcat line, grep-able as "[Perf] stats", for docs/perf.</summary>
    public string LogLine()
    {
        var c = CultureInfo.InvariantCulture;
        return string.Format(c, "[Perf] stats frame {0:0.0} ms draws {1} batches {2} setpass {3} tris {4} mem {5:0} MB gc {6:0} MB",
            FrameMs, DrawCalls, Batches, SetPassCalls, Triangles, SystemMemoryBytes / 1048576f, GcReservedBytes / 1048576f);
    }
}
