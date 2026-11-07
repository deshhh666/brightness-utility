using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightnessUtility;

internal static class InteractionCheck
{
    private static int result = 1;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static uint Read(MonitorDevice monitor)
    {
        uint min, current, max, type;
        bool ok = monitor.Vcp
            ? Native.GetVCPFeatureAndVCPFeatureReply(monitor.Handle, 0x10, out type, out current, out max)
            : Native.GetMonitorBrightness(monitor.Handle, out min, out current, out max);
        Require(ok, "Brightness read failed");
        return current;
    }
    private static async Task WaitIdle(BrightnessCard card)
    {
        var clock = Stopwatch.StartNew();
        var timer = (System.Windows.Forms.Timer)typeof(BrightnessCard).GetField("debounce", Fields).GetValue(card);
        do
        {
            await Task.Delay(10);
            if (!timer.Enabled && !(bool)typeof(BrightnessCard).GetField("writing", Fields).GetValue(card)) return;
        } while (clock.ElapsedMilliseconds < 5000);
        throw new Exception("Slider update timed out");
    }
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        var form = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(560, 220) };
        form.Shown += async delegate
        {
            try
            {
                string settingsPath = Path.Combine(Path.GetTempPath(), "BrightnessTheme-" + Guid.NewGuid(), "theme.txt");
                try
                {
                    Require(ThemeSettings.Load(settingsPath), "Default theme must be dark");
                    ThemeSettings.Save(settingsPath, false);
                    Require(!ThemeSettings.Load(settingsPath), "Light theme did not persist");
                    ThemeSettings.Save(settingsPath, true);
                    Require(ThemeSettings.Load(settingsPath), "Dark theme did not persist");
                    Console.WriteLine("PASS: default dark theme and persistence in both directions");
                }
                finally { if (File.Exists(settingsPath)) File.Delete(settingsPath); if (Directory.Exists(Path.GetDirectoryName(settingsPath))) Directory.Delete(Path.GetDirectoryName(settingsPath)); }
                using (var monitors = await Task.Run(() => MonitorCollection.Find()))
                {
                    int tested = 0;
                    foreach (var monitor in monitors.Items)
                    {
                        if (!monitor.Supported || monitor.WmiPath != null || monitor.Max - monitor.Min < 100) continue;
                        var gate = new SemaphoreSlim(1, 1);
                        using (var card = new BrightnessCard(monitor, gate))
                        {
                            form.Controls.Add(card);
                            var slider = (BrightnessSlider)typeof(BrightnessCard).GetField("slider", Fields).GetValue(card);
                            card.ApplyTheme(true);
                            Require(card.Colors.Surface == slider.BackColor && slider.BackColor != Color.White, "Slider background must follow dark theme");
                            uint original = Read(monitor);
                            int first = monitor.Current >= 2 ? monitor.Current - 1 : monitor.Current + 1;
                            int second = monitor.Current >= 2 ? monitor.Current - 2 : monitor.Current + 2;
                            bool changedDuringDrag = false;
                            var clock = Stopwatch.StartNew();
                            long firstChange = -1;
                            Exception failure = null;
                            try
                            {
                                for (int step = 0; step < 24; step++)
                                {
                                    slider.Value = step % 2 == 0 ? first : second;
                                    await Task.Delay(15);
                                    if (monitor.Current == first || monitor.Current == second)
                                    {
                                        changedDuringDrag = true;
                                        if (firstChange < 0) firstChange = clock.ElapsedMilliseconds;
                                    }
                                }
                                Require(changedDuringDrag, "No update during continuous dragging");
                                slider.Value = first;
                                await WaitIdle(card);
                                uint expected = monitor.Min + (uint)Math.Round((monitor.Max - monitor.Min) * first / 100.0);
                                Require(Read(monitor) == expected, "Last slider position was not applied");
                                card.ApplyTheme(false);
                                Require(card.Colors.Surface.ToArgb() == Color.White.ToArgb() && slider.BackColor.ToArgb() == Color.White.ToArgb(), "Light theme failed");
                                Console.WriteLine("PASS: continuous dragging + final hardware value; first completed update in " + firstChange + " ms");
                                tested++;
                            }
                            catch (Exception ex) { failure = ex; }
                            card.Stop();
                            await gate.WaitAsync();
                            try
                            {
                                bool restored = monitor.Vcp ? Native.SetVCPFeature(monitor.Handle, 0x10, original) : Native.SetMonitorBrightness(monitor.Handle, original);
                                await Task.Delay(200);
                                Require(restored && Read(monitor) == original, "Original brightness was not restored");
                                Console.WriteLine("RESTORED: " + original);
                            }
                            finally { gate.Release(); }
                            if (failure != null) throw failure;
                        }
                    }
                    Require(tested > 0, "No suitable DDC/CI monitor found for interaction check");
                    Console.WriteLine("Monitors tested: " + tested);
                }
                result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
        form.Dispose();
        return result;
    }
}
