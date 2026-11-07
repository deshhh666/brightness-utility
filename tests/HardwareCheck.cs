using System;
using System.Threading;
using BrightnessUtility;

internal static class HardwareCheck
{
    private static uint Read(MonitorDevice monitor)
    {
        uint min, current, max, type;
        bool ok = monitor.Vcp
            ? Native.GetVCPFeatureAndVCPFeatureReply(monitor.Handle, 0x10, out type, out current, out max)
            : Native.GetMonitorBrightness(monitor.Handle, out min, out current, out max);
        if (!ok) throw new Exception("Cannot read back brightness.");
        return current;
    }
    private static int Main()
    {
        int tested = 0;
        using (var monitors = MonitorCollection.Find())
        {
            foreach (var monitor in monitors.Items)
            {
                if (!monitor.Supported || monitor.WmiPath != null) continue;
                uint original = Read(monitor);
                int percent = monitor.Current;
                int target = percent > 1 ? percent - 1 : percent + 1;
                uint expected = monitor.Min + (uint)Math.Round((monitor.Max - monitor.Min) * target / 100.0);
                try
                {
                    monitor.Set(target);
                    Thread.Sleep(250);
                    uint actual = Read(monitor);
                    if (actual != expected) throw new Exception("Readback mismatch: " + actual + " != " + expected);
                    Console.WriteLine("PASS: " + monitor.Name + " " + original + " -> " + actual);
                    tested++;
                }
                finally
                {
                    bool restored = monitor.Vcp ? Native.SetVCPFeature(monitor.Handle, 0x10, original) : Native.SetMonitorBrightness(monitor.Handle, original);
                    Thread.Sleep(250);
                    if (!restored || Read(monitor) != original) throw new Exception("Failed to restore original brightness.");
                    Console.WriteLine("RESTORED: " + original);
                }
            }
        }
        Console.WriteLine("Hardware checks passed: " + tested);
        return tested > 0 ? 0 : 2;
    }
}
