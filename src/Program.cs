using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexLimitMeter
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--check")
            {
                try { File.WriteAllText(args[1], Limits.Read().Summary()); return 0; }
                catch (Exception e) { File.WriteAllText(args[1], "Unable to read limits: " + e.Message); return 1; }
            }
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest.Run();
            bool first;
            using (var mutex = new Mutex(true, "Local\\CodexLimitMeter.Tray", out first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var tray = new Tray()) Application.Run(tray);
            }
            return 0;
        }
    }

    sealed class Window
    {
        public double Remaining;
        public int Minutes;
        public DateTime? Reset;
        public string Label { get { return Minutes == 10080 ? "Weekly" : Minutes == 300 ? "5-hour" : Minutes > 0 ? Minutes + "-minute" : "Usage"; } }
        public string Text { get { return Label + ": " + Remaining.ToString("0") + "% left" + (Reset.HasValue ? "  |  resets " + Reset.Value.ToString("ddd d MMM, HH:mm") : ""); } }
    }
    sealed class Bucket
    {
        public string Name;
        public List<Window> Windows = new List<Window>();
    }
    sealed class Limits
    {
        public List<Bucket> Buckets = new List<Bucket>();
        public DateTime Updated = DateTime.Now;
        public double? Remaining
        {
            get
            {
                var main = Buckets.FirstOrDefault(b => b.Name == "codex") ?? Buckets.FirstOrDefault();
                return main == null || main.Windows.Count == 0 ? (double?)null : main.Windows.Min(w => w.Remaining);
            }
        }
        public string Summary() { return string.Join(Environment.NewLine, Buckets.SelectMany(b => new[] { b.Name }.Concat(b.Windows.Select(w => w.Text)))) + Environment.NewLine + "Updated " + Updated.ToString("HH:mm:ss"); }
        internal static Dictionary<string, object> Obj(object x) { return x as Dictionary<string, object>; }
        internal static object Get(Dictionary<string, object> x, string key) { object v; return x != null && x.TryGetValue(key, out v) ? v : null; }
        public static Limits Parse(Dictionary<string, object> result)
        {
            var limits = new Limits();
            var map = Obj(Get(result, "rateLimitsByLimitId"));
            if (map != null && map.Count > 0)
                foreach (var pair in map) limits.Add(pair.Key, Obj(pair.Value));
            else limits.Add("codex", Obj(Get(result, "rateLimits")));
            if (limits.Buckets.Count == 0) throw new InvalidOperationException("No ChatGPT limits returned. Sign in to Codex with a ChatGPT account.");
            return limits;
        }
        void Add(string id, Dictionary<string, object> data)
        {
            if (data == null) return;
            var b = new Bucket { Name = id };
            foreach (string key in new[] { "primary", "secondary" })
            {
                var w = Obj(Get(data, key));
                if (w == null || Get(w, "usedPercent") == null) continue;
                double used = Convert.ToDouble(Get(w, "usedPercent"));
                if (double.IsNaN(used) || double.IsInfinity(used)) continue;
                var window = new Window { Remaining = Math.Max(0, Math.Min(100, 100 - used)), Minutes = Convert.ToInt32(Get(w, "windowDurationMins") ?? 0) };
                if (Get(w, "resetsAt") != null) window.Reset = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToDouble(Get(w, "resetsAt"))).ToLocalTime();
                b.Windows.Add(window);
            }
            Buckets.Add(b);
        }
        public static string FindCodex()
        {
            string custom = Environment.GetEnvironmentVariable("CODEX_LIMIT_METER_CODEX");
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom) && Path.GetExtension(custom).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(custom);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string installed = Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            if (File.Exists(installed)) return installed;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { string file = Path.Combine(dir.Trim('"'), "codex.exe"); if (File.Exists(file)) return file; } catch (ArgumentException) { }
            }
            throw new FileNotFoundException("Codex executable not found. Install Codex, or set CODEX_LIMIT_METER_CODEX to its .exe path.");
        }
        public static Limits Read()
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 4194304 };
            var start = new ProcessStartInfo(FindCodex(), "app-server") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_HOME")))
                start.EnvironmentVariables["CODEX_HOME"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            using (var p = new Process { StartInfo = start })
            {
                p.Start();
                p.ErrorDataReceived += delegate { }; // Drain diagnostics without storing auth or account data.
                p.BeginErrorReadLine();
                using (var timeout = new System.Threading.Timer(delegate { try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { } }, null, 30000, Timeout.Infinite))
                {
                    try
                    {
                        p.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_limit_meter\",\"title\":\"Codex Limit Meter\",\"version\":\"1.0.0\"}}}");
                        p.StandardInput.Flush();
                        Reply(p, json, 1);
                        p.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                        p.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                        p.StandardInput.Flush();
                        return Parse(Reply(p, json, 2));
                    }
                    finally
                    {
                        try { p.StandardInput.Close(); if (!p.WaitForExit(1000)) { p.Kill(); p.WaitForExit(2000); } } catch (InvalidOperationException) { }
                    }
                }
            }
        }
        static Dictionary<string, object> Reply(Process p, JavaScriptSerializer json, int id)
        {
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                var msg = json.Deserialize<Dictionary<string, object>>(line);
                if (Get(msg, "id") == null || Convert.ToString(Get(msg, "id")) != id.ToString()) continue;
                if (Get(msg, "error") != null) throw new InvalidOperationException("Codex rejected the usage request. Check Codex sign-in and connection.");
                var result = Obj(Get(msg, "result"));
                if (result == null) throw new InvalidOperationException("Codex returned an invalid response.");
                return result;
            }
            throw new InvalidOperationException("Codex did not respond within 30 seconds or could not start. Check Codex sign-in and connection.");
        }
    }

    sealed class Tray : ApplicationContext
    {
        readonly NotifyIcon icon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60000 };
        readonly Control dispatcher = new Control();
        Limits latest;
        string error;
        bool busy, closing;
        Icon ownedIcon;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public Tray()
        {
            var handle = dispatcher.Handle; // Message dispatch only; no visible Form or taskbar button.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            icon.ContextMenuStrip = menu;
            icon.Text = "Codex Limit Meter - checking limits";
            SetIcon("...", Color.SlateGray);
            icon.Visible = true;
            icon.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { BuildMenu(); menu.Show(Cursor.Position); } };
            menu.Opening += delegate { BuildMenu(); };
            timer.Tick += async delegate { await Refresh(); };
            timer.Start();
            dispatcher.BeginInvoke(new Action(async delegate { await Refresh(); }));
        }
        async Task Refresh()
        {
            if (busy || closing) return;
            busy = true;
            try { latest = await Task.Run(() => Limits.Read()); error = null; }
            catch (Exception e) { error = e.Message; }
            finally { busy = false; }
            if (closing) return;
            var remaining = latest == null ? null : latest.Remaining;
            SetIcon(error != null ? "!" : remaining.HasValue ? remaining.Value.ToString("0") : "--", error != null || !remaining.HasValue ? Color.SlateGray : remaining <= 10 ? Color.IndianRed : remaining <= 25 ? Color.DarkOrange : Color.MediumSeaGreen);
            string text = error != null ? "Codex - unavailable (click for details)" : "Codex: " + (remaining.HasValue ? remaining.Value.ToString("0") + "% remaining" : "no quota windows") + " | " + DateTime.Now.ToString("HH:mm");
            icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }
        void BuildMenu()
        {
            while (menu.Items.Count > 0) { var old = menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose(); }
            Add("Codex Limit Meter", false);
            if (error != null) { Add("Unavailable - previous data may be stale", false); Add(error, false); }
            if (latest != null)
            {
                foreach (var b in latest.Buckets)
                {
                    Add(b.Name, false);
                    foreach (var w in b.Windows) Add(w.Text, false);
                    if (b.Windows.Count == 0) Add("No quota windows reported", false);
                }
                Add("Last success: " + latest.Updated.ToString("d MMM HH:mm:ss"), false);
            }
            menu.Items.Add(new ToolStripSeparator());
            var refresh = Add(busy ? "Refreshing..." : "Refresh now", !busy);
            refresh.Click += async delegate { await Refresh(); };
            var startup = Add("Start with Windows", true);
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startup.Checked = key != null && Convert.ToString(key.GetValue("CodexLimitMeter")) == "\"" + Application.ExecutablePath + "\"";
            startup.Click += delegate
            {
                try { using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) { if (startup.Checked) key.DeleteValue("CodexLimitMeter", false); else key.SetValue("CodexLimitMeter", "\"" + Application.ExecutablePath + "\""); } }
                catch (Exception) { icon.ShowBalloonTip(5000, "Codex Limit Meter", "Could not change Windows startup setting.", ToolTipIcon.Warning); }
            };
            Add("Open Codex usage page", true).Click += delegate { Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage") { UseShellExecute = true }); };
            Add("Exit", true).Click += delegate { ExitThread(); };
        }
        ToolStripMenuItem Add(string text, bool enabled) { var item = new ToolStripMenuItem(text) { Enabled = enabled }; menu.Items.Add(item); return item; }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        void SetIcon(string text, Color color)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            using (var font = new Font("Segoe UI", text.Length > 2 ? 12 : 16, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(brush, 0, 0, 31, 31);
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (fmt) g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, 32, 31), fmt);
                IntPtr h = bitmap.GetHicon();
                Icon next;
                try { using (var borrowed = Icon.FromHandle(h)) next = (Icon)borrowed.Clone(); } finally { DestroyIcon(h); }
                icon.Icon = next;
                if (ownedIcon != null) ownedIcon.Dispose();
                ownedIcon = next;
            }
        }
        protected override void ExitThreadCore()
        {
            closing = true;
            timer.Stop();
            icon.Visible = false;
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { closing = true; timer.Dispose(); icon.Dispose(); menu.Dispose(); dispatcher.Dispose(); if (ownedIcon != null) ownedIcon.Dispose(); }
            base.Dispose(disposing);
        }
    }
    static class SelfTest
    {
        public static int Run()
        {
            var json = new JavaScriptSerializer();
            Func<string, Limits> parse = s => Limits.Parse(json.Deserialize<Dictionary<string, object>>(s));
            try
            {
                var a = parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":1800000000},\"secondary\":{\"usedPercent\":90,\"windowDurationMins\":10080}}}");
                if (a.Remaining != 10 || a.Buckets[0].Windows[0].Label != "5-hour" || !a.Buckets[0].Windows[0].Reset.HasValue) return 1;
                var b = parse("{\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":99}},\"codex\":{\"primary\":{\"usedPercent\":-3}}}}");
                if (b.Remaining != 100 || b.Buckets.Count != 2) return 2;
                if (parse("{\"rateLimits\":{\"primary\":null}}").Remaining != null) return 3;
                if (parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":150}}}").Remaining != 0) return 4;
                try { parse("{}"); return 5; } catch (InvalidOperationException) { }
                return 0;
            }
            catch { return 10; }
        }
    }
}
