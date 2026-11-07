using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightnessUtility;

internal static class LifecycleCheck
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        int result = 1;
        var form = new MainForm(false, true);
        form.Shown += async delegate
        {
            try
            {
                for (int attempt = 0; attempt < 500 && (bool)typeof(MainForm).GetField("loading", Flags).GetValue(form); attempt++) await Task.Delay(20);
                if ((bool)typeof(MainForm).GetField("loading", Flags).GetValue(form)) throw new Exception("Load timed out");
                if (form.Visible) throw new Exception("--tray must hide the main window");
                var tray = (NotifyIcon)typeof(MainForm).GetField("tray", Flags).GetValue(form);
                if (!tray.Visible) throw new Exception("Tray icon must stay visible");
                typeof(MainForm).GetMethod("ShowMain", Flags).Invoke(form, null);
                if (!form.Visible) throw new Exception("Main window did not reopen");
                form.Close(); await Task.Delay(50);
                if (form.IsDisposed || form.Visible) throw new Exception("Close must hide the window without exiting");
                typeof(MainForm).GetMethod("ShowPopup", Flags).Invoke(form, null);
                var popup = (Form)typeof(MainForm).GetField("popup", Flags).GetValue(form);
                if (popup == null || !popup.Visible) throw new Exception("Popup did not open while main window hidden");
                Console.WriteLine("PASS: start in tray, reopen window, close to tray, popup while hidden");
                result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); }
            typeof(MainForm).GetMethod("RequestExit", Flags).Invoke(form, null);
        };
        Application.Run(form);
        if (result == 0) Console.WriteLine("PASS: explicit exit terminates the application");
        form.Dispose(); return result;
    }
}
