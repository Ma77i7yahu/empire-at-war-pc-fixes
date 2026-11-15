// Star Wars: Empire at War (Steam 64-bit) - Force Commander-style camera mod installer.
// Per game (Empire at War in GameData, Forces of Corruption in corruption):
//  - StarWarsG.exe gets the Q/E rotate + Z/C tilt code cave (PatchData runs); the untouched
//    exe is kept as "StarWarsG.exe.orig".
//  - Data\XML\GameConstants.xml and TacticalCameras.xml are written from the game's own MEG
//    archives (or from a loose copy another mod already put there, which is saved as
//    "<name>.before-camera-mod" and put back on removal), with the land camera unlocked and
//    the "Unlocked" camera made a normal land view.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

class Run {
    public readonly int Offset; public readonly byte[] Old, New;
    public Run(int offset, byte[] old, byte[] nw) { Offset = offset; Old = old; New = nw; }
}

// one game's MEG archives: finds the copy of a file the game itself would load
class Megs {
    readonly string dataDir;
    public Megs(string dataDir) { this.dataDir = dataDir; }

    // highest priority first: the patch archives load after MegaFiles.xml's list, later ones win
    List<string> Order() {
        var list = new List<string>();
        string mf = Path.Combine(dataDir, "MegaFiles.xml");
        if (File.Exists(mf))
            foreach (Match m in Regex.Matches(File.ReadAllText(mf, Latin1), @"<File>\s*([^<]+?)\s*</File>", RegexOptions.IgnoreCase))
                list.Add(Path.Combine(Path.GetDirectoryName(dataDir), m.Groups[1].Value));
        list.Reverse();
        list.InsertRange(0, new[] { "64Patch.meg", "Patch2.meg", "Patch.meg" }.Select(n => Path.Combine(dataDir, n)));
        return list;
    }

    public byte[] Find(string name) {   // name like DATA\XML\GAMECONSTANTS.XML
        foreach (var meg in Order()) {
            if (!File.Exists(meg)) continue;
            var b = Read(meg, name);
            if (b != null) return b;
        }
        return null;
    }

    // MEG v1: u32 nNames, u32 nFiles, names (u16 len + chars), 20-byte records (crc, index, size, start, nameIndex)
    static byte[] Read(string meg, string want) {
        try {
            using (var fs = File.OpenRead(meg))
            using (var r = new BinaryReader(fs)) {
                uint nn = r.ReadUInt32(), nf = r.ReadUInt32();
                if (nn > 1000000 || nf > 1000000) return null;
                var names = new string[nn];
                for (int i = 0; i < nn; i++) names[i] = Latin1.GetString(r.ReadBytes(r.ReadUInt16()));
                for (int i = 0; i < nf; i++) {
                    r.ReadUInt32(); r.ReadUInt32();
                    uint size = r.ReadUInt32(), start = r.ReadUInt32(), ni = r.ReadUInt32();
                    if (ni >= nn || !string.Equals(names[ni].Replace('/', '\\'), want, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((long)start + size > fs.Length) return null;
                    fs.Position = start;
                    return r.ReadBytes((int)size);
                }
            }
        } catch (IOException) { }   // includes EndOfStreamException
        return null;
    }

    public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
}

// an XML the mod edits; texts are handled as Latin-1 so every byte round-trips
abstract class XmlEdit {
    public abstract string Name { get; }               // file name in Data\XML
    public abstract bool IsOurs(string text, string stock);
    public abstract string Apply(string text);         // null when the expected part isn't there
    public abstract string Revert(string text, string stock);
    public string MegName { get { return @"DATA\XML\" + Name.ToUpperInvariant(); } }
    protected static string ReplaceOnce(Regex re, string text, MatchEvaluator f) {
        return re.Matches(text).Count == 1 ? re.Replace(text, f, 1) : null;
    }
}

class GameConstantsEdit : XmlEdit {
    const string Marker = "<!-- Force Commander camera mod -->";
    static readonly Regex Locked = new Regex(@"(<Land_Tactical_Camera_Locked>\s*)(True|False)(\s*</Land_Tactical_Camera_Locked>)( " + Regex.Escape(Marker) + ")?");
    public override string Name { get { return "GameConstants.xml"; } }
    // the marker, or the pre-installer copy (stock with only the value flipped)
    public override bool IsOurs(string text, string stock) {
        return text.Contains(Marker) || (stock != null && text == Set(stock, "False", false));
    }
    static string Set(string text, string v, bool marker) {
        return ReplaceOnce(Locked, text, m => m.Groups[1].Value + v + m.Groups[3].Value + (marker ? " " + Marker : ""));
    }
    public override string Apply(string text) { return Set(text, "False", true); }
    public override string Revert(string text, string stock) { return Set(text, "True", false); }
}

class TacticalCamerasEdit : XmlEdit {
    const string Marker = "Unlocked = copy of Land_Mode";
    static readonly Regex Block = new Regex(@"[ \t]*(<!--[^\r\n]*-->\r?\n)?[ \t]*<TacticalCamera Name=""Unlocked"">.*?</TacticalCamera>", RegexOptions.Singleline);
    readonly string block;
    public TacticalCamerasEdit(string block) { this.block = block; }
    public override string Name { get { return "TacticalCameras.xml"; } }
    public override bool IsOurs(string text, string stock) { return text.Contains(Marker); }
    public override string Apply(string text) { return ReplaceOnce(Block, text, m => block); }
    public override string Revert(string text, string stock) {
        var m = stock == null ? null : Block.Match(stock);
        return m == null || !m.Success ? null : ReplaceOnce(Block, text, x => m.Value);
    }
}

class Game {
    public string Key, Title, DirName, OrigMd5, PatchedMd5;
    public Run[] Runs;
    public XmlEdit[] Xmls;
    public CheckBox Box;
    public Label State;
    // filled by Load
    public string Dir;
    public byte[] BaseExe;
    public bool Installed;
    public string Exe { get { return Path.Combine(Dir, "StarWarsG.exe"); } }
    public string Backup { get { return Exe + ".orig"; } }
    public string XmlDir { get { return Path.Combine(Dir, @"Data\XML"); } }
}

class InstallerForm : Form {
    const string SavedSuffix = ".before-camera-mod";
    const string GameFolder = @"steamapps\common\Star Wars Empire at War";

    readonly TextBox folder = new TextBox();
    readonly Label status = new Label();
    readonly TextBox info = new TextBox();
    readonly Button install = new Button(), restore = new Button();
    readonly List<Game> games = new List<Game>();

    InstallerForm() {
        Text = "Star Wars: Empire at War - Force Commander Camera Mod";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        games.Add(new Game { Key = "eaw", Title = "Empire at War", DirName = "GameData",
            OrigMd5 = PatchData.EawOriginalMd5, PatchedMd5 = PatchData.EawPatchedMd5, Runs = PatchData.EawRuns,
            Xmls = new XmlEdit[] { new GameConstantsEdit(), new TacticalCamerasEdit(PatchData.EawUnlockedBlock) } });
        games.Add(new Game { Key = "foc", Title = "Forces of Corruption", DirName = "corruption",
            OrigMd5 = PatchData.FocOriginalMd5, PatchedMd5 = PatchData.FocPatchedMd5, Runs = PatchData.FocRuns,
            Xmls = new XmlEdit[] { new GameConstantsEdit(), new TacticalCamerasEdit(PatchData.FocUnlockedBlock) } });

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        Controls.Add(root);

        var fl = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        fl.Controls.Add(new Label { Text = "Game folder:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        folder.Width = 470;
        folder.Margin = new Padding(0, 3, 4, 0);
        folder.Leave += (s, e) => LoadFolder();
        fl.Controls.Add(folder);
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (s, e) => {
            using (var d = new FolderBrowserDialog { Description = "Select the \"Star Wars Empire at War\" folder (the one containing \"GameData\" and \"corruption\")" }) {
                if (Directory.Exists(folder.Text)) d.SelectedPath = folder.Text;
                if (d.ShowDialog(this) == DialogResult.OK) { folder.Text = d.SelectedPath; LoadFolder(); }
            }
        };
        fl.Controls.Add(browse);
        root.Controls.Add(fl);
        status.AutoSize = true; status.MaximumSize = new Size(640, 0); status.Margin = new Padding(0, 4, 0, 6);
        root.Controls.Add(status);

        var g = new GroupBox { Text = "Install the camera controls in", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(640, 0), Padding = new Padding(8, 4, 8, 6) };
        var gp = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 20) };
        g.Controls.Add(gp);
        root.Controls.Add(g);
        foreach (var game in games) {
            game.Box = new CheckBox { Text = game.Title, AutoSize = true, Width = 170, Margin = new Padding(0, 3, 12, 3) };
            game.State = new Label { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 6, 0, 3) };
            gp.Controls.Add(game.Box);
            gp.Controls.Add(game.State);
        }

        info.Multiline = true; info.ReadOnly = true; info.ScrollBars = ScrollBars.Vertical;
        info.Size = new Size(640, 260); info.BackColor = SystemColors.Info;
        info.Text =
            "Adds Force Commander-style camera keys to tactical battles (land and space):\r\n" +
            "  Q / E  -  rotate the camera left / right (90 degrees a second)\r\n" +
            "  Z / C  -  tilt the camera up (more top-down) / down (flatter) (45 degrees a second)\r\n" +
            "Zoom, WASD scrolling and the mouse controls are unchanged.\r\n\r\n" +
            "The stock land camera can't rotate at all, so the land camera is also unlocked: land battles use the " +
            "game's \"Unlocked\" camera, set up to look and zoom like the normal land view (tilt range 10-85 degrees). " +
            "The space camera already rotates and is otherwise untouched.\r\n\r\n" +
            "Files changed (per game): StarWarsG.exe (original kept as StarWarsG.exe.orig), and Data\\XML\\GameConstants.xml " +
            "+ TacticalCameras.xml, built from the game's own data files. Untick a game and press Install, or use Restore, " +
            "to take the mod back out.\r\n\r\n" +
            "Made for the Steam 64-bit release (2017 update). Steam updates or \"Verify integrity of game files\" put the " +
            "original exe back; run the installer again afterwards.\r\n\r\n" +
            "Status: tested in land and space battles in both Empire at War and Forces of Corruption.";
        root.Controls.Add(info);

        var bl = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        install.Text = "Install selected"; install.AutoSize = true;
        install.Click += (s, e) => DoInstall();
        restore.Text = "Restore original game"; restore.AutoSize = true;
        restore.Click += (s, e) => DoRestore();
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (s, e) => Close();
        bl.Controls.AddRange(new Control[] { install, restore, close });
        root.Controls.Add(bl);

        string here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        folder.Text = FindRoot(here) ?? SteamGuess() ?? "";
        LoadFolder();
    }

    static string Md5(byte[] b) {
        using (var m = MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    // accepts the install folder, GameData or corruption
    string FindRoot(string dir) {
        if (dir.Length == 0 || !Directory.Exists(dir)) return null;
        foreach (var d in new[] { dir, Path.GetDirectoryName(dir) })
            if (d != null && games.Any(g => File.Exists(Path.Combine(d, g.DirName, "StarWarsG.exe")))) return d;
        return null;
    }

    // the Steam folder and its other libraries
    string SteamGuess() {
        var libs = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
            try {
                using (var k = hive.OpenSubKey(hive == Registry.CurrentUser ? @"Software\Valve\Steam" : @"SOFTWARE\WOW6432Node\Valve\Steam")) {
                    var p = k == null ? null : (k.GetValue("SteamPath") ?? k.GetValue("InstallPath")) as string;
                    if (p != null) libs.Add(p.Replace('/', '\\'));
                }
            } catch { }
        }
        foreach (var s in libs.ToArray()) {
            string vdf = Path.Combine(s, @"steamapps\libraryfolders.vdf");
            try {
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            } catch { }
        }
        libs.Add(@"C:\Program Files (x86)\Steam");
        foreach (var l in libs) {
            var r = FindRoot(Path.Combine(l, GameFolder));
            if (r != null) return r;
        }
        return null;
    }

    static byte[] Unpatch(Game g, byte[] exe) {
        var img = (byte[])exe.Clone();
        foreach (var r in g.Runs) {
            if (r.Offset + r.Old.Length > img.Length) return null;
            Buffer.BlockCopy(r.Old, 0, img, r.Offset, r.Old.Length);
        }
        return img;
    }
    static byte[] Patch(Game g) {
        var img = (byte[])g.BaseExe.Clone();
        foreach (var r in g.Runs) Buffer.BlockCopy(r.New, 0, img, r.Offset, r.New.Length);
        if (Md5(img) != g.PatchedMd5) throw new InvalidOperationException("The patched " + g.Title + " exe came out wrong.");
        return img;
    }

    void LoadFolder() {
        string dir = FindRoot(folder.Text.Trim().Trim('"'));
        install.Enabled = restore.Enabled = false;
        int found = 0;
        foreach (var g in games) {
            g.Dir = null; g.BaseExe = null; g.Installed = false;
            g.Box.Enabled = g.Box.Checked = false;
            g.State.ForeColor = Color.DimGray;
            g.State.Text = "";
            if (dir == null) continue;
            g.Dir = Path.Combine(dir, g.DirName);
            if (!File.Exists(g.Exe)) { g.State.Text = "Not installed (no " + g.DirName + "\\StarWarsG.exe)."; continue; }
            byte[] cur;
            try { cur = File.ReadAllBytes(g.Exe); }
            catch (Exception ex) { g.State.ForeColor = Color.DarkRed; g.State.Text = "Can't read the exe: " + ex.Message; continue; }
            string md5 = Md5(cur);
            if (md5 == g.OrigMd5) g.BaseExe = cur;
            else {
                var un = Unpatch(g, cur);
                if (un != null && Md5(un) == g.OrigMd5) g.BaseExe = un;
                else if (File.Exists(g.Backup)) {
                    var bak = File.ReadAllBytes(g.Backup);
                    if (Md5(bak) == g.OrigMd5) g.BaseExe = bak;
                }
            }
            if (g.BaseExe == null) {
                g.State.ForeColor = Color.DarkRed;
                g.State.Text = "This StarWarsG.exe isn't the Steam 64-bit version the mod was made for (MD5 " + g.OrigMd5.ToUpper() +
                               "). If you've modified it, put the original back (Steam: Verify integrity of game files).";
                continue;
            }
            found++;
            g.Installed = md5 == g.PatchedMd5;
            g.Box.Enabled = true;
            g.Box.Checked = true;
            g.State.ForeColor = g.Installed ? Color.DarkGreen : Color.Black;
            g.State.Text = g.Installed ? "Installed." : md5 == g.OrigMd5 ? "Original game - ready to install." : "Exe recognised - ready to install.";
        }
        if (dir == null) {
            status.ForeColor = Color.DarkRed;
            status.Text = folder.Text.Trim().Length == 0 ? "Choose the folder where Star Wars Empire at War is installed."
                                                         : "Neither GameData\\StarWarsG.exe nor corruption\\StarWarsG.exe is in that folder.";
            return;
        }
        status.ForeColor = found > 0 ? Color.DarkGreen : Color.DarkRed;
        status.Text = found > 0 ? "Found the game. Tick where you want the camera controls; untick to remove them."
                                : "No supported game exe was found in that folder.";
        install.Enabled = found > 0;
        restore.Enabled = games.Any(g => g.BaseExe != null && (g.Installed || HasOurXml(g)));
    }

    bool HasOurXml(Game g) {
        var megs = new Megs(Path.Combine(g.Dir, "Data"));
        foreach (var x in g.Xmls) {
            string p = Path.Combine(g.XmlDir, x.Name);
            if (File.Exists(p) && x.IsOurs(ReadText(p), Stock(megs, x))) return true;
        }
        return false;
    }

    static string ReadText(string p) { return Megs.Latin1.GetString(File.ReadAllBytes(p)); }
    static void WriteText(string p, string t) {
        File.WriteAllBytes(p + ".tmp", Megs.Latin1.GetBytes(t));
        File.Copy(p + ".tmp", p, true);
        File.Delete(p + ".tmp");
    }
    static string Stock(Megs megs, XmlEdit x) {
        var b = megs.Find(x.MegName);
        return b == null ? null : Megs.Latin1.GetString(b);
    }

    // works out every file write first, so nothing is touched if a file can't be built
    class Plan { public List<Action> Steps = new List<Action>(); public List<string> Done = new List<string>(); }

    void PlanInstall(Game g, Plan plan) {
        byte[] img = Patch(g);
        var megs = new Megs(Path.Combine(g.Dir, "Data"));
        foreach (var x in g.Xmls) {
            string p = Path.Combine(g.XmlDir, x.Name), saved = p + SavedSuffix, stock = Stock(megs, x);
            string src;
            bool save = false;
            if (File.Exists(saved)) src = ReadText(saved);
            else if (File.Exists(p)) {
                src = ReadText(p);
                if (x.IsOurs(src, stock)) src = x.Revert(src, stock);
                else save = true;   // another mod's copy: keep it to put back later
            } else src = stock;
            if (src == null) throw new InvalidOperationException("Couldn't find the game's " + x.Name + " in " + g.DirName + "\\Data.");
            string text = x.Apply(src);
            if (text == null) throw new InvalidOperationException(g.DirName + "\\Data\\XML\\" + x.Name + " doesn't have the part the mod changes (is it from another mod?).");
            plan.Steps.Add(() => {
                Directory.CreateDirectory(g.XmlDir);
                if (save) File.Copy(p, saved);
                WriteText(p, text);
            });
            if (save) plan.Done.Add("Your existing " + g.DirName + "\\Data\\XML\\" + x.Name + " (from another mod?) was changed too; its old version is saved as \"" + x.Name + SavedSuffix + "\" and comes back when the mod is removed.");
        }
        plan.Steps.Add(() => {
            if (File.Exists(g.Backup) && Md5(File.ReadAllBytes(g.Backup)) != g.OrigMd5) {
                string old = g.Exe + ".orig-old";
                for (int i = 2; File.Exists(old); i++) old = g.Exe + ".orig-old" + i;
                File.Move(g.Backup, old);
            }
            if (!File.Exists(g.Backup)) File.WriteAllBytes(g.Backup, g.BaseExe);
            File.WriteAllBytes(g.Exe + ".tmp", img);
            File.Copy(g.Exe + ".tmp", g.Exe, true);
            File.Delete(g.Exe + ".tmp");
        });
    }

    void PlanRemove(Game g, Plan plan) {
        var megs = new Megs(Path.Combine(g.Dir, "Data"));
        foreach (var x in g.Xmls) {
            string p = Path.Combine(g.XmlDir, x.Name), saved = p + SavedSuffix, stock = Stock(megs, x);
            if (File.Exists(saved)) { plan.Steps.Add(() => { File.Copy(saved, p, true); File.Delete(saved); }); continue; }
            if (!File.Exists(p)) continue;
            string cur = ReadText(p);
            if (!x.IsOurs(cur, stock)) continue;
            string back = x.Revert(cur, stock);
            if (back == null || back == stock) plan.Steps.Add(() => File.Delete(p));
            else plan.Steps.Add(() => WriteText(p, back));
        }
        if (Md5(File.ReadAllBytes(g.Exe)) != g.OrigMd5) {
            byte[] b = g.BaseExe;
            plan.Steps.Add(() => {
                File.WriteAllBytes(g.Exe + ".tmp", b);
                File.Copy(g.Exe + ".tmp", g.Exe, true);
                File.Delete(g.Exe + ".tmp");
            });
        }
    }

    void Execute(Plan plan, string msg) {
        try {
            foreach (var s in plan.Steps) s();
        } catch (IOException ex) {
            MessageBox.Show(this, "Couldn't write the game files. Close Empire at War if it's running and try again.\r\n\r\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadFolder();
            return;
        } catch (UnauthorizedAccessException ex) {
            MessageBox.Show(this, "Windows denied access to the game folder. Right-click the installer and choose \"Run as administrator\".\r\n\r\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadFolder();
            return;
        }
        if (plan.Done.Count > 0) msg += "\r\n\r\n" + string.Join("\r\n\r\n", plan.Done);
        MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadFolder();
    }

    void DoInstall() {
        var plan = new Plan();
        var on = new List<string>(); var off = new List<string>();
        try {
            foreach (var g in games.Where(x => x.BaseExe != null)) {
                if (g.Box.Checked) { PlanInstall(g, plan); on.Add(g.Title); }
                else { int n = plan.Steps.Count; PlanRemove(g, plan); if (plan.Steps.Count > n) off.Add(g.Title); }
            }
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        string msg = on.Count > 0 ? "Camera controls installed in " + string.Join(" and ", on) + ".\r\n\r\nIn a battle: Q / E rotate, Z / C tilt." : "Nothing is installed now.";
        if (off.Count > 0) msg += "\r\n\r\nRemoved from " + string.Join(" and ", off) + ".";
        if (on.Count > 0) msg += "\r\n\r\nThe untouched exe is saved as \"StarWarsG.exe.orig\" next to the game's exe.";
        Execute(plan, msg);
    }

    void DoRestore() {
        if (MessageBox.Show(this, "Remove the camera mod from every game and put the original files back?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var plan = new Plan();
        try { foreach (var g in games.Where(x => x.BaseExe != null)) PlanRemove(g, plan); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        Execute(plan, "The original game has been restored. The camera mod is removed.");
    }

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
    }
}
