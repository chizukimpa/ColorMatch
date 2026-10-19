// Setup.cs - instalador do ColorMatch (sem UAC, per-user)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Setup
{
    const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);
    static string InstallDir;
    static bool Autostart = true;
    static bool Shortcut = true;
    static bool UninstallMode = false;

    [STAThread]
    static void Main(string[] args)
    {
        foreach (var a in args) if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) UninstallMode = true;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (UninstallMode)
        {
            DoUninstall();
            return;
        }

        // se já instalado, oferece desinstalar/reinstalar
        string existing = GetInstalledLocation();
        if (existing != null && Directory.Exists(existing))
        {
            var r = MessageBox.Show("ColorMatch já está instalado em:\n" + existing +
                "\n\nDeseja reinstalar (sobrescrever)?\n(Clique em Não para desinstalar.)",
                "ColorMatch", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.No) { DoUninstall(); return; }
        }

        using (var f = new InstallForm())
        {
            if (f.ShowDialog() != DialogResult.OK) return;
            InstallDir = f.ChosenPath;
            Autostart = f.Autostart;
            Shortcut = f.Shortcut;
        }
        try { DoInstall(); }
        catch (Exception ex) { MessageBox.Show("Falha na instalação:\n" + ex.Message, "ColorMatch", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    static string GetInstalledLocation()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorMatch"))
            {
                if (k == null) return null;
                return k.GetValue("InstallLocation") as string;
            }
        }
        catch { return null; }
    }

    static void DoInstall()
    {
        // para o app se estiver rodando
        foreach (var p in Process.GetProcessesByName("ColorMatch")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }

        Directory.CreateDirectory(InstallDir);
        // extrai arquivos embutidos
        ExtractResource("ColorMatch.exe", Path.Combine(InstallDir, "ColorMatch.exe"));
        ExtractResource("app.ico", Path.Combine(InstallDir, "app.ico"));

        // atalho no menu iniciar
        if (Shortcut)
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            string progDir = Path.Combine(startMenu, "Programs", "ColorMatch");
            Directory.CreateDirectory(progDir);
            CreateShortcut(Path.Combine(progDir, "ColorMatch.lnk"), Path.Combine(InstallDir, "ColorMatch.exe"), "ColorMatch");
        }

        // autostart (sem UAC)
        if (Autostart)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                k.SetValue("ColorMatch", "\"" + Path.Combine(InstallDir, "ColorMatch.exe") + "\" /invisivel");
        }

        // entrada de desinstalação (per-user, sem UAC)
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorMatch"))
        {
            k.SetValue("DisplayName", "ColorMatch");
            k.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "ColorMatch.exe") + "\""); // placeholder, replaced below
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("DisplayIcon", Path.Combine(InstallDir, "app.ico"));
            k.SetValue("Publisher", "ColorMatch");
            k.SetValue("NoModify", 1);
            k.SetValue("NoRepair", 1);
        }
        // UninstallString aponta para o próprio instalador se existir, senão para um batch
        string setupExe = Process.GetCurrentProcess().MainModule.FileName;
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorMatch", true))
            k.SetValue("UninstallString", "\"" + setupExe + "\" /uninstall");

        // copia o instalador para a pasta de instalação (para desinstalar depois)
        try { File.Copy(setupExe, Path.Combine(InstallDir, "Uninstall.exe"), true); } catch { }
        // atualiza UninstallString para o copiado
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorMatch", true))
            k.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "Uninstall.exe") + "\" /uninstall");

        MessageBox.Show("ColorMatch instalado com sucesso em:\n" + InstallDir +
            "\n\nInicie pelo Menu Iniciar ou atalho.", "ColorMatch", MessageBoxButtons.OK, MessageBoxIcon.Information);

        var launch = MessageBox.Show("Executar ColorMatch agora?", "ColorMatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (launch == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(Path.Combine(InstallDir, "ColorMatch.exe")) { UseShellExecute = true });
    }

    static void DoUninstall()
    {
        string dir = GetInstalledLocation();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            // tenta a pasta padrão
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ColorMatch");
        }

        foreach (var p in Process.GetProcessesByName("ColorMatch")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }

        // remove atalho
        try
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            string progDir = Path.Combine(startMenu, "Programs", "ColorMatch");
            if (Directory.Exists(progDir)) Directory.Delete(progDir, true);
        }
        catch { }

        // remove autostart + entrada de desinstalação
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) k.DeleteValue("ColorMatch", false); } catch { }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorMatch", false); } catch { }

        // remove arquivos (deixa a pasta se não conseguir apagar o próprio exe)
        try
        {
            foreach (var f in Directory.GetFiles(dir)) { try { File.Delete(f); } catch { } }
            // agenda exclusão da pasta no próximo reboot se necessário
            try { Directory.Delete(dir, true); } catch { }
        }
        catch { }

        // agenda a remoção do próprio instalador (e da pasta) no próximo reboot
        try
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            MoveFileEx(self, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        catch { }

        MessageBox.Show("ColorMatch desinstalado.\nSe sobrar algum arquivo na pasta, pode apagar manualmente:\n" + dir,
            "ColorMatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void ExtractResource(string name, string dest)
    {
        var asm = Assembly.GetExecutingAssembly();
        // tenta vários nomes possíveis
        string[] candidates = new string[] { name, "ColorMatch." + name, "Setup." + name };
        Stream s = null;
        foreach (var c in candidates) { s = asm.GetManifestResourceStream(c); if (s != null) break; }
        if (s == null)
        {
            // lista recursos disponíveis para debug
            var all = asm.GetManifestResourceNames();
            throw new Exception("Recurso não encontrado: " + name + "\nDisponíveis: " + string.Join(", ", all));
        }
        using (s)
        using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            s.CopyTo(fs);
    }

    static void CreateShortcut(string lnkPath, string target, string desc)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            sc.WorkingDirectory = Path.GetDirectoryName(target);
            sc.Description = desc;
            sc.IconLocation = target + ",0";
            sc.Save();
        }
        catch { }
    }
}

class InstallForm : Form
{
    public string ChosenPath;
    public bool Autostart = true;
    public bool Shortcut = true;
    TextBox txt;
    CheckBox chkAuto, chkShortcut;

    public InstallForm()
    {
        Text = "Instalar ColorMatch";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(460, 230);
        BackColor = System.Drawing.Color.White;

        var lbl = new Label { Text = "ColorMatch será instalado em:", Location = new Point(16, 16), AutoSize = true };
        Controls.Add(lbl);

        txt = new TextBox { Location = new Point(16, 40), Size = new Size(340, 24),
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ColorMatch") };
        Controls.Add(txt);

        var btnBrowse = new Button { Text = "Procurar...", Location = new Point(364, 38), Size = new Size(80, 28) };
        btnBrowse.Click += (s, e) =>
        {
            using (var d = new FolderBrowserDialog { SelectedPath = txt.Text })
                if (d.ShowDialog() == DialogResult.OK) txt.Text = d.SelectedPath;
        };
        Controls.Add(btnBrowse);

        chkAuto = new CheckBox { Text = "Iniciar com o Windows (recomendado)", Location = new Point(16, 80), Checked = true, AutoSize = true };
        Controls.Add(chkAuto);
        chkShortcut = new CheckBox { Text = "Criar atalho no Menu Iniciar", Location = new Point(16, 108), Checked = true, AutoSize = true };
        Controls.Add(chkShortcut);

        var btnInstall = new Button { Text = "Instalar", Location = new Point(250, 170), Size = new Size(90, 32), DialogResult = DialogResult.OK };
        btnInstall.Click += (s, e) => { ChosenPath = txt.Text; Autostart = chkAuto.Checked; Shortcut = chkShortcut.Checked; };
        Controls.Add(btnInstall);

        var btnCancel = new Button { Text = "Cancelar", Location = new Point(350, 170), Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
        Controls.Add(btnCancel);

        AcceptButton = btnInstall;
        CancelButton = btnCancel;
    }
}
