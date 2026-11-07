using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace BrightnessUtility
{
    internal sealed class AppSettings
    {
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal readonly string PathName;
        internal AppSettings(string path)
        {
            PathName = path;
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                document.Load(path);
                foreach (XmlElement item in document.SelectNodes("/settings/item")) Values[item.GetAttribute("key")] = item.InnerText;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }
        }
        internal string Get(string key, string fallback) { string value; return Values.TryGetValue(key, out value) ? value : fallback; }
        internal int Preset(string preset, string id)
        {
            int value;
            int fallback = preset == "День" ? 80 : preset == "Вечер" ? 40 : 15;
            return Int32.TryParse(Get("preset:" + preset + ":" + id, ""), out value) ? Math.Max(0, Math.Min(100, value)) : fallback;
        }
        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            string temporary = PathName + ".tmp";
            var document = new XmlDocument();
            var root = document.CreateElement("settings"); document.AppendChild(root);
            foreach (var pair in Values)
            {
                var item = document.CreateElement("item"); item.SetAttribute("key", pair.Key); item.InnerText = pair.Value; root.AppendChild(item);
            }
            document.Save(temporary);
            if (File.Exists(PathName)) File.Replace(temporary, PathName, null); else File.Move(temporary, PathName);
        }
    }

    internal static class Autostart
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "BrightnessUtility";
        internal static string Command { get { return "\"" + Application.ExecutablePath + "\" --tray"; } }
        internal static bool Enabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && String.Equals(Convert.ToString(key.GetValue(Name)), Command, StringComparison.OrdinalIgnoreCase); }
        }
        internal static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
                if (enabled) key.SetValue(Name, Command, RegistryValueKind.String); else key.DeleteValue(Name, false);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly FlowLayoutPanel cards;
        private readonly Panel tools;
        private readonly Button refresh;
        private readonly Label summary;
        private readonly ShortcutHint shortcuts;
        private readonly CheckBox darkTheme, linked, startup;
        private readonly BrightnessSlider allSlider;
        private readonly Label allValue;
        private readonly Font sameFont = new Font("Consolas", 22), mixedFont = new Font("Segoe UI", 11);
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly System.Windows.Forms.Timer reconnect = new System.Windows.Forms.Timer();
        private readonly AppSettings settings;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly bool smokeTest, startInTray;
        private readonly ToolTip tips = new ToolTip();
        private MonitorCollection monitors;
        private Form popup;
        private bool loading, canClose, exiting, exitRequested, updatingAll, reloadAgain, systemEvents, updatingStartup;
        private bool hotUp, hotDown;
        private IntPtr mouseHook;
        private Native.MouseHook mouseCallback;
        private int wheelRemainder, reconnectAttempts;

        internal MainForm(bool smoke) : this(smoke, false) { }
        internal MainForm(bool smoke, bool minimized, bool automatedSmoke = true)
        {
            smokeTest = smoke; startInTray = minimized;
            settings = new AppSettings(Path.Combine(Path.GetDirectoryName(ThemeSettings.FilePath), smoke ? "unused-test-settings.xml" : "settings.xml"));
            if (smoke) settings.Values.Clear();
            Text = "Яркость монитора"; Font = new Font("Segoe UI", 10);
            ClientSize = new Size(600, 744); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen; Icon = Art.LoadIcon(32);
            Controls.Add(new BrandMark { Location = new Point(28, 28), Size = new Size(32, 32) });
            Controls.Add(new Label { Text = "Яркость монитора", Font = new Font("Segoe UI", 20), Location = new Point(76, 20), Size = new Size(416, 40) });
            refresh = new DesignButton { Text = "", Glyph = "refresh", Quiet = true, Location = new Point(532, 28), Size = new Size(40, 40), AccessibleName = "Обновить мониторы" };
            tips.SetToolTip(refresh, "Обновить мониторы");
            summary = new Label { Text = "Поиск экранов…", Location = new Point(76, 64), Size = new Size(496, 24), Font = new Font("Segoe UI", 9), AutoEllipsis = true };
            tools = new SurfacePanel { Location = new Point(24, 108), Size = new Size(552, 160) };
            linked = new DesignCheckBox { Text = "Все мониторы", Checked = settings.Get("linked", "false") == "true", Location = new Point(20, 16), Size = new Size(352, 28) };
            allSlider = new BrightnessSlider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, Location = new Point(20, 56), Size = new Size(512, 32), AccessibleName = "Общая яркость" };
            allValue = new Label { Text = "—", Location = new Point(408, 12), Size = new Size(124, 36), Font = sameFont, TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };
            tools.Controls.AddRange(new Control[] { linked, allSlider, allValue });
            string[] presets = { "День", "Вечер", "Ночь" };
            string[] glyphs = { "sun", "dusk", "moon" };
            for (int i = 0; i < presets.Length; i++)
            {
                string preset = presets[i];
                var apply = new DesignButton { Text = preset, Glyph = glyphs[i], Location = new Point(20 + i * 172, 104), Size = new Size(116, 36) };
                var save = new DesignButton { Text = "", Glyph = "save", Quiet = true, Location = new Point(144 + i * 172, 104), Size = new Size(32, 36), AccessibleName = "Сохранить пресет «" + preset + "»" };
                apply.Click += delegate { ApplyPreset(preset); };
                save.Click += delegate { SavePreset(preset); };
                tips.SetToolTip(save, "Сохранить текущую яркость в «" + preset + "»");
                tools.Controls.AddRange(new Control[] { apply, save });
            }
            cards = new FlowLayoutPanel { Location = new Point(24, 280), Size = new Size(576, 280), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var preferences = new SurfacePanel { Location = new Point(24, 572), Size = new Size(552, 80), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            darkTheme = new DesignCheckBox { Text = "Тёмная тема", Checked = smoke || ThemeSettings.Load(ThemeSettings.FilePath), Location = new Point(20, 12), Size = new Size(248, 32) };
            bool autostart = false;
            try { if (!smoke) autostart = Autostart.Enabled; } catch (System.Security.SecurityException) { }
            startup = new DesignCheckBox { Text = "Запуск с Windows", Checked = autostart, Location = new Point(288, 12), Size = new Size(248, 32) };
            preferences.Controls.AddRange(new Control[] { darkTheme, startup,
                new Label { Text = "Выбор сохраняется", Location = new Point(64, 48), Size = new Size(200, 20), Font = new Font("Segoe UI", 9), BackColor = Color.Transparent },
                new Label { Text = "Сразу в трей", Location = new Point(332, 48), Size = new Size(200, 20), Font = new Font("Segoe UI", 9), BackColor = Color.Transparent } });
            shortcuts = new ShortcutHint { Location = new Point(28, 672), Size = new Size(544, 28), Font = new Font("Segoe UI", 9), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var closeHint = new Label { Text = "Крестик скрывает в трей · Выход — в меню значка", Location = new Point(28, 712), Size = new Size(544, 20), Font = new Font("Segoe UI", 9), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            Controls.AddRange(new Control[] { refresh, summary, tools, cards, preferences, shortcuts, closeHint });
            cards.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            int availableHeight = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 40;
            if (Height > availableHeight) Height = Math.Max(620, availableHeight);
            tips.SetToolTip(startup, "Запускать свёрнутой в трей при входе в Windows. Не перемещайте EXE после включения.");
            tips.SetToolTip(linked, "При включении любой отдельный ползунок также меняет остальные экраны.");
            linked.CheckedChanged += delegate { settings.Values["linked"] = linked.Checked ? "true" : "false"; UpdateAllControl(); Persist(); };
            allSlider.ValueChanged += delegate { allValue.Text = allSlider.Value + "%"; if (!updatingAll) SetAll(allSlider.Value); };
            darkTheme.CheckedChanged += delegate
            {
                ApplyTheme(); ClosePopup();
                if (!smokeTest) try { ThemeSettings.Save(ThemeSettings.FilePath, darkTheme.Checked); } catch (Exception ex) { ShowError("Сохранение темы", ex); }
            };
            startup.CheckedChanged += delegate
            {
                if (smokeTest || updatingStartup) return;
                try { Autostart.Set(startup.Checked); summary.Text = startup.Checked ? "Автозапуск в трее включён" : "Автозапуск отключён"; }
                catch (Exception ex) { summary.Text = "Автозапуск: " + ex.Message; updatingStartup = true; try { startup.Checked = !startup.Checked; } finally { updatingStartup = false; } }
            };
            refresh.Click += async delegate { await ReloadAsync(); };
            reconnect.Interval = 1200;
            reconnect.Tick += async delegate
            {
                reconnect.Stop(); await ReloadAsync();
                if (!exiting && reconnectAttempts-- > 0) { reconnect.Interval = 4000; reconnect.Start(); }
            };
            trayMenu = new ContextMenuStrip { Renderer = new ThemeMenuRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI", 10), Padding = new Padding(4), };
            trayMenu.Items.Add("Быстрая регулировка", null, delegate { ShowPopup(); });
            trayMenu.Items.Add("Открыть окно", null, delegate { ShowMain(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            foreach (string name in presets) { string preset = name; trayMenu.Items.Add(preset, null, delegate { ApplyPreset(preset); }); }
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Выход", null, delegate { RequestExit(); });
            tray = new NotifyIcon { Icon = Art.LoadIcon(16), Text = "Яркость монитора", ContextMenuStrip = trayMenu, Visible = !smoke };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowPopup(); };
            ApplyTheme();
            Shown += async delegate
            {
                if (startInTray) Hide();
                if (!smokeTest) EnableIntegration();
                await ReloadAsync();
                if (smokeTest && automatedSmoke)
                {
                    try
                    {
                        SavePreview(this, "smoke-test.png");
                        ShowPopup(); if (popup == null) throw new Exception("Tray popup was not created");
                        SavePreview(popup, "smoke-test-tray.png"); ClosePopup();
                        darkTheme.Checked = false; SavePreview(this, "smoke-test-light.png");
                        if (BackColor != new Palette(false).Background) throw new Exception("Light theme failed");
                        darkTheme.Checked = true;
                        if (BackColor != new Palette(true).Background) throw new Exception("Dark theme failed");
                        // Enable/disable linking without moving sliders or touching user settings.
                        linked.Checked = true; if (!allSlider.Enabled && SupportedCards().Count > 0) throw new Exception("Common slider disabled"); linked.Checked = false;
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-test.txt"), "PASS: dark/light themes, tray popup, common control. Cards: " + cards.Controls.Count, System.Text.Encoding.UTF8);
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-test.txt"), "FAIL: " + ex); Environment.ExitCode = 1; }
                    RequestExit();
                }
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized && !smokeTest) { Hide(); WindowState = FormWindowState.Normal; } };
        }

        private List<BrightnessCard> SupportedCards()
        {
            var result = new List<BrightnessCard>();
            foreach (BrightnessCard card in cards.Controls) if (card.Device.Supported) result.Add(card);
            return result;
        }
        private bool Persist() { if (smokeTest) return true; try { settings.Save(); return true; } catch (Exception ex) { ShowError("Сохранение настроек", ex); return false; } }
        private void ShowError(string action, Exception ex) { summary.Text = action + ": " + ex.Message; tips.SetToolTip(summary, summary.Text); }
        private string DisplayName(MonitorDevice device) { return settings.Get("name:" + device.Id, device.Name); }
        private void Rename(BrightnessCard card)
        {
            using (var dialog = new Form { Text = "Название монитора", ClientSize = new Size(380, 138), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font, BackColor = BackColor, ForeColor = ForeColor })
            {
                var field = new TextBox { Text = DisplayName(card.Device), Location = new Point(20, 24), Size = new Size(340, 28), MaxLength = 60, BorderStyle = BorderStyle.FixedSingle, BackColor = new Palette(darkTheme.Checked).Surface, ForeColor = ForeColor };
                var save = new DesignButton { Text = "Сохранить", DialogResult = DialogResult.OK, Dark = darkTheme.Checked, BackColor = BackColor, Location = new Point(148, 80), Size = new Size(112, 36) };
                var cancel = new DesignButton { Text = "Отмена", DialogResult = DialogResult.Cancel, Dark = darkTheme.Checked, BackColor = BackColor, Location = new Point(268, 80), Size = new Size(92, 36) };
                dialog.Controls.AddRange(new Control[] { field, save, cancel }); dialog.AcceptButton = save; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string name = field.Text.Trim();
                if (name.Length == 0) settings.Values.Remove("name:" + card.Device.Id); else settings.Values["name:" + card.Device.Id] = name;
                card.SetName(DisplayName(card.Device)); Persist(); ClosePopup();
            }
        }
        private void SetAll(int value)
        {
            if (loading || exiting) return;
            foreach (var card in SupportedCards()) card.SetTarget(value);
            UpdateAllControl();
        }
        private void AdjustAll(int delta)
        {
            if (loading || exiting) return;
            foreach (var card in SupportedCards()) card.SetTarget(card.Target + delta);
            UpdateAllControl();
        }
        private void UpdateAllControl()
        {
            var list = SupportedCards();
            allSlider.Enabled = !loading && linked.Checked && list.Count > 0;
            updatingAll = true;
            try
            {
                bool same = list.Count > 0; int sum = 0;
                foreach (var card in list) { sum += card.Target; if (card.Target != list[0].Target) same = false; }
                allSlider.Value = list.Count > 0 ? sum / list.Count : 0;
                allValue.Text = list.Count == 0 ? "—" : same ? allSlider.Value + "%" : "Разные";
                allValue.Font = same ? sameFont : mixedFont;
            }
            finally { updatingAll = false; }
        }
        private void ApplyPreset(string preset)
        {
            if (loading || exiting) return;
            foreach (var card in SupportedCards()) card.SetTarget(settings.Preset(preset, card.Device.Id));
            UpdateAllControl(); ClosePopup(); summary.Text = "Пресет «" + preset + "» отправлен мониторам";
        }
        private void SavePreset(string preset)
        {
            var list = SupportedCards(); if (loading || list.Count == 0) return;
            foreach (var card in list) settings.Values["preset:" + preset + ":" + card.Device.Id] = card.Target.ToString();
            if (Persist()) summary.Text = "Пресет «" + preset + "» сохранён для " + list.Count + " экранов";
        }
        private void ClosePopup() { if (popup != null) { var old = popup; popup = null; old.Close(); old.Dispose(); } }
        private void ShowPopup()
        {
            if (exiting) return;
            if (popup != null) { ClosePopup(); return; }
            bool dark = darkTheme.Checked;
            var c = new Palette(dark);
            var quick = new QuickForm(c) { Text = "Яркость монитора", Font = Font, AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi, Icon = Art.LoadIcon(32) };
            popup = quick;
            var list = SupportedCards();
            bool syncing = false;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            float dpi; using (var graphics = CreateGraphics()) dpi = graphics.DpiX / 96f;
            quick.ClientSize = new Size(384, Math.Min(Math.Max(216, list.Count * 104 + 144), Math.Max(216, (int)((area.Height - 48) / dpi))));
            quick.Controls.Add(new BrandMark { Dark = dark, Location = new Point(20, 20), Size = new Size(24, 24) });
            quick.Controls.Add(new Label { Text = "Яркость", Location = new Point(56, 16), Size = new Size(240, 32), Font = new Font("Segoe UI", 14), ForeColor = c.Text });
            var close = new DesignButton { Text = "", Glyph = "x", Quiet = true, Dark = dark, Location = new Point(332, 16), Size = new Size(32, 32), AccessibleName = "Закрыть быструю регулировку", BackColor = c.Background };
            close.Click += delegate { ClosePopup(); }; quick.Controls.Add(close);
            var flow = new FlowLayoutPanel { Location = new Point(20, 68), Size = new Size(364, quick.ClientSize.Height - 136), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            quick.Controls.Add(flow);
            var synchronize = new System.Collections.Generic.List<Action>();
            foreach (var card in list)
            {
                var row = new Panel { Width = 344, Height = 96, Margin = new Padding(0, 0, 0, 8), BackColor = c.Background };
                var label = new Label { Text = DisplayName(card.Device), Location = new Point(0, 4), Size = new Size(248, 28), AutoEllipsis = true, ForeColor = c.Text, TextAlign = ContentAlignment.MiddleLeft };
                var number = new Label { Text = card.Target + "%", Location = new Point(252, 0), Size = new Size(92, 36), Font = new Font("Consolas", 20), ForeColor = c.Text, TextAlign = ContentAlignment.MiddleRight };
                var slider = new BrightnessSlider { Minimum = 0, Maximum = 100, Value = card.Target, SmallChange = 1, LargeChange = 10, Location = new Point(0, 44), Size = new Size(344, 32), BackColor = c.Background, Dark = dark, AccessibleName = "Яркость: " + DisplayName(card.Device) };
                slider.ValueChanged += delegate
                {
                    if (syncing) return;
                    if (linked.Checked) SetAll(slider.Value); else { card.SetTarget(slider.Value); UpdateAllControl(); }
                    number.Text = slider.Value + "%";
                };
                row.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(c.Border)) e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1); };
                row.Controls.AddRange(new Control[] { label, number, slider }); flow.Controls.Add(row);
                synchronize.Add(delegate
                {
                    if (card.IsDisposed) return;
                    syncing = true;
                    try { if (!slider.Capture && slider.Value != card.Target) slider.Value = card.Target; number.Text = card.Target + "%"; }
                    finally { syncing = false; }
                });
            }
            if (list.Count == 0) flow.Controls.Add(new Label { Text = loading ? "Поиск мониторов…" : "Нет доступных экранов", Size = new Size(344, 40), ForeColor = c.Secondary });
            var open = new DesignButton { Text = "Открыть утилиту", Glyph = "settings", Dark = dark, Location = new Point(20, quick.ClientSize.Height - 56), Size = new Size(344, 36), BackColor = c.Background, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            open.Click += delegate { ClosePopup(); ShowMain(); }; quick.Controls.Add(open);
            var sync = new System.Windows.Forms.Timer { Interval = 80 };
            sync.Tick += delegate { foreach (var action in synchronize) action(); };
            quick.FormClosed += delegate { sync.Dispose(); if (popup == quick) popup = null; };
            quick.Deactivate += delegate { if (!smokeTest && popup == quick) ClosePopup(); };
            quick.Show(this);
            quick.Location = new Point(Math.Max(area.Left, area.Right - quick.Width - 12), Math.Max(area.Top, area.Bottom - quick.Height - 12));
            sync.Start();
        }
        private void ShowMain() { if (exiting) return; Show(); WindowState = FormWindowState.Normal; Activate(); }
        private static void SavePreview(Form form, string name) { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name)); } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTheme(); }
        private void ApplyTheme()
        {
            if (darkTheme == null) return;
            bool dark = darkTheme.Checked;
            var c = new Palette(dark); BackColor = c.Background; ForeColor = c.Text;
            ThemeChildren(this, dark, false);
            allValue.ForeColor = c.Text;
            foreach (BrightnessCard card in cards.Controls) card.ApplyTheme(dark);
            if (trayMenu != null) { trayMenu.BackColor = c.Surface; trayMenu.ForeColor = c.Text; ((ThemeMenuRenderer)trayMenu.Renderer).Colors = c; }
            if (IsHandleCreated) { int enabled = dark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)); }
        }
        private void ThemeChildren(Control root, bool dark, bool onSurface)
        {
            var c = new Palette(dark);
            foreach (Control control in root.Controls)
            {
                if (control is BrightnessCard) continue;
                var surface = control as SurfacePanel;
                if (surface != null) { surface.SetTheme(dark); ThemeChildren(surface, dark, true); continue; }
                var button = control as DesignButton;
                if (button != null) { button.Dark = dark; button.BackColor = onSurface ? c.Surface : c.Background; button.Invalidate(); }
                else if (control is Label) control.ForeColor = control.Font.Size >= 18 ? c.Text : c.Secondary;
                var check = control as DesignCheckBox;
                if (check != null) { check.Dark = dark; check.BackColor = onSurface ? c.Surface : c.Background; check.Invalidate(); }
                var slider = control as BrightnessSlider;
                if (slider != null) { slider.Dark = dark; slider.BackColor = onSurface ? c.Surface : c.Background; slider.Invalidate(); }
                var mark = control as BrandMark; if (mark != null) { mark.Dark = dark; mark.Invalidate(); }
                var hint = control as ShortcutHint; if (hint != null) { hint.Dark = dark; hint.Invalidate(); }
                ThemeChildren(control, dark, onSurface);
            }
        }
        private async Task ReloadAsync()
        {
            if (exiting) return;
            if (loading) { reloadAgain = true; return; }
            loading = true; refresh.Enabled = tools.Enabled = false; cards.Enabled = false; ClosePopup();
            summary.Text = "Поиск мониторов…";
            foreach (BrightnessCard card in cards.Controls) card.Stop();
            await gate.WaitAsync();
            try
            {
                while (cards.Controls.Count > 0) cards.Controls[0].Dispose();
                if (monitors != null) { monitors.Dispose(); monitors = null; }
                monitors = await Task.Run(() => MonitorCollection.Find());
                if (exiting) return;
                int index = 0;
                foreach (var monitor in monitors.Items)
                {
                    monitor.Name = (++index) + " · " + monitor.Name;
                    var card = new BrightnessCard(monitor, gate); card.SetName(DisplayName(monitor));
                    card.RenameRequested += Rename;
                    card.UserValueChanged += delegate(BrightnessCard source) { if (linked.Checked) SetAll(source.Target); else UpdateAllControl(); };
                    cards.Controls.Add(card);
                }
                summary.Text = "Экранов доступно: " + SupportedCards().Count + " · Подключение отслеживается";
                if (monitors.Items.Count == 0) cards.Controls.Add(new BrightnessCard(new MonitorDevice { Name = "Монитор не обнаружен", Id = "none" }, gate));
            }
            catch (Exception ex) { ShowError("Поиск мониторов", ex); }
            finally
            {
                gate.Release(); loading = false; refresh.Enabled = tools.Enabled = cards.Enabled = !exiting; UpdateAllControl(); ApplyTheme();
                if (reloadAgain && !exiting) { reloadAgain = false; reconnect.Interval = 1200; reconnect.Start(); }
            }
        }
        private void ScheduleReconnect()
        {
            if (exiting || !IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(new Action(delegate { if (exiting) return; reconnectAttempts = 2; reconnect.Stop(); reconnect.Interval = 1200; reconnect.Start(); })); } catch (InvalidOperationException) { }
        }
        private void DisplayChanged(object sender, EventArgs e) { ScheduleReconnect(); }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) ScheduleReconnect(); }
        private void EnableIntegration()
        {
            SystemEvents.DisplaySettingsChanged += DisplayChanged; SystemEvents.PowerModeChanged += PowerChanged; systemEvents = true;
            hotUp = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.PageUp);
            hotDown = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.PageDown);
            mouseCallback = MouseProc;
            mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
            if (!hotUp || !hotDown || mouseHook == IntPtr.Zero) { tips.SetToolTip(shortcuts, "Часть глобальных сочетаний недоступна: возможно, занята другой программой"); }
        }
        private IntPtr MouseProc(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message.ToInt32() == 0x020A && !loading && !exiting && monitors != null && monitors.Items.Exists(x => x.Supported) && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0 && (Native.GetAsyncKeyState(0x12) & 0x8000) != 0)
            {
                int delta = (short)((uint)Marshal.ReadInt32(data, 8) >> 16);
                wheelRemainder += delta; int steps = wheelRemainder / 120; wheelRemainder %= 120;
                if (steps != 0) BeginInvoke(new Action(() => AdjustAll(steps * 5)));
                return new IntPtr(1);
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.ShowMessage) { ShowMain(); return; }
            if (m.Msg == 0x0312) { AdjustAll(m.WParam.ToInt32() == 1 ? 5 : -5); return; }
            if (m.Msg == 0x007e || (m.Msg == 0x0219 && m.WParam.ToInt64() == 7)) ScheduleReconnect();
            base.WndProc(ref m);
        }
        private void RequestExit() { exitRequested = true; Close(); }
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (canClose) { base.OnFormClosing(e); return; }
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) { canClose = true; base.OnFormClosing(e); return; }
            e.Cancel = true;
            if (!exitRequested && !smokeTest) { Hide(); ClosePopup(); base.OnFormClosing(e); return; }
            if (exiting) return;
            exiting = true; reconnect.Stop(); ClosePopup();
            while (loading) await Task.Delay(30);
            foreach (BrightnessCard card in cards.Controls) card.Stop();
            await gate.WaitAsync();
            try { if (monitors != null) monitors.Dispose(); monitors = null; }
            finally { gate.Release(); }
            canClose = true; BeginInvoke(new Action(Close)); base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            exiting = true;
            if (systemEvents) { SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.PowerModeChanged -= PowerChanged; }
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (hotUp) Native.UnregisterHotKey(Handle, 1); if (hotDown) Native.UnregisterHotKey(Handle, 2);
            tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); tips.Dispose(); reconnect.Dispose(); sameFont.Dispose(); mixedFont.Dispose(); ClosePopup();
            base.OnFormClosed(e);
        }
    }
}
