using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

public sealed class SettingsPage : TrainerPage
{
    private readonly NumericUpDown _interval = Ui.Number(10, 2000, 50);
    private readonly CheckBox _autoAttach = Ui.Check("Conectar automáticamente al abrir el trainer");
    private readonly CheckBox _restoreOnExit = Ui.Check("Restaurar los valores originales al cerrar");
    private readonly TextBox _toggleHotkey = Ui.TextBox(width: 160);
    private readonly TextBox _saveHotkey = Ui.TextBox(width: 160);
    private readonly TextBox _teleportHotkey = Ui.TextBox(width: 160);
    private readonly TextBox _gamePath = Ui.TextBox(width: 420);
    private readonly Label _mapStatus = Ui.Hint("");
    private readonly Label _paths = Ui.Hint("");
    private bool _loading;

    public SettingsPage(MainForm host) : base(host)
    {
        BuildUi();
        OnProfileChanged();
    }

    private void BuildUi()
    {
        var root = Ui.Stack();
        root.AutoScroll = true;

        Ui.AddBlock(root, Ui.Section("AJUSTES"));

        var behaviour = Card();
        CardAdd(behaviour, Ui.Label("Cada cuántos milisegundos se escriben los valores congelados:", bold: true));

        var intervalRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        intervalRow.Controls.Add(_interval);
        intervalRow.Controls.Add(Ui.Hint("50 ms va bien. Bájalo si el juego sobrescribe el valor muy rápido."));
        CardAdd(behaviour, intervalRow);
        CardAdd(behaviour, _autoAttach);
        CardAdd(behaviour, _restoreOnExit);

        var hotkeys = Card();
        CardAdd(hotkeys, Ui.Label("Atajos globales (funcionan con el juego en primer plano):", bold: true));
        CardAdd(hotkeys, FieldRow("Mostrar / ocultar trainer", _toggleHotkey, "Ctrl+Alt+T"));
        CardAdd(hotkeys, FieldRow("Guardar waypoint", _saveHotkey, "F9"));
        CardAdd(hotkeys, FieldRow("Teletransportar", _teleportHotkey, "F10"));

        var applyHotkeys = Ui.Button("Aplicar atajos", primary: true);
        applyHotkeys.Click += (_, _) => ApplyHotkeys();
        var applyRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        applyRow.Controls.Add(applyHotkeys);
        CardAdd(hotkeys, applyRow);
        CardAdd(hotkeys, Ui.Hint("Formato: F1, Ctrl+Alt+2, Shift+F5… Los atajos de cada truco se configuran en la pestaña Trucos."));

        var maps = Card();
        CardAdd(maps, Ui.Label("Ruta del juego (carpeta que contiene «romfs»):", bold: true));
        var gamePathRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        _gamePath.TextChanged += (_, _) =>
        {
            if (_loading) return;
            Host.Profile.GamePath = _gamePath.Text.Trim();
        };
        _gamePath.Leave += (_, _) =>
        {
            if (!_loading) Host.SaveConfig();
        };
        gamePathRow.Controls.Add(_gamePath);
        var browse = Ui.Button("Examinar...");
        browse.Click += (_, _) => BrowseGamePath();
        gamePathRow.Controls.Add(browse);
        var extractMaps = Ui.Button("Extraer mapas ahora", primary: true);
        extractMaps.Click += async (_, _) => await ExtractMapsNowAsync(extractMaps);
        gamePathRow.Controls.Add(extractMaps);
        CardAdd(maps, gamePathRow);
        CardAdd(maps, _mapStatus);
        CardAdd(maps, Ui.Hint("La extracción lee romfs\\index.json + romfs\\romfs.bin y genera maps\\map-calibration.json " +
                              "y los PNG junto al trainer. El juego nunca se modifica."));

        var actions = Card();
        CardAdd(actions, Ui.Label("Acciones:", bold: true));

        var actionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent
        };
        var save = Ui.Button("Guardar configuración", primary: true);
        save.Click += (_, _) =>
        {
            Host.SaveConfig();
            Host.SetStatus("Configuración guardada.");
        };
        var restore = Ui.Button("Restaurar valores originales");
        restore.Click += (_, _) => Host.RestoreOriginals();
        var folder = Ui.Button("Abrir carpeta de datos");
        folder.Click += (_, _) => Host.OpenConfigFolder();
        var admin = Ui.Button("Reiniciar como administrador");
        admin.Click += (_, _) => Host.RestartAsAdmin();
        actionRow.Controls.Add(save);
        actionRow.Controls.Add(restore);
        actionRow.Controls.Add(folder);
        actionRow.Controls.Add(admin);
        CardAdd(actions, actionRow);
        CardAdd(actions, _paths);
        CardAdd(actions, Ui.Hint("Si el juego se ejecuta como administrador, el trainer también debe hacerlo. " +
                                  "El registro de errores está en trainer.log, en la misma carpeta."));

        var legal = Card();
        CardAdd(legal, Ui.Label("Uso responsable", bold: true));
        CardAdd(legal, Ui.Hint("Este trainer lee y escribe la memoria de un proceso. Úsalo solo en juegos de un " +
                               "jugador, con copias que poseas, y nunca en juegos online ni para saltarte " +
                               "protecciones. Al modificar la memoria puede que el juego se cuelgue: guarda " +
                               "partidas antes de probar. No se hace ninguna copia de la memoria del juego."));

        Ui.AddBlock(root, behaviour);
        Ui.AddBlock(root, hotkeys);
        Ui.AddBlock(root, maps);
        Ui.AddBlock(root, actions);
        Ui.AddBlock(root, legal);

        _interval.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            Host.Profile.FreezeIntervalMs = (int)_interval.Value;
            Host.ApplyFreezeInterval();
            Host.SaveConfig();
        };

        _autoAttach.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Host.Profile.AutoAttach = _autoAttach.Checked;
            Host.SaveConfig();
        };

        _restoreOnExit.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Host.Profile.RestoreOnExit = _restoreOnExit.Checked;
            Host.SaveConfig();
        };

        Controls.Add(root);
    }

    public override void OnProfileChanged()
    {
        _loading = true;
        TrainerProfile profile = Host.Profile;
        _interval.Value = Math.Clamp(profile.FreezeIntervalMs, 10, 2000);
        _autoAttach.Checked = profile.AutoAttach;
        _restoreOnExit.Checked = profile.RestoreOnExit;
        _toggleHotkey.Text = profile.HotkeyToggleWindow;
        _saveHotkey.Text = profile.HotkeySaveWaypoint;
        _teleportHotkey.Text = profile.HotkeyTeleport;
        _gamePath.Text = profile.GamePath;
        _paths.Text = "Configuración: " + Host.ConfigPath;
        _loading = false;

        string mapsDir = Path.Combine(AppContext.BaseDirectory, "maps");
        int pngs = Directory.Exists(mapsDir) ? Directory.GetFiles(mapsDir, "*.png").Length : 0;
        _mapStatus.Text = pngs > 0 ? $"Mapas extraídos: {pngs} PNG en {mapsDir}" : "Mapas sin extraer todavía.";
    }

    private void BrowseGamePath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Elige la carpeta del juego que contiene la subcarpeta romfs",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = _gamePath.Text.Trim()
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _gamePath.Text = dialog.SelectedPath;
        Host.Profile.GamePath = dialog.SelectedPath;
        Host.SaveConfig();
    }

    private async Task ExtractMapsNowAsync(Button button)
    {
        string gamePath = _gamePath.Text.Trim();
        if (gamePath.Length == 0 || !Directory.Exists(Path.Combine(gamePath, "romfs")))
        {
            Host.SetStatus("Ruta del juego inválida: no existe la carpeta romfs.", true);
            return;
        }

        Host.Profile.GamePath = gamePath;
        Host.SaveConfig();

        string mapsDir = Path.Combine(AppContext.BaseDirectory, "maps");
        button.Enabled = false;
        _mapStatus.Text = "Extrayendo mapas... esto puede tardar un poco.";
        Host.SetStatus("Extrayendo mapas del juego...");
        bool ok;
        try
        {
            ok = await Task.Run(() => GameMapExtractor.ForceExtract(gamePath, mapsDir));
        }
        catch (Exception ex)
        {
            Log.Error("mapas:botón extraer", ex);
            ok = false;
        }

        button.Enabled = true;
        int pngs = Directory.Exists(mapsDir) ? Directory.GetFiles(mapsDir, "*.png").Length : 0;
        _mapStatus.Text = ok
            ? $"Mapas extraídos: {pngs} PNG en {mapsDir}"
            : "No se pudieron extraer los mapas. Revisa trainer.log.";
        Host.SetStatus(_mapStatus.Text, !ok);
    }

    private void ApplyHotkeys()
    {
        string toggle = _toggleHotkey.Text.Trim();
        string save = _saveHotkey.Text.Trim();
        string teleport = _teleportHotkey.Text.Trim();

        foreach ((string text, string label) in new[]
                 {
                     (toggle, "Mostrar/ocultar"), (save, "Guardar waypoint"), (teleport, "Teletransportar")
                 })
        {
            if (text.Length == 0) continue;
            if (!HotkeyManager.TryParse(text, out _, out string error))
            {
                Host.SetStatus($"{label}: {error}", true);
                return;
            }
        }

        TrainerProfile profile = Host.Profile;
        profile.HotkeyToggleWindow = toggle;
        profile.HotkeySaveWaypoint = save;
        profile.HotkeyTeleport = teleport;

        Host.RegisterHotkeys();
        Host.SaveConfig();
        Host.SetStatus("Atajos aplicados. Si alguno falla, prueba otra combinación.");
    }

    private static TableLayoutPanel Card()
    {
        var card = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 0,
            BackColor = Theme.Card,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0),
            Dock = DockStyle.Top
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return card;
    }

    private static void CardAdd(TableLayoutPanel card, Control control)
    {
        int row = card.RowStyles.Count;
        card.RowCount = row + 1;
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 3, 0, 3);
        card.Controls.Add(control, 0, row);
    }

    private static Control FieldRow(string label, TextBox box, string placeholder)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        var name = Ui.Label(label, muted: true);
        name.Width = 190;
        name.AutoSize = false;
        name.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(name);
        row.Controls.Add(box);
        row.Controls.Add(Ui.Hint($"ej. {placeholder}"));
        return row;
    }
}
