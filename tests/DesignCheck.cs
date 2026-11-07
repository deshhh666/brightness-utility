using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BrightnessUtility;

internal static class DesignCheck
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Call(object target, string method, object argument) { target.GetType().GetMethod(method, Flags).Invoke(target, new object[] { argument }); }
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        try
        {
            using (var form = new Form())
            using (var slider = new BrightnessSlider { Width = 512, Height = 36, Value = 50 })
            {
                form.Controls.Add(slider); form.CreateControl(); slider.CreateControl();
                Call(slider, "OnKeyDown", new KeyEventArgs(Keys.Right)); Require(slider.Value == 51, "Arrow step failed");
                Call(slider, "OnKeyDown", new KeyEventArgs(Keys.PageUp)); Require(slider.Value == 61, "PageUp step failed");
                Call(slider, "OnKeyDown", new KeyEventArgs(Keys.End)); Require(slider.Value == 100, "End key failed");
                Call(slider, "OnKeyDown", new KeyEventArgs(Keys.Right)); Require(slider.Value == 100, "Upper bound failed");
                Call(slider, "OnKeyDown", new KeyEventArgs(Keys.Home)); Require(slider.Value == 0, "Home key failed");
                Call(slider, "OnKeyDown", new KeyEventArgs(Keys.Left)); Require(slider.Value == 0, "Lower bound failed");
                Call(slider, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, 60)); Require(slider.Value == 0, "High resolution wheel moved too early");
                Call(slider, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, 60)); Require(slider.Value == 5, "High resolution wheel accumulation failed");
                Call(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 256, 18, 0));
                Require(slider.Value >= 49 && slider.Value <= 51, "Click position did not map to 50%");
                Call(slider, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 510, 18, 0));
                Require(slider.Value == 100, "Drag did not reach 100%");
                Call(slider, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 510, 18, 0)); Require(!slider.Capture, "Mouse capture did not release");
                slider.AccessibilityObject.Value = "37%"; Require(slider.Value == 37, "Accessible slider value failed");
                Require(slider.AccessibilityObject.Role == AccessibleRole.Slider, "Accessibility role failed");
                Console.WriteLine("PASS: custom slider keyboard, bounds, wheel, click, drag and accessibility");
            }
            using (var icon = Art.LoadIcon(16)) Require(icon.Width == 16 && icon.Height == 16, "Tray icon size failed");
            using (var icon = Art.LoadIcon(32)) Require(icon.Width == 32 && icon.Height == 32, "App icon size failed");
            Require(typeof(DesignCheck).Assembly.GetManifestResourceInfo("Brightness.ico") != null, "Embedded icon missing");
            Console.WriteLine("PASS: standalone embedded tray/app icon resources");
            // Paint into a deliberately contaminated buffer. A switch must cover its
            // complete client area without relying on themed parent background paint.
            using (var toggle = new DesignCheckBox { Size = new Size(352, 32), Text = "Все мониторы", Font = new Font("Segoe UI", 10) })
            using (var bitmap = new Bitmap(toggle.Width, toggle.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                foreach (bool dark in new bool[] { true, false, true })
                foreach (bool enabled in new bool[] { false, true, false })
                {
                    toggle.Dark = dark; toggle.BackColor = new Palette(dark).Surface; toggle.Checked = enabled;
                    graphics.Clear(Color.Magenta);
                    Call(toggle, "OnPaint", new PaintEventArgs(graphics, toggle.ClientRectangle));
                    for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        Require(bitmap.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb(), "Unpainted switch background: stale pixels can leak through");
                    Require(bitmap.GetPixel(4, 1).ToArgb() == toggle.BackColor.ToArgb(), "Switch background must match its surface");
                }
            }
            Console.WriteLine("PASS: switch clears contaminated paint buffer across states and themes");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
    }
}
