// Drives the installer form without showing it against a test copy of the game.
// Usage: harness.exe <installer.exe> <testroot> <real game root with the tested install>
// testroot\{GameData,corruption} must hold StarWarsG.exe.orig.src (untouched exe) and Data\{MegaFiles.xml,config.meg,patch2.meg}.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

class Harness {
    static Type T; static object F;
    static object Get(string n) { return T.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(F); }
    static object Call(string n, params object[] a) { return T.GetMethod(n, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(F, a); }
    static int fails;
    static void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    static readonly Encoding L1 = Encoding.GetEncoding(28591);
    static string Txt(string p) { return File.Exists(p) ? L1.GetString(File.ReadAllBytes(p)) : null; }
    static bool Same(string a, string b) { return File.Exists(a) && File.Exists(b) && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)); }
    static string root, real;
    static readonly string[] Dirs = { "GameData", "corruption" };
    static string P(string d, string f) { return Path.Combine(root, d, f); }
    static string X(string d, string f) { return Path.Combine(root, d, @"Data\XML", f); }

    static void Reset(bool legacy) {
        foreach (var d in Dirs) {
            string xml = Path.Combine(root, d, @"Data\XML");
            if (Directory.Exists(xml)) Directory.Delete(xml, true);
            foreach (var f in Directory.GetFiles(Path.Combine(root, d), "StarWarsG.exe*")) if (!f.EndsWith(".src")) File.Delete(f);
            if (legacy) {   // what the user had before the installer: tested exe + .orig + hand-made XMLs
                File.Copy(Path.Combine(real, d, "StarWarsG.exe"), P(d, "StarWarsG.exe"));
                File.Copy(Path.Combine(real, d, "StarWarsG.exe.orig"), P(d, "StarWarsG.exe.orig"));
                Directory.CreateDirectory(xml);
                foreach (var f in new[] { "GameConstants.xml", "TacticalCameras.xml" }) File.Copy(Path.Combine(real, d, @"Data\XML", f), X(d, f));
            } else File.Copy(P(d, "StarWarsG.exe.orig.src"), P(d, "StarWarsG.exe"));
        }
        Call("LoadFolder");
    }
    static CheckBox Box(int i) { var g = ((System.Collections.IList)Get("games"))[i]; return (CheckBox)g.GetType().GetField("Box").GetValue(g); }
    static string State(int i) { var g = ((System.Collections.IList)Get("games"))[i]; return ((Label)g.GetType().GetField("State").GetValue(g)).Text; }

    // the tested hand-made files, as the installer should now produce them
    static void CheckInstalled(string d, string tag) {
        string tested = Path.Combine(real, d, @"Data\XML");
        Check(tag + " " + d + " exe = tested", Same(P(d, "StarWarsG.exe"), Path.Combine(real, d, "StarWarsG.exe")));
        Check(tag + " " + d + " backup = original", Same(P(d, "StarWarsG.exe.orig"), P(d, "StarWarsG.exe.orig.src")));
        var gc = Txt(X(d, "GameConstants.xml"));
        Check(tag + " " + d + " GameConstants = tested + marker", gc != null && gc.Contains("<!-- Force Commander camera mod -->") &&
              gc.Replace(" <!-- Force Commander camera mod -->", "") == Txt(Path.Combine(tested, "GameConstants.xml")));
        var tc = Txt(X(d, "TacticalCameras.xml"));
        // the hand-made base-game copy also trimmed the trailing whitespace on the last line
        var want = Txt(Path.Combine(tested, "TacticalCameras.xml"));
        Check(tag + " " + d + " TacticalCameras = tested", tc != null && (tc == want || tc.Replace("</TacticalCameras>\t  ", "</TacticalCameras>") == want));
    }
    static void CheckClean(string d, string tag) {
        Check(tag + " " + d + " exe original", Same(P(d, "StarWarsG.exe"), P(d, "StarWarsG.exe.orig.src")));
        var left = Directory.Exists(Path.Combine(root, d, @"Data\XML")) ? Directory.GetFiles(Path.Combine(root, d, @"Data\XML")) : new string[0];
        Check(tag + " " + d + " no XMLs left (" + string.Join(", ", left.Select(Path.GetFileName)) + ")", left.Length == 0);
    }

    [STAThread]
    static int Main(string[] a) {
        root = a[1]; real = a[2];
        var asm = Assembly.LoadFrom(a[0]);
        T = asm.GetType("InstallerForm");
        F = T.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null).Invoke(null);
        ((TextBox)Get("folder")).Text = root;
        var timer = new Timer { Interval = 300 };
        timer.Tick += (s, e) => CloseDialogs();
        timer.Start();

        // 1. clean install into both
        Reset(false);
        Console.WriteLine("status: " + ((Label)Get("status")).Text + " | " + State(0) + " | " + State(1));
        Check("clean: both ticked", Box(0).Checked && Box(1).Checked);
        Call("DoInstall");
        foreach (var d in Dirs) CheckInstalled(d, "clean");
        Check("reload shows installed", State(0) == "Installed." && State(1) == "Installed.");

        // 2. untick FoC -> removed there only; reinstall over itself is a no-op for EaW
        Box(1).Checked = false;
        Call("DoInstall");
        CheckInstalled("GameData", "untick");
        CheckClean("corruption", "untick");
        Check("FoC unticked after reload? (should be re-ticked as ready)", Box(1).Checked && State(1).StartsWith("Original"));

        // 3. restore all
        Call("DoRestore");
        foreach (var d in Dirs) CheckClean(d, "restore");

        // 4. another mod's GameConstants already there -> edited, saved, put back exactly
        Reset(false);
        Directory.CreateDirectory(Path.Combine(root, @"GameData\Data\XML"));
        File.Copy(Path.Combine(real, @"GameData\Data\XML\GameConstants.xml"), X("GameData", "GameConstants.xml"));
        var other = Txt(X("GameData", "GameConstants.xml")).Replace("<Land_Tactical_Camera_Locked> False", "<Land_Tactical_Camera_Locked> True") + "<!-- other mod -->\r\n";
        File.WriteAllBytes(X("GameData", "GameConstants.xml"), L1.GetBytes(other));
        Call("LoadFolder");
        Call("DoInstall");
        var gc = Txt(X("GameData", "GameConstants.xml"));
        Check("other mod: kept its change", gc.EndsWith("<!-- other mod -->\r\n") && gc.Contains("<Land_Tactical_Camera_Locked> False </Land_Tactical_Camera_Locked> <!-- Force Commander camera mod -->"));
        Check("other mod: saved copy", Txt(X("GameData", "GameConstants.xml.before-camera-mod")) == other);
        Call("DoInstall");   // again: must still build from the saved copy
        Check("other mod: reinstall same", Txt(X("GameData", "GameConstants.xml")) == gc);
        Call("DoRestore");
        Check("other mod: put back", Txt(X("GameData", "GameConstants.xml")) == other && !File.Exists(X("GameData", "GameConstants.xml.before-camera-mod")));
        Check("other mod: TacticalCameras removed", !File.Exists(X("GameData", "TacticalCameras.xml")));

        // 5. the user's own pre-installer setup
        Reset(true);
        Check("legacy: detected as installed", State(0) == "Installed." && State(1) == "Installed.");
        Call("DoInstall");
        foreach (var d in Dirs) CheckInstalled(d, "legacy reinstall");
        Check("legacy: no saved copies", !Dirs.Any(d => Directory.GetFiles(Path.Combine(root, d, @"Data\XML"), "*.before-camera-mod").Any()));
        Reset(true);
        Call("DoRestore");
        foreach (var d in Dirs) {
            Check("legacy restore " + d + " exe original", Same(P(d, "StarWarsG.exe"), P(d, "StarWarsG.exe.orig.src")));
            Check("legacy restore " + d + " GameConstants removed", !File.Exists(X(d, "GameConstants.xml")));
            var tc = Txt(X(d, "TacticalCameras.xml"));   // hand-made copy also trimmed the last line, so it's reverted rather than deleted
            Check("legacy restore " + d + " TacticalCameras stock", tc == null || !tc.Contains("Unlocked = copy of Land_Mode"));
        }

        // 6. patched exe without its backup still recognised (runs reversed)
        Reset(false);
        Call("DoInstall");
        File.Delete(P("GameData", "StarWarsG.exe.orig"));
        Call("LoadFolder");
        Check("no backup: recognised", State(0) == "Installed.");
        Call("DoRestore");
        Check("no backup: restored", Same(P("GameData", "StarWarsG.exe"), P("GameData", "StarWarsG.exe.orig.src")));

        // 7. unknown exe
        Reset(false);
        File.WriteAllBytes(P("corruption", "StarWarsG.exe"), new byte[] { 1, 2, 3 });
        Call("LoadFolder");
        Check("unknown exe: disabled", !Box(1).Enabled && Box(0).Enabled);
        Reset(false);

        timer.Stop();
        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        return fails;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumThreadWindows(int tid, EnumProc f, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cls, string t);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    // answers every message box: logs its text, Yes/OK
    static void CloseDialogs() {
        EnumThreadWindows(GetCurrentThreadId(), (h, l) => {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            if (sb.ToString() != "#32770") return true;
            var txt = FindWindowEx(h, IntPtr.Zero, "Static", null);
            txt = FindWindowEx(h, txt, "Static", null);   // first Static is the icon
            var m = new StringBuilder(2048);
            GetWindowText(txt, m, 2048);
            Console.WriteLine("  [dialog] " + m.ToString().Replace("\r\n", " / "));
            PostMessage(h, 0x0111, (IntPtr)6, IntPtr.Zero);   // WM_COMMAND IDYES
            PostMessage(h, 0x0111, (IntPtr)1, IntPtr.Zero);   // WM_COMMAND IDOK
            return true;
        }, IntPtr.Zero);
    }
}
