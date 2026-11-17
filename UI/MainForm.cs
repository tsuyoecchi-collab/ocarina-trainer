using System.Diagnostics;
using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

public enum PageId
{
    Cheats,
    Scanner,
    Stats,
    Collectibles,
    Waypoints,
    Settings,
    Guide
}

public sealed class MainForm : Form
{
    private readonly ConfigFile _configFile;
    private readonly TrainerEngine _engine = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly Dictionary<PageId, TrainerPage> _pages = new();
    private readonly Dictionary<PageId, Button> _navButtons = new();

    private readonly ComboBox _processCombo = Ui.Combo(215);
    private readonly Button _refreshButton = Ui.Button("Actualizar");
    private readonly Button _attachButton = Ui.Button("Conectar", primary: true);
    private readonly Label _processStatus = Ui.Hint("");

    private readonly Label _connState = new();
    private readonly Label _connDetail = new();
    private readonly Label _connBase = new();

    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    private readonly Label _status = new();
    private readonly Label _hints = new();
    private readonly System.Windows.Forms.Timer _freezeTimer = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;

    private TrainerProfile _profile;
    private GameMemory? _memory;
    private PageId _currentPage = PageId.Collectibles;
    private bool _disconnectNotified;
    private DateTime _nextAutoReconnectUtc = DateTime.MinValue;

    public MainForm()
    {
        _configFile = ConfigStore.Load();
        _profile = _configFile.GetProfile(_configFile.LastProcess);
        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        Text = "Phykro Trainer";
        Icon = _appIcon;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 730);
        MinimumSize = new Size(1000, 640);
        DoubleBuffered = true;
        KeyPreview = true;

        BuildLayout();
        CreatePages();
        ShowPage(PageId.Collectibles);

        _engine.Message += message => Notify(message);

        _freezeTimer.Interval = Math.Clamp(_profile.FreezeIntervalMs, 10, 2000);
        _freezeTimer.Tick += (_, _) => _engine.FreezeTick();
        _refreshTimer.Interval = 300;
        _refreshTimer.Tick += OnUiTick;

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Phykro Trainer",
            Visible = true
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostrar / ocultar", null, (_, _) => ToggleWindow());
        menu.Items.Add("Restaurar valores originales", null, (_, _) => RestoreOriginals());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ToggleWindow();

        Load += (_, _) => OnLoaded();
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) Hide();
        };
    }

    public TrainerEngine Engine => _engine;
    public TrainerProfile Profile => _profile;
    public GameMemory? Memory => _memory;

    /// <summary>Módulo del juego que corresponde al perfil activo (proceso actual).</summary>
    public IGameModule GameModule => GameModules.Resolve(_profile.ProcessName);

    // ---------------------------------------------------------------- interfaz

    private readonly List<PageId> _topTabs = new() { PageId.Collectibles, PageId.Stats, PageId.Cheats };
    private Panel _topBar = null!;
    private Panel _bottomBar = null!;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg, Margin = new Padding(0), Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.Controls.Add(BuildTopGameBar(), 0, 0);
        root.Controls.Add(_pageHost, 0, 1);
        root.Controls.Add(BuildBottomGameBar(), 0, 2);
        Controls.Add(root);

        KeyDown += (_, e) =>
        {
            if (ActiveControl is TextBoxBase or ComboBox) return;
            if (e.KeyCode == Keys.Q || e.KeyCode == Keys.PageUp) { CycleTopTab(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.E || e.KeyCode == Keys.PageDown) { CycleTopTab(1); e.Handled = true; }
        };
    }

    private void CycleTopTab(int delta)
    {
        int idx = _topTabs.IndexOf(_currentPage);
        if (idx < 0) { ShowPage(PageId.Collectibles); return; }
        ShowPage(_topTabs[(idx + delta + _topTabs.Count) % _topTabs.Count]);
    }

    private Control BuildTopGameBar()
    {
        _topBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(54, 40, 22), Margin = new Padding(0), Padding = new Padding(12, 0, 12, 0) };
        _topBar.Paint += (_, e) => { using var line = new Pen(Color.FromArgb(145, 114, 62), 1.5f); e.Graphics.DrawLine(line, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1); };

        var leftConn = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0) };
        _connState.AutoSize = true; _connState.Font = Theme.Small; _connState.ForeColor = Theme.Gold; _connState.Text = "○ Sin conexión"; _connState.BackColor = Color.Transparent; _connState.Margin = new Padding(0, 4, 8, 0);
        _processCombo.Width = 140; _processCombo.Margin = new Padding(0, 1, 4, 0);
        _attachButton.Height = 26; _attachButton.Width = 76; _attachButton.Margin = new Padding(0, 0, 4, 0);
        _refreshButton.Height = 26; _refreshButton.Width = 30; _refreshButton.Text = "↻"; _refreshButton.Margin = new Padding(0);
        _refreshButton.Click += (_, _) => RefreshProcessList();
        _attachButton.Click += (_, _) => ConnectSelected();
        _processCombo.SelectedIndexChanged += (_, _) => UpdateProcessStatus();
        leftConn.Controls.AddRange(new Control[] { _connState, _processCombo, _attachButton, _refreshButton });

        var centerTabs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0) };
        centerTabs.Controls.Add(MakeShoulderButton("LB", () => CycleTopTab(-1)));
        AddTopNav(centerTabs, PageId.Collectibles, "Mapa");
        AddTopNav(centerTabs, PageId.Stats, "Estadísticas");
        AddTopNav(centerTabs, PageId.Cheats, "Trucos");
        centerTabs.Controls.Add(MakeShoulderButton("RB", () => CycleTopTab(1)));

        _topBar.Controls.Add(leftConn);
        _topBar.Controls.Add(centerTabs);
        void CenterMenu() { centerTabs.Location = new Point(Math.Max(leftConn.Right + 12, (_topBar.Width - centerTabs.Width) / 2), 0); }
        _topBar.Resize += (_, _) => CenterMenu();
        centerTabs.Resize += (_, _) => CenterMenu();
        return _topBar;
    }

    private static Button MakeShoulderButton(string label, Action onClick)
    {
        var btn = new Button { Text = label, Width = 34, Height = 22, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(236, 226, 200), ForeColor = Color.FromArgb(46, 34, 18), Font = new Font("Segoe UI", 8f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(10, 13, 10, 0), UseVisualStyleBackColor = false, FlatAppearance = { BorderSize = 0 } };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void AddTopNav(Control parent, PageId page, string text)
    {
        var button = new Button { Text = text, TextAlign = ContentAlignment.MiddleCenter, Width = 132, Height = 47, FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Color.FromArgb(245, 236, 214), Font = new Font("Georgia", 11.5f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(4, 0, 4, 0), UseVisualStyleBackColor = false, FlatAppearance = { BorderSize = 0, MouseOverBackColor = Color.FromArgb(82, 61, 31) } };
        button.Click += (_, _) => ShowPage(page);
        parent.Controls.Add(button);
        _navButtons[page] = button;
    }

    private Control BuildBottomGameBar()
    {
        _bottomBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(46, 34, 19), Margin = new Padding(0), Padding = new Padding(14, 0, 16, 0) };
        _bottomBar.Paint += (_, e) => { using var line = new Pen(Color.FromArgb(132, 102, 56), 1f); e.Graphics.DrawLine(line, 0, 0, _bottomBar.Width, 0); };

        _status.AutoSize = false; _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; _status.ForeColor = Theme.Muted; _status.Font = Theme.Small; _status.Text = "Listo. Abre el juego y pulsa Conectar."; _status.BackColor = Color.Transparent;
        var rightActions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };

        AddBottomAction(rightActions, "Catálogo / Hyrule", "X", () => { ShowPage(PageId.Collectibles); _collectiblesPage.ToggleCatalogView(); });
        AddBottomAction(rightActions, "Escáner", "Y", () => ShowPage(PageId.Scanner));
        AddBottomAction(rightActions, "Waypoints", "W", () => ShowPage(PageId.Waypoints));
        AddBottomAction(rightActions, "Ajustes", "S", () => ShowPage(PageId.Settings));
        AddBottomAction(rightActions, "Guía", "?", () => ShowPage(PageId.Guide));

        _bottomBar.Controls.Add(_status);
        _bottomBar.Controls.Add(rightActions);
        return _bottomBar;
    }

    private static void AddBottomAction(Control parent, string label, string keyBadge, Action onClick)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Cursor = Cursors.Hand, Margin = new Padding(12, 0, 0, 0) };
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(245, 236, 214), Font = new Font("Georgia", 10f, FontStyle.Bold), BackColor = Color.Transparent, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 5, 0) };
        var badge = new Label { Text = keyBadge, Width = 22, Height = 22, BackColor = Color.Transparent, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 0) };
        badge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Color.FromArgb(244, 236, 214));
            e.Graphics.FillEllipse(b, 0, 0, badge.Width - 1, badge.Height - 1);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(Color.FromArgb(44, 33, 18));
            using var f = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            e.Graphics.DrawString(keyBadge, f, tb, new RectangleF(0, 0, badge.Width, badge.Height), sf);
        };
        panel.Click += (_, _) => onClick(); lbl.Click += (_, _) => onClick(); badge.Click += (_, _) => onClick();
        panel.Controls.Add(lbl); panel.Controls.Add(badge); parent.Controls.Add(panel);
    }

    private void CreatePages()
    {
        var cheats = new CheatsPage(this);
        var scanner = new ScannerPage(this);
        var stats = new StatsPage(this);
        var collectibles = new CollectiblesPage(this);
        var waypoints = new WaypointsPage(this);
        var settings = new SettingsPage(this);
        var guide = new GuidePage(this);

        _pages[PageId.Cheats] = cheats; _pages[PageId.Scanner] = scanner; _pages[PageId.Stats] = stats;
        _pages[PageId.Collectibles] = collectibles; _pages[PageId.Waypoints] = waypoints; _pages[PageId.Settings] = settings; _pages[PageId.Guide] = guide;

        foreach (TrainerPage page in _pages.Values) { page.Visible = false; _pageHost.Controls.Add(page); }
        _cheatsPage = cheats; _scannerPage = scanner; _statsPage = stats; _collectiblesPage = collectibles; _waypointsPage = waypoints; _settingsPage = settings;
    }

    private CheatsPage _cheatsPage = null!;
    private ScannerPage _scannerPage = null!;
    private StatsPage _statsPage = null!;
    private CollectiblesPage _collectiblesPage = null!;
    private WaypointsPage _waypointsPage = null!;
    private SettingsPage _settingsPage = null!;

    public void ShowPage(PageId page)
    {
        _currentPage = page;
        foreach ((PageId id, TrainerPage control) in _pages) { control.Visible = id == page; if (id == page) control.BringToFront(); }
        foreach ((PageId id, Button button) in _navButtons)
        {
            bool selected = id == page;
            button.BackColor = selected ? Color.FromArgb(106, 78, 38) : Color.Transparent;
button.ForeColor = selected ? Color.FromArgb(255, 246, 224) : Color.FromArgb(236, 224, 198);
}
}    // ---------------------------------------------------------------- conexión

    private void OnLoaded()
    {
        RefreshProcessList();

        if (_profile.AutoAttach)
        {
            ProcessItem? item = FindProcess(_profile.ProcessName);
            if (item is not null)
            {
                _processCombo.SelectedItem = item;
                ConnectSelected();
            }
            else
            {
                SetStatus($"No encuentro «{_profile.ProcessName}». Abre el juego y pulsa Actualizar.");
            }
        }
        else
        {
            SetStatus("Listo. Elige el proceso y pulsa Conectar.");
        }

        _refreshTimer.Start();
        RegisterHotkeys();
    }

    private ProcessItem? SelectedProcess => _processCombo.SelectedItem as ProcessItem;

    private void RefreshProcessList()
    {
        ProcessItem? keep = SelectedProcess;
        string wanted = _profile.ProcessName;
        var items = new List<ProcessItem>();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId) continue;
                if (process.MainWindowHandle == IntPtr.Zero &&
                    !ProcessNames.Match(process.ProcessName, wanted))
                {
                    continue;
                }

                items.Add(new ProcessItem(process.ProcessName, process.Id));
            }
            catch
            {
                // proceso protegido o ya cerrado
            }
            finally
            {
                process.Dispose();
            }
        }

        items.Sort((a, b) =>
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.Id.CompareTo(b.Id);
        });

        _processCombo.BeginUpdate();
        _processCombo.Items.Clear();
        foreach (ProcessItem item in items) _processCombo.Items.Add(item);
        _processCombo.EndUpdate();

        ProcessItem? selection = items.FirstOrDefault(i => keep is not null && i.Id == keep.Id)
                                 ?? items.FirstOrDefault(i => ProcessNames.Match(i.Name, wanted))
                                 ?? items.FirstOrDefault();

        if (selection is not null) _processCombo.SelectedItem = selection;
        UpdateProcessStatus();
    }

    private ProcessItem? FindProcess(string name)
    {
        foreach (ProcessItem item in _processCombo.Items)
        {
            if (ProcessNames.Match(item.Name, name)) return item;
        }

        return null;
    }

    private void UpdateProcessStatus()
    {
        if (_memory is { IsAlive: true } memory)
        {
            _processStatus.Text = memory.HasWriteAccess
                ? "Conectado"
                : "Conectado en solo lectura";
            _processStatus.ForeColor = memory.HasWriteAccess ? Theme.Muted : Theme.Gold;
            return;
        }

        ProcessItem? item = SelectedProcess;
        _processStatus.Text = item is null ? "No hay procesos" : $"{item.Name} (PID {item.Id})";
        _processStatus.ForeColor = Theme.Muted;
    }

    private void ConnectSelected()
    {
        ProcessItem? item = SelectedProcess;
        if (item is null)
        {
            SetStatus("Elige un proceso de la lista o pulsa Actualizar.", true);
            return;
        }

        try
        {
            UseWaitCursor = true;
            GameMemory memory = GameMemory.Attach(item.Id);

            bool profileChanged = !ProcessNames.Match(_profile.ProcessName, item.Name);
            if (profileChanged)
            {
                _profile = _configFile.GetProfile(item.Name);
                _configFile.LastProcess = _profile.ProcessName;
            }

            _engine.Attach(memory, _profile);
            _memory = memory;
            _disconnectNotified = false;

            foreach (TrainerPage page in _pages.Values) page.OnProfileChanged();

            _freezeTimer.Interval = Math.Clamp(_profile.FreezeIntervalMs, 10, 2000);
            _freezeTimer.Start();
            _refreshTimer.Start();
            RegisterHotkeys();
            UpdateConnectionUi();

            SetStatus($"Conectado a {memory.ProcessName} (PID {memory.ProcessId}). " +
                      (memory.HasWriteAccess ? "Lectura y escritura activas." : "Solo lectura: ejecuta como administrador."));
            Log.Write($"Conectado a {memory.ProcessName} PID {memory.ProcessId}, x64={memory.Is64Bit}, escritura={memory.HasWriteAccess}");
            SaveConfig();
        }
        catch (Exception ex)
        {
            Log.Error("conectar", ex);
            _engine.Detach();
            _memory = null;
            UpdateConnectionUi();
            SetStatus("No se pudo conectar: " + ex.Message, true);
            MessageBox.Show(this,
                "No se pudo conectar con el juego:\n\n" + ex.Message +
                "\n\nConsejos:\n• Abre el juego antes de conectar.\n• Si el juego se ejecuta como administrador, " +
                "reinicia el trainer como administrador (Ajustes).",
                "Phykro Trainer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void UpdateConnectionUi()
    {
        GameMemory? memory = _memory;
        if (memory is { IsAlive: true })
        {
            _connState.Text = "● Conectado";
            _connState.ForeColor = Theme.Accent;
            _connDetail.Text = $"{memory.ProcessName} · PID {memory.ProcessId}";
            GameModule? main = memory.MainModule;
            _connBase.Text = main is null
                ? (memory.Is64Bit ? "x64" : "x86")
                : $"base 0x{main.Value.Base:X} · {(memory.Is64Bit ? "x64" : "x86")}";
            _attachButton.Text = "Reconectar";
        }
        else
        {
            _connState.Text = "○ Sin conexión";
            _connState.ForeColor = Theme.Muted;
            _connDetail.Text = "Abre el juego y conecta";
            _connBase.Text = "";
            _attachButton.Text = "Conectar";
        }

        UpdateProcessStatus();
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (_memory is not null && !_memory.IsAlive)
        {
            HandleDisconnect();
            _nextAutoReconnectUtc = DateTime.UtcNow.AddSeconds(1.5);
            return;
        }

        if (_memory is null)
        {
            if (DateTime.UtcNow >= _nextAutoReconnectUtc)
            {
                _nextAutoReconnectUtc = DateTime.UtcNow.AddSeconds(1.5);
                TryAutoReconnect();
            }
            return;
        }

        _cheatsPage.OnTick();
        _waypointsPage.OnTick();
        _statsPage.OnTick();
        _collectiblesPage.OnTick();
    }

    private void TryAutoReconnect()
    {
        string wanted = _profile.ProcessName;
        if (string.IsNullOrWhiteSpace(wanted)) return;

        Process? match = null;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != Environment.ProcessId && ProcessNames.Match(p.ProcessName, wanted))
                {
                    match = p;
                    break;
                }
            }
            catch { }
            finally
            {
                if (match != p) p.Dispose();
            }
        }

        if (match is null) return;

        try
        {
            var item = new ProcessItem(match.ProcessName, match.Id);
            RefreshProcessList();
            for (int i = 0; i < _processCombo.Items.Count; i++)
            {
                if (_processCombo.Items[i] is ProcessItem pi && pi.Id == item.Id)
                {
                    _processCombo.SelectedIndex = i;
                    break;
                }
            }
            ConnectSelected();
        }
        finally
        {
            match.Dispose();
        }
    }

    private void HandleDisconnect()
    {
        _freezeTimer.Stop();
        _engine.Detach();
        _memory = null;
        _hotkeys.Clear();
        UpdateConnectionUi();
        foreach (TrainerPage page in _pages.Values) page.OnProfileChanged();

        if (!_disconnectNotified)
        {
            _disconnectNotified = true;
            SetStatus("El juego se cerró o se perdió la conexión.", true);
        }
    }

    // ---------------------------------------------------------------- atajos

    public void RegisterHotkeys()
    {
        _hotkeys.Clear();
        _hotkeys.SetWindow(Handle);

        _hotkeys.Register(_profile.HotkeyToggleWindow, ToggleWindow);
        _hotkeys.Register(_profile.HotkeySaveWaypoint, () => _waypointsPage.SaveQuick());
        _hotkeys.Register(_profile.HotkeyTeleport, () => _waypointsPage.TeleportSelected());

        foreach (CheatTarget target in _profile.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Hotkey)) continue;
            _hotkeys.Register(target.Hotkey, () => _engine.ToggleFreeze(target));
        }

        if (_hotkeys.Errors.Count > 0)
        {
            SetStatus("Atajos: " + string.Join(" · ", _hotkeys.Errors), true);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = 1;
            Native.DwmSetWindowAttribute(Handle, Native.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
            // no es crítico
        }

        _hotkeys.SetWindow(Handle);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.WmHotkey)
        {
            _hotkeys.Handle(message.WParam.ToInt32());
            return;
        }

        base.WndProc(ref message);
    }

    private void ToggleWindow()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
            return;
        }

        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        TopMost = true;
        TopMost = false;
        BringToFront();
    }

    // ---------------------------------------------------------------- utilidades

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Theme.Danger : Theme.Muted;
        if (error) Log.Write("AVISO: " + text);
    }

    public void Notify(string text)
    {
        SetStatus(text);
        if (!ContainsFocus && Visible)
        {
            try { _tray.ShowBalloonTip(1600, "Phykro Trainer", text, ToolTipIcon.Info); }
            catch { /* ignorar */ }
        }
    }

    public void SaveConfig()
    {
        _configFile.LastProcess = _profile.ProcessName;
        if (!ConfigStore.Save(_configFile))
            SetStatus("No se pudo guardar la configuración en " + Paths.ConfigPath, true);
    }

    public void AddTargets(IEnumerable<CheatTarget> targets)
    {
        var list = targets.ToList();
        if (list.Count == 0) return;

        foreach (CheatTarget target in list) _profile.Targets.Add(target);
        _cheatsPage.OnProfileChanged();
        RegisterHotkeys();
        SaveConfig();
        ShowPage(PageId.Collectibles);
        SetStatus($"Añadidos {list.Count} truco(s). Revisa la dirección y asigna un atajo si quieres.");
    }

    public (bool ok, string text) TestAddress(string specText, ValueKind kind)
    {
        if (_memory is not { IsAlive: true } memory) return (false, "Sin conexión con el juego.");
        if (!AddressSpec.TryParse(specText, out AddressSpec spec, out string error)) return (false, error);
        if (!spec.TryResolve(memory, out long address, out error)) return (false, error);

        byte[] buffer = new byte[ValueCodec.Size(kind)];
        if (!memory.TryRead(address, buffer)) return (false, "La dirección existe pero no se puede leer.");
        return (true, $"0x{address:X} → {ValueCodec.Format(kind, buffer)}");
    }

    public void ApplyFreezeInterval()
    {
        _freezeTimer.Interval = Math.Clamp(_profile.FreezeIntervalMs, 10, 2000);
    }

    public void RestoreOriginals()
    {
        int restored = _engine.RestoreAll();
        SaveConfig();
        SetStatus(restored > 0
            ? $"Restaurados {restored} valores originales."
            : "No hay valores originales guardados en esta sesión.");
    }

    public void RestartAsAdmin()
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--elevated"
            };
            Process.Start(start);
            Close();
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo reiniciar como administrador: " + ex.Message, true);
        }
    }

    public void OpenConfigFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Paths.DataDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo abrir la carpeta: " + ex.Message, true);
        }
    }

    public string ConfigPath => Paths.ConfigPath;

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _freezeTimer.Stop();
        _refreshTimer.Stop();
        _hotkeys.Dispose();

        try
        {
            if (_profile.RestoreOnExit && _engine.Originals.Count > 0) _engine.RestoreAll();
        }
        catch (Exception ex)
        {
            Log.Error("restaurar al cerrar", ex);
        }

        _configFile.LastProcess = _profile.ProcessName;
        ConfigStore.Save(_configFile);

        _tray.Visible = false;
        _tray.Dispose();
        if (!ReferenceEquals(_appIcon, SystemIcons.Application)) _appIcon.Dispose();
        _engine.Dispose();
    }

    private sealed record ProcessItem(string Name, int Id)
    {
        public override string ToString() => $"{Name}  (PID {Id})";
    }
}




