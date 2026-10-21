using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

public class TrayTest : MarshalByRefObject {
    [STAThread]
    public static int Main(string[] args) {
        try {
            var setup = new AppDomainSetup { ApplicationBase = Path.GetFullPath(args[0]).TrimEnd('\\') + "\\" };
            var domain = AppDomain.CreateDomain("TrayTest", null, setup);
            var runner = (TrayTest)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location, typeof(TrayTest).FullName);
            runner.Run(args.Length > 1 ? args[1] : "normal");
            AppDomain.Unload(domain);
            return 0;
        } catch (Exception e) { Console.WriteLine(e); return 1; }
    }
    public void Run(string mode) {
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        var app = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mouse Drive Beta.exe"));
        Application.EnableVisualStyles();
        var languageType = app.GetType("FileManager.LanguageFile", true);
        var language = Activator.CreateInstance(languageType);
        languageType.GetMethod("GetLanguageFileCount").Invoke(language, null);
        languageType.GetMethod("SetLanguage").Invoke(language, new object[] { languageType.GetField("LanguageIndex").GetValue(language), null });
        var configType = app.GetType("Mouse_Drive_Beta.FileManager.DriveConfig", true);
        var config = Activator.CreateInstance(configType);
        if (!(bool)configType.GetMethod("GetDriveConfig").Invoke(config, null)) throw new Exception("Config failed");
        var param = configType.GetField("gDriveParam").GetValue(config);
        var type = app.GetType("Mouse_Drive_Beta.FormHomePage", true);
        using (var form = (Form)Activator.CreateInstance(type, new object[] { param, true })) {
            var flag = type.GetField("StartInTray", BindingFlags.Instance | BindingFlags.NonPublic);
            flag.SetValue(form, true);
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
            form.Show();
            if (form.Visible || !form.IsHandleCreated) throw new Exception("Startup must have a hidden native handle");
            Console.WriteLine("PASS startup: Visible=false; native handle created.");
            type.GetMethod("ToolStripMenuItem_OpenMenu_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty });
            if (!form.Visible || form.WindowState != FormWindowState.Normal) throw new Exception("Tray restore failed");
            Console.WriteLine("PASS tray Open: Visible=true; Normal.");
            type.GetMethod("customButton_Mini_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty });
            if (form.Visible) throw new Exception("Minimize must hide");
            Console.WriteLine("PASS minimize: Visible=false.");
            var open = (ToolStripMenuItem)type.GetField("ToolStripMenuItem_OpenMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            var exit = (ToolStripMenuItem)type.GetField("ToolStripMenuItem_Exit", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            if (mode == "exit-home") {
                Console.WriteLine("Invoking actual HomePage tray Exit");
                exit.PerformClick();
                throw new Exception("Tray Exit did not terminate the process");
            }
            VerifyCross(form, type, open);
        }
        var mainType = app.GetType("Mouse_Drive_Beta.FormMain", true);
        using (var form = (Form)Activator.CreateInstance(mainType, new object[] { param })) {
            var open = (ToolStripMenuItem)mainType.GetField("ToolStripMenuItem_OpenMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            if (mode == "exit-main") {
                var exit = (ToolStripMenuItem)mainType.GetField("ToolStripMenuItem_Exit", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Console.WriteLine("Invoking actual FormMain tray Exit");
                exit.PerformClick();
                // The vendor exit handler uses a 100 ms timer for device cleanup.
                var timeout = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < timeout) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                throw new Exception("Tray Exit did not terminate the process");
            }
            VerifyCross(form, mainType, open);
        }
    }
    private static void VerifyCross(Form form, Type type, ToolStripMenuItem open) {
        open.PerformClick();
        var cross = (Control)type.GetField("customButton_Close", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cross, new object[] { EventArgs.Empty });
        if (form.Visible || form.IsDisposed) throw new Exception(type.Name + " cross must hide without disposing");
        open.PerformClick();
        if (!form.Visible) throw new Exception(type.Name + " tray restore after cross failed");
        form.Close();
        if (form.Visible || form.IsDisposed) throw new Exception(type.Name + " user close must hide without disposing");
        var shutdown = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
        type.GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { shutdown });
        if (shutdown.Cancel) throw new Exception("Windows shutdown must not be cancelled");
        Console.WriteLine("PASS " + type.Name + ": actual cross hides, tray restores, Close hides, shutdown allowed.");
    }
}
