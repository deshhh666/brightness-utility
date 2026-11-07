using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BrightnessUtility
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                var report = new StringBuilder("Brightness diagnostics\r\n" + Environment.OSVersion + "\r\n");
                using (var monitors = MonitorCollection.Find())
                {
                    report.AppendLine(monitors.Notes);
                    foreach (var m in monitors.Items)
                        report.AppendLine(m.Name + " | " + m.Protocol + " | " + m.Current + "% | " + (m.Supported ? "supported" : "unavailable"));
                }
                File.WriteAllText(args.Length > 1 ? args[1] : "diagnostics.txt", report.ToString(), Encoding.UTF8);
                return;
            }
            bool smoke = args.Length > 0 && args[0] == "--smoke-test";
            bool tray = args.Length > 0 && args[0] == "--tray";
            bool created;
            using (var singleton = new Mutex(true, "Local\\BrightnessUtility.Desktop.v2" + (smoke ? ".Test" : ""), out created))
            {
                if (!created) { Native.PostMessage(new IntPtr(0xffff), Native.ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                try { Application.Run(new MainForm(smoke, tray)); }
                finally { singleton.ReleaseMutex(); }
            }
        }
    }

    internal static class Native
    {
        internal static readonly uint ShowMessage = RegisterWindowMessage("BrightnessUtility.Show.v2");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        internal delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int type, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo
        {
            internal int Size;
            internal int Left, Top, Right, Bottom, WorkLeft, WorkTop, WorkRight, WorkBottom;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice
        {
            internal int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string name, uint index, ref DisplayDevice device, uint flags);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PhysicalMonitor
        {
            internal IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
        }
        internal delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, MonitorEnum callback, IntPtr data);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
    }

    internal sealed class MonitorDevice : IDisposable
    {
        internal string Name, WmiPath, Id;
        internal IntPtr Handle;
        internal uint Min, Max;
        internal bool Supported, Vcp;
        internal int Current;
        internal string Protocol { get { return WmiPath != null ? "Windows / WMI" : "DDC/CI"; } }
        internal void Set(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (!Supported) throw new InvalidOperationException("Монитор не поддерживает управление яркостью.");
            if (WmiPath != null)
            {
                using (var method = new ManagementObject(WmiPath))
                using (var input = method.GetMethodParameters("WmiSetBrightness"))
                {
                    input["Timeout"] = (uint)0;
                    input["Brightness"] = (byte)percent;
                    using (var output = method.InvokeMethod("WmiSetBrightness", input, null))
                        if (output == null || Convert.ToUInt32(output["ReturnValue"]) != 0)
                            throw new InvalidOperationException("Windows не удалось установить яркость.");
                }
            }
            else
            {
                uint raw = Min + (uint)Math.Round((Max - Min) * percent / 100.0);
                bool ok = false;
                int error = 0;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    ok = Vcp ? Native.SetVCPFeature(Handle, 0x10, raw) : Native.SetMonitorBrightness(Handle, raw);
                    if (ok) break;
                    error = Marshal.GetLastWin32Error();
                    Thread.Sleep(80);
                }
                if (!ok) throw new Win32Exception(error, "Монитор не принял яркость. Проверьте DDC/CI и подключение; затем нажмите «Обновить».");
            }
            Current = percent;
        }
        public void Dispose() { if (Handle != IntPtr.Zero) { Native.DestroyPhysicalMonitor(Handle); Handle = IntPtr.Zero; } }
    }

    internal sealed class MonitorCollection : IDisposable
    {
        internal readonly List<MonitorDevice> Items = new List<MonitorDevice>();
        internal string Notes = "";
        internal static MonitorCollection Find()
        {
            var result = new MonitorCollection();
            try
            {
                using (var search = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active=True"))
                using (var found = search.Get())
                    foreach (ManagementObject item in found)
                    using (item)
                    {
                        string instance = Convert.ToString(item["InstanceName"]);
                        string escaped = instance.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string path = "\\\\.\\root\\wmi:WmiMonitorBrightnessMethods.InstanceName=\"" + escaped + "\"";
                        result.Items.Add(new MonitorDevice { Name = "Встроенный экран " + (result.Items.Count + 1), Id = "wmi:" + instance, WmiPath = path, Supported = true, Current = Convert.ToInt32(item["CurrentBrightness"]), Max = 100 });
                    }
            }
            catch (Exception ex) { result.Notes += "WMI: " + ex.Message + "\r\n"; }
            try
            {
                Native.MonitorEnum callback = delegate(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data)
                {
                    uint count;
                    if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0) return true;
                    var physical = new Native.PhysicalMonitor[count];
                    if (!Native.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
                    var info = new Native.MonitorInfo { Size = Marshal.SizeOf(typeof(Native.MonitorInfo)) };
                    Native.GetMonitorInfo(monitor, ref info);
                    int physicalIndex = 0;
                    foreach (var p in physical)
                    {
                        var display = new Native.DisplayDevice { Size = Marshal.SizeOf(typeof(Native.DisplayDevice)) };
                        Native.EnumDisplayDevices(info.Device, (uint)physicalIndex, ref display, 0);
                        string identity = String.IsNullOrEmpty(display.Id) ? info.Device : display.Id;
                        var device = new MonitorDevice { Name = p.Description, Handle = p.Handle, Id = "ddc:" + identity + ":" + physicalIndex++ };
                        uint current, type;
                        device.Supported = Native.GetMonitorBrightness(p.Handle, out device.Min, out current, out device.Max) && device.Max > device.Min;
                        if (!device.Supported)
                        {
                            device.Min = 0;
                            device.Supported = Native.GetVCPFeatureAndVCPFeatureReply(p.Handle, 0x10, out type, out current, out device.Max) && device.Max > 0;
                            device.Vcp = device.Supported;
                        }
                        device.Current = device.Supported ? (int)Math.Max(0, Math.Min(100, Math.Round(((double)current - device.Min) * 100 / (device.Max - device.Min)))) : 0;
                        string panelIdentity = (display.Id ?? "").Replace("MONITOR\\", "DISPLAY\\");
                        if (panelIdentity.Length > 0 && result.Items.Exists(x => x.WmiPath != null && x.Id.StartsWith("wmi:" + panelIdentity, StringComparison.OrdinalIgnoreCase))) { device.Dispose(); continue; }
                        result.Items.Add(device);
                    }
                    return true;
                };
                if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                    result.Notes += "Windows не удалось перечислить экраны.\r\n";
            }
            catch (Exception ex) { result.Notes += "DDC/CI: " + ex.Message + "\r\n"; }
            return result;
        }
        public void Dispose() { foreach (var item in Items) item.Dispose(); }
    }

    internal sealed class BrightnessSlider : Control
    {
        private int wheel, current, min, max = 100;
        private float dpiScale = 1;
        internal bool Dark = true;
        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int SmallChange { get; set; }
        public int LargeChange { get; set; }
        public TickStyle TickStyle { get; set; }
        public int Value
        {
            get { return current; }
            set { int next = Math.Max(min, Math.Min(max, value)); if (current == next) return; current = next; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }
        public event EventHandler ValueChanged;
        internal BrightnessSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand; Height = 36; SmallChange = 1; LargeChange = 10; AccessibleRole = AccessibleRole.Slider;
        }
        private float PixelScale { get { return dpiScale; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); using (var graphics = CreateGraphics()) dpiScale = graphics.DpiX / 96f; }
        private void MoveTo(int x) { Value = min + (int)Math.Round((x - 36 * PixelScale) / Math.Max(1, Width - 72 * PixelScale) * (max - min)); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) { Focus(); Capture = true; MoveTo(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Capture && Enabled) MoveTo(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Capture = false; base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys keyData) { Keys key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= SmallChange; break;
                case Keys.Right: case Keys.Up: Value += SmallChange; break;
                case Keys.PageUp: Value += LargeChange; break;
                case Keys.PageDown: Value -= LargeChange; break;
                case Keys.Home: Value = min; break;
                case Keys.End: Value = max; break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = e.SuppressKeyPress = true; base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var c = new Palette(Dark); float s = PixelScale, y = Height / 2f, left = 36 * s, right = Width - 36 * s;
            float position = left + (right - left) * (Value - min) / Math.Max(1f, max - min);
            Art.Glyph(g, "dim", new RectangleF(0, y - 9 * s, 18 * s, 18 * s), c.Muted);
            Art.Glyph(g, "sun", new RectangleF(Width - 18 * s, y - 9 * s, 18 * s, 18 * s), c.Muted);
            using (var rail = new Pen(c.Rail, 4 * s) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round }) g.DrawLine(rail, left, y, right, y);
            if (Enabled && position > left) using (var fill = new Pen(c.Accent, 4 * s) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round }) g.DrawLine(fill, left, y, position, y);
            using (var thumb = new SolidBrush(Enabled ? c.Accent : c.Muted)) g.FillEllipse(thumb, position - 7 * s, y - 7 * s, 14 * s, 14 * s);
            using (var center = new SolidBrush(BackColor)) g.FillEllipse(center, position - 2 * s, y - 2 * s, 4 * s, 4 * s);
            if (Focused && ShowFocusCues) using (var pen = new Pen(c.Secondary, s)) g.DrawEllipse(pen, position - 11 * s, y - 11 * s, 22 * s, 22 * s);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibleObject(this); }
        private sealed class SliderAccessibleObject : ControlAccessibleObject
        {
            private readonly BrightnessSlider owner;
            internal SliderAccessibleObject(BrightnessSlider control) : base(control) { owner = control; }
            public override string Value { get { return owner.Value + "%"; } set { int parsed; if (Int32.TryParse(value.TrimEnd('%'), out parsed)) owner.Value = parsed; } }
            public override AccessibleRole Role { get { return AccessibleRole.Slider; } }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            wheel += e.Delta;
            int steps = wheel / 120;
            wheel %= 120;
            Value = Math.Max(Minimum, Math.Min(Maximum, Value + steps * 5));
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }
    }

    internal sealed class BrightnessCard : SurfacePanel
    {
        private readonly MonitorDevice device;
        private readonly BrightnessSlider slider;
        private readonly Label title, value, status;
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        private readonly SemaphoreSlim gate;
        private readonly ToolTip cardTips = new ToolTip();
        private bool writing, closing, dark, hasError;
        private int revision;
        private bool programmatic;
        private readonly DesignButton rename;
        private readonly Control monitorMark;
        internal event Action<BrightnessCard> UserValueChanged;
        internal event Action<BrightnessCard> RenameRequested;
        internal MonitorDevice Device { get { return device; } }
        internal int Target { get { return slider.Value; } }
        internal bool Idle { get { return !writing && !debounce.Enabled; } }
        internal void SetTarget(int percent) { if (closing || !device.Supported) return; programmatic = true; try { slider.Value = Math.Max(0, Math.Min(100, percent)); } finally { programmatic = false; } }
        internal void SetName(string name) { title.Text = name; slider.AccessibleName = "Яркость: " + name; }
        internal BrightnessCard(MonitorDevice monitor, SemaphoreSlim ioGate)
        {
            device = monitor; gate = ioGate;
            Width = 552; Height = 128; Margin = new Padding(0, 0, 0, 12);
            title = new Label { Text = monitor.Name, Location = new Point(52, 16), Size = new Size(296, 28), Font = new Font("Segoe UI", 10.5f), AutoEllipsis = true, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            rename = new DesignButton { Text = "", Glyph = "edit", Quiet = true, Location = new Point(352, 12), Size = new Size(32, 32), AccessibleName = "Переименовать монитор" };
            rename.Click += delegate { if (RenameRequested != null) RenameRequested(this); };
            cardTips.SetToolTip(rename, "Переименовать монитор");
            title.DoubleClick += delegate { if (RenameRequested != null) RenameRequested(this); };
            value = new Label { Text = monitor.Supported ? monitor.Current + "%" : "—", Location = new Point(408, 12), Size = new Size(124, 36), TextAlign = ContentAlignment.MiddleRight, Font = new Font("Consolas", 22), BackColor = Color.Transparent };
            slider = new BrightnessSlider { Minimum = 0, Maximum = 100, Value = monitor.Current, SmallChange = 1, LargeChange = 10, Location = new Point(20, 52), Size = new Size(512, 32), Enabled = monitor.Supported, AccessibleName = "Яркость: " + monitor.Name };
            status = new Label { Text = monitor.Supported ? "Аппаратная яркость" : "Включите DDC/CI в меню монитора", Location = new Point(20, 96), Size = new Size(512, 24), Font = new Font("Segoe UI", 9), BackColor = Color.Transparent, AutoEllipsis = true };
            monitorMark = new Panel { Location = new Point(20, 20), Size = new Size(20, 20), BackColor = Color.Transparent };
            monitorMark.Paint += delegate(object sender, PaintEventArgs e) { Art.Glyph(e.Graphics, "monitor", new RectangleF(0, 0, 18, 18), new Palette(dark).Secondary); };
            Controls.AddRange(new Control[] { title, value, slider, status, rename, monitorMark });
            cardTips.SetToolTip(status, monitor.Supported ? "Управление монитором через " + monitor.Protocol : "Включите DDC/CI в меню монитора и нажмите кнопку обновления");
            debounce.Interval = 20;
            // Throttle rather than debounce: continuous dragging must keep sending updates.
            slider.ValueChanged += delegate { value.Text = slider.Value + "%"; revision++; if (!writing && !closing && !debounce.Enabled) debounce.Start(); if (!programmatic && UserValueChanged != null) UserValueChanged(this); };
            debounce.Tick += async delegate { debounce.Stop(); await ApplyAsync(); };
        }
        private async Task ApplyAsync()
        {
            if (writing || closing) return;
            writing = true;
            try
            {
                while (!closing)
                {
                    int version = revision;
                    try
                    {
                        await gate.WaitAsync();
                        try
                        {
                            if (closing) return;
                            // Read the latest value after waiting; never enqueue stale positions.
                            int requested = slider.Value;
                            version = revision;
                            if (device.Current != requested)
                            {
                                await Task.Run(() => device.Set(requested));
                            }
                            if (!closing) { hasError = false; status.Text = "Аппаратная яркость"; UpdateStatusColor(); }
                        }
                        finally { gate.Release(); }
                    }
                    catch (Exception ex)
                    {
                        if (!closing) { hasError = true; status.Text = ex.Message; cardTips.SetToolTip(status, ex.Message); UpdateStatusColor(); }
                    }
                    // A newer position is sent immediately after the previous hardware call.
                    if (version == revision) break;
                }
            }
            finally { writing = false; }
        }
        internal void ApplyTheme(bool isDark)
        {
            dark = isDark;
            SetTheme(dark); var c = new Palette(dark);
            title.ForeColor = c.Text;
            value.ForeColor = c.Text;
            slider.BackColor = c.Surface; slider.Dark = dark; slider.Invalidate();
            rename.Dark = dark; rename.BackColor = c.Surface; rename.Invalidate(); monitorMark.Invalidate();
            UpdateStatusColor();
        }
        private void UpdateStatusColor() { status.ForeColor = hasError ? (dark ? Color.FromArgb(255, 150, 150) : Color.FromArgb(170, 48, 48)) : new Palette(dark).Muted; }
        internal void Stop() { closing = true; debounce.Stop(); }
        protected override void Dispose(bool disposing) { if (disposing) { Stop(); debounce.Dispose(); cardTips.Dispose(); } base.Dispose(disposing); }
    }

    internal static class ThemeSettings
    {
        internal static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrightnessUtility", "theme.txt");
        internal static bool Load(string path)
        {
            try { return !String.Equals(File.ReadAllText(path).Trim(), "light", StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
        internal static void Save(string path, bool dark)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, dark ? "dark" : "light");
        }
    }

}
