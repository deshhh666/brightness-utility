using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using BrightnessUtility;

internal static class FeaturesCheck
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object obj, string name) { return (T)obj.GetType().GetField(name, Flags).GetValue(obj); }
    private static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, Flags).Invoke(obj, args); }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static async Task Until(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done()) { if (clock.ElapsedMilliseconds > 10000) throw new Exception("Timed out"); await Task.Delay(20); }
    }
    private static uint Read(MonitorDevice monitor)
    {
        uint min, current, max, type;
        bool ok = monitor.Vcp ? Native.GetVCPFeatureAndVCPFeatureReply(monitor.Handle, 0x10, out type, out current, out max) : Native.GetMonitorBrightness(monitor.Handle, out min, out current, out max);
        Require(ok, "Cannot read brightness"); return current;
    }
    private static List<BrightnessCard> Cards(MainForm form) { return (List<BrightnessCard>)Call(form, "SupportedCards"); }
    private static async Task Idle(MainForm form)
    {
        await Until(() => { foreach (var card in Cards(form)) if (!card.Idle) return false; return true; });
    }
    private static void CheckHardware(MainForm form)
    {
        foreach (var card in Cards(form))
        {
            if (card.Device.WmiPath != null) continue;
            uint expected = card.Device.Min + (uint)Math.Round((card.Device.Max - card.Device.Min) * card.Target / 100.0);
            Require(Read(card.Device) == expected, "Hardware did not match target " + card.Target);
        }
    }
    private static void CheckAutostart()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Run";
        object previous = null; RegistryValueKind kind = RegistryValueKind.String;
        using (var key = Registry.CurrentUser.OpenSubKey(path))
        {
            if (key != null) { previous = key.GetValue("BrightnessUtility", null, RegistryValueOptions.DoNotExpandEnvironmentNames); if (previous != null) kind = key.GetValueKind("BrightnessUtility"); }
        }
        try
        {
            Autostart.Set(true); Require(Autostart.Enabled, "Autostart did not enable");
            Require(Autostart.Command.StartsWith("\"") && Autostart.Command.EndsWith("\" --tray"), "Executable path must be quoted");
            Autostart.Set(false); Require(!Autostart.Enabled, "Autostart did not disable");
            Console.WriteLine("PASS: autostart enable/disable, quoted command with --tray");
        }
        finally
        {
            using (var key = Registry.CurrentUser.CreateSubKey(path))
                if (previous == null) key.DeleteValue("BrightnessUtility", false); else key.SetValue("BrightnessUtility", previous, kind);
            Console.WriteLine("RESTORED: previous autostart state");
        }
    }
    private static void CheckSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BrightnessFeatures-" + Guid.NewGuid());
        string path = Path.Combine(directory, "settings.xml");
        try
        {
            var saved = new AppSettings(path);
            saved.Values["name:monitor1"] = "Левый & <основной>";
            saved.Values["name:monitor2"] = "Правый";
            saved.Values["preset:Ночь:monitor1"] = "27";
            saved.Values["preset:Ночь:monitor2"] = "43";
            saved.Save(); saved.Values["linked"] = "true"; saved.Save();
            var loaded = new AppSettings(path);
            Require(loaded.Get("name:monitor1", "") == "Левый & <основной>", "Name persistence failed");
            Require(loaded.Preset("Ночь", "monitor1") == 27 && loaded.Preset("Ночь", "monitor2") == 43, "Per-monitor preset persistence failed");
            Require(loaded.Preset("День", "new-monitor") == 80, "New monitor default preset failed");
            Console.WriteLine("PASS: names, per-monitor presets, XML escaping, atomic settings replacement");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        int result = 1;
        var form = new MainForm(true, false, false);
        form.Opacity = 0; form.ShowInTaskbar = false;
        form.Shown += async delegate
        {
            var original = new Dictionary<string, uint>();
            try
            {
                CheckSettings(); CheckAutostart();
                await Until(() => Field<MonitorCollection>(form, "monitors") != null && !Field<bool>(form, "loading"));
                var list = Cards(form); Require(list.Count >= 2, "Two monitors required for this integration check");
                foreach (var card in list) { Require(!String.IsNullOrEmpty(card.Device.Id) && !original.ContainsKey(card.Device.Id), "Monitor IDs must be unique"); if (card.Device.WmiPath == null) original[card.Device.Id] = Read(card.Device); }
                Field<CheckBox>(form, "linked").Checked = true;
                Field<BrightnessSlider>(list[0], "slider").Value = 99;
                await Idle(form);
                foreach (var card in Cards(form)) Require(card.Target == 99, "Linked slider did not propagate");
                CheckHardware(form); Console.WriteLine("PASS: linked sliders and hardware readback");

                // A preset can contain different values even with linking enabled.
                list[0].SetTarget(98); list[1].SetTarget(99); await Idle(form);
                Call(form, "SavePreset", "Ночь"); Call(form, "SetAll", 100); await Idle(form);
                Call(form, "ApplyPreset", "Ночь"); await Idle(form);
                Require(list[0].Target == 98 && list[1].Target == 99, "Preset values lost per-monitor settings");
                Call(form, "ShowPopup"); await Task.Delay(250);
                Require(Field<Form>(form, "popup") != null, "Tray popup missing");
                Require(list[0].Target == 98 && list[1].Target == 99, "Popup synchronization overwrote preset values");
                CheckHardware(form); Call(form, "ClosePopup"); Console.WriteLine("PASS: saved preset + tray popup preserves individual values");

                Call(form, "SetAll", 100); await Idle(form);
                var slider = Field<BrightnessSlider>(list[0], "slider");
                Call(slider, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, -120));
                await Idle(form); foreach (var card in Cards(form)) Require(card.Target == 95, "Wheel must adjust by 5%"); CheckHardware(form);
                Call(form, "EnableIntegration");
                Require(Field<IntPtr>(form, "mouseHook") != IntPtr.Zero, "Global wheel hook failed to register");
                Require(Field<bool>(form, "hotUp") && Field<bool>(form, "hotDown"), "Hotkeys failed to register");
                var message = Message.Create(form.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                Call(form, "WndProc", message); await Idle(form);
                foreach (var card in Cards(form)) Require(card.Target == 100, "Hotkey did not adjust by 5%"); CheckHardware(form);
                Console.WriteLine("PASS: wheel +5/-5, global hook/hotkey registration and hotkey dispatch");

                var settings = Field<AppSettings>(form, "settings");
                string firstId = list[0].Device.Id; settings.Values["name:" + firstId] = "Основной";
                var before = Field<MonitorCollection>(form, "monitors");
                Call(form, "PowerChanged", null, new PowerModeChangedEventArgs(PowerModes.Resume));
                await Until(() => !Object.ReferenceEquals(before, Field<MonitorCollection>(form, "monitors")) && !Field<bool>(form, "loading"));
                bool found = false;
                foreach (var card in Cards(form)) if (card.Device.Id == firstId) { found = true; Require(Field<Label>(card, "title").Text == "Основной", "Name did not survive rediscovery"); }
                Require(found, "Monitor ID changed after rediscovery");
                CheckHardware(form); Console.WriteLine("PASS: resume event triggers rediscovery; stable IDs and names retained");
                result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); }
            // Restore exact hardware values even when an assertion failed.
            try
            {
                await Until(() => !Field<bool>(form, "loading"));
                Field<System.Windows.Forms.Timer>(form, "reconnect").Stop();
                foreach (var card in Cards(form)) card.Stop();
                var gate = Field<System.Threading.SemaphoreSlim>(form, "gate"); await gate.WaitAsync();
                try
                {
                    foreach (var card in Cards(form))
                    {
                        uint raw; if (!original.TryGetValue(card.Device.Id, out raw)) continue;
                        bool ok = card.Device.Vcp ? Native.SetVCPFeature(card.Device.Handle, 0x10, raw) : Native.SetMonitorBrightness(card.Device.Handle, raw);
                        await Task.Delay(150); Require(ok && Read(card.Device) == raw, "Failed to restore brightness");
                    }
                    Console.WriteLine("RESTORED: all original monitor brightness values");
                }
                finally { gate.Release(); }
            }
            catch (Exception ex) { result = 1; Console.WriteLine("RESTORE FAILED: " + ex); }
            Call(form, "RequestExit");
        };
        Application.Run(form); form.Dispose(); return result;
    }
}
