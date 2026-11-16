using System.ComponentModel;
using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

public sealed class CheatsPage : TrainerPage
{
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _count = Ui.Hint("");
    private BindingList<CheatTarget> _items = new();

    private readonly Button _editButton = Ui.Button("Editar");
    private readonly Button _duplicateButton = Ui.Button("Duplicar");
    private readonly Button _removeButton = Ui.Button("Eliminar");
    private readonly Button _restoreButton = Ui.Button("Restaurar originales");

    public CheatsPage(MainForm host) : base(host)
    {
        BuildUi();
        OnProfileChanged();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        header.Controls.Add(Ui.Section("TRUCOS"));
        _count.Margin = new Padding(16, 10, 0, 0);
        header.Controls.Add(_count);

        var hint = Ui.Hint("Marca «Activo» y «Congelar» para que el valor se escriba solo. " +
                           "Doble clic en una fila para cambiar nombre, valor, máximo o atajo. " +
                           "«Máximo» recorta lo que se escribe para no romper el juego; si pones " +
                           "una «Dirección del máximo», el trainer la lee del juego y la actualiza sola. " +
                           "Si no sabes la dirección, usa el Escáner.");

        var toolbar = Ui.Toolbar();
        var addButton = Ui.Button("Añadir truco", primary: true);
        addButton.Click += (_, _) => AddCheat();

        _editButton.Click += (_, _) => EditCheat();
        _duplicateButton.Click += (_, _) => DuplicateCheat();
        _removeButton.Click += (_, _) => RemoveCheat();
        _restoreButton.Click += (_, _) => Host.RestoreOriginals();

        var applyButton = Ui.Button("Aplicar ahora");
        applyButton.Click += (_, _) => ApplySelected();

        var readButton = Ui.Button("Leer");
        readButton.Click += (_, _) => ReadSelected();

        var templates = Ui.Button("Plantillas ▾");
        templates.Click += (_, _) => ShowTemplates(templates);

        var scanner = Ui.Button("Buscar dirección");
        scanner.Click += (_, _) => Host.ShowPage(PageId.Scanner);

        toolbar.Controls.AddRange(new Control[]
        {
            addButton, _editButton, _duplicateButton, _removeButton, applyButton, readButton,
            templates, _restoreButton, scanner
        });

        BuildGrid();

        var footer = new Label
        {
            Text = "Los valores originales se restauran al salir (Ajustes) o con «Restaurar originales».",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };

        root.Controls.Add(header, 0, 0);
        var top = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top
        };
        for (int i = 0; i < 3; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(hint, 0, 0);
        top.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(top, 0, 1);

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0) };
        gridHost.Controls.Add(_grid);
        root.Controls.Add(gridHost, 0, 2);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);
    }

    private void BuildGrid()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(Ui.CheckColumn("On", nameof(CheatTarget.Enabled), 42));
        _grid.Columns.Add(Ui.CheckColumn("Congelar", nameof(CheatTarget.Frozen), 72));
        _grid.Columns.Add(Ui.TextColumn("Nombre", nameof(CheatTarget.Name), 135));
        _grid.Columns.Add(Ui.TextColumn("Tipo", nameof(CheatTarget.KindLabel), 58));
        _grid.Columns.Add(Ui.TextColumn("Dirección", nameof(CheatTarget.Spec), 235, format: "mono"));
        _grid.Columns.Add(Ui.TextColumn("Valor", nameof(CheatTarget.Value), 70));
        _grid.Columns.Add(Ui.TextColumn("Máximo", nameof(CheatTarget.Max), 66));
        _grid.Columns.Add(Ui.TextColumn("Atajo", nameof(CheatTarget.Hotkey), 75));
        _grid.Columns.Add(Ui.TextColumn("Actual", nameof(CheatTarget.CurrentDisplay), 75));
        _grid.Columns.Add(Ui.FillColumn("Estado", nameof(CheatTarget.Status)));

        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) return;
            EditCheat();
        };

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string property = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (property is nameof(CheatTarget.Enabled) or nameof(CheatTarget.Frozen) or
                nameof(CheatTarget.Value) or nameof(CheatTarget.Max))
                Host.SaveConfig();
        };

        // La columna «Máximo» no se edita a mano cuando el truco tiene dirección de máximo:
        // el trainer la mantiene con la capacidad real del juego.
        _grid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string property = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (property != nameof(CheatTarget.Max)) return;
            if (_grid.Rows[e.RowIndex].DataBoundItem is CheatTarget { MaxSpec.Length: > 0 } linked)
            {
                _grid.CancelEdit();
                Host.SetStatus("«Máximo» lo da el juego. Borra la «Dirección del máximo» (Editar) para ponerlo a mano.", true);
            }
        };

        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    public override void OnProfileChanged()
    {
        _items = new BindingList<CheatTarget>(Host.Profile.Targets);
        _grid.DataSource = _items;
        UpdateCount();
        UpdateButtons();
    }

    public override void OnTick()
    {
        if (!Host.Engine.IsConnected) return;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is CheatTarget { Enabled: true } target)
                Host.Engine.RefreshCurrent(target);
        }
    }

    private CheatTarget? Selected => _grid.CurrentRow?.DataBoundItem as CheatTarget;

    private void UpdateButtons()
    {
        bool has = Selected is not null;
        _editButton.Enabled = has;
        _duplicateButton.Enabled = has;
        _removeButton.Enabled = has;
    }

    private void UpdateCount() => _count.Text = $"{_items.Count} truco(s)";

    private void AddCheat(CheatTarget? preset = null, string? presetName = null,
        ValueKind? presetKind = null, string? presetValue = null)
    {
        using var dialog = preset is null
            ? new CheatDialog(this.Host, null, "Nuevo truco", presetName, presetKind, presetValue)
            : new CheatDialog(this.Host, preset, "Editar truco");

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        CheatTarget result = dialog.Result;
        if (preset is null) Host.Profile.Targets.Add(result);
        _items.ResetBindings();
        UpdateCount();
        Host.RegisterHotkeys();
        Host.SaveConfig();

        if (Host.Engine.IsConnected && result.Enabled) Host.Engine.RefreshCurrent(result);
        Host.SetStatus($"Truco «{result.Name}» guardado.");
    }

    private void EditCheat()
    {
        CheatTarget? target = Selected;
        if (target is null) return;

        using var dialog = new CheatDialog(Host, target, "Editar truco");
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        CheatTarget result = dialog.Result;
        target.Name = result.Name;
        target.Kind = result.Kind;
        target.Spec = result.Spec;
        target.Value = result.Value;
        target.Max = result.Max;
        target.MaxSpec = result.MaxSpec;
        target.LimitToMax = result.LimitToMax;
        target.Hotkey = result.Hotkey;
        target.Note = result.Note;
        target.Enabled = result.Enabled;
        target.Frozen = result.Frozen;

        _items.ResetBindings();
        Host.RegisterHotkeys();
        Host.SaveConfig();
        Host.SetStatus($"Truco «{target.Name}» actualizado.");

        if (Host.Engine.IsConnected && target.Enabled)
            Host.Engine.RefreshCurrent(target);
    }

    private void DuplicateCheat()
    {
        CheatTarget? target = Selected;
        if (target is null) return;
        CheatTarget copy = target.Clone(target.Name + " (copia)");
        Host.Profile.Targets.Add(copy);
        _items.ResetBindings();
        UpdateCount();
        Host.SaveConfig();
        Host.SetStatus("Truco duplicado. Asígnale otra dirección o atajo.");
    }

    private void RemoveCheat()
    {
        CheatTarget? target = Selected;
        if (target is null) return;

        if (MessageBox.Show(FindForm(), $"¿Eliminar el truco «{target.Name}»?",
                "Phykro Trainer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Host.Profile.Targets.Remove(target);
        _items.ResetBindings();
        UpdateCount();
        Host.RegisterHotkeys();
        Host.SaveConfig();
        Host.SetStatus("Truco eliminado.");
    }

    private void ApplySelected()
    {
        CheatTarget? target = Selected;
        if (target is null) return;

        if (!Host.Engine.IsConnected)
        {
            Host.SetStatus("Conecta con el juego antes de aplicar.", true);
            return;
        }

        bool ok = Host.Engine.Apply(target);
        Host.SetStatus(ok
            ? $"{target.Name}: escrito {target.CurrentDisplay}."
            : $"{target.Name}: {target.Status}", !ok);
    }

    private void ReadSelected()
    {
        CheatTarget? target = Selected;
        if (target is null) return;
        bool ok = Host.Engine.RefreshCurrent(target);
        Host.SetStatus(ok
            ? $"«{target.Name}» ahora vale {target.CurrentDisplay}."
            : $"«{target.Name}»: {target.Status}", !ok);
    }

    private void ShowTemplates(Control anchor)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.CardAlt,
            ForeColor = Theme.Text,
            Font = Theme.Ui
        };

        void Item(string name, ValueKind kind, string value, string note)
        {
            var entry = new ToolStripMenuItem($"{name}  ({ValueCodec.ShortLabel(kind)}, {value})")
            {
                ToolTipText = note
            };
            entry.Click += (_, _) => AddCheat(presetName: name, presetKind: kind, presetValue: value);
            menu.Items.Add(entry);
        }

        menu.Items.Add(new ToolStripMenuItem("Elige una plantilla y pega la dirección encontrada") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        Item("Vida infinita", ValueKind.Int32, "20", "Se congela la salud en 20.");
        Item("Rupias", ValueKind.Int32, "999", "Dinero del juego.");
        Item("Munición / flechas", ValueKind.Int32, "30", "Balas, flechas u objetos.");
        Item("Velocidad de movimiento", ValueKind.Float, "1.5", "Suele ser float.");
        Item("Altura de salto", ValueKind.Float, "1.5", "Suele ser float.");
        Item("Daño / ataque", ValueKind.Int32, "99", "Fuerza del ataque.");

        IGameModule game = Host.GameModule;
        if (game.PresetGroups.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem($"{game.DisplayName} · direcciones probadas") { Enabled = false });

            foreach (CheatPresetGroup group in game.PresetGroups)
            {
                var entry = new ToolStripMenuItem(group.Name) { ToolTipText = group.Description };
                entry.Click += (_, _) => AddPresetGroup(group);
                menu.Items.Add(entry);
            }
        }

        menu.Show(anchor, new Point(0, anchor.Height));
    }

    /// <summary>
    /// Añade los trucos de una plantilla del módulo de juego (sin pasar por el diálogo) y
    /// deja congelados los que lo piden, en el valor que tengan en ese momento.
    /// </summary>
    private void AddPresetGroup(CheatPresetGroup group)
    {
        var created = new List<CheatTarget>();
        foreach (CheatPreset preset in group.Cheats)
        {
            if (Host.Profile.Targets.Any(t => string.Equals(t.Spec, preset.Spec, StringComparison.OrdinalIgnoreCase)))
                continue;
            created.Add(CreateFromPreset(preset));
        }

        if (created.Count == 0)
        {
            Host.SetStatus($"Los trucos de «{group.Name}» ya están añadidos.", true);
            return;
        }

        foreach (CheatTarget target in created) Host.Profile.Targets.Add(target);
        _items.ResetBindings();
        UpdateCount();
        Host.RegisterHotkeys();
        Host.SaveConfig();
        if (Host.Engine.IsConnected)
        {
            foreach (CheatTarget target in created) Host.Engine.RefreshCurrent(target);
        }

        bool anyFrozen = group.Cheats.Any(c => c.FreezeByDefault);
        if (created.Count == 1 && created[0].Hotkey.Length > 0)
        {
            Host.SetStatus($"Truco «{created[0].Name}» añadido. Atajo {created[0].Hotkey}: " +
                           $"congela/descongela en {created[0].Value}.");
        }
        else
        {
            Host.SetStatus($"Añadidos {created.Count} truco(s) de «{group.Name}»" +
                           (anyFrozen ? ", congelados en su valor actual." : "."));
        }
    }

    /// <summary>Crea un truco a partir de una plantilla del módulo, leyendo el valor actual si procede.</summary>
    private CheatTarget CreateFromPreset(CheatPreset preset)
    {
        string value = preset.Value;
        if (preset.CaptureCurrentValue && Host.Engine.IsConnected)
        {
            var probe = new CheatTarget { Name = preset.Name, Kind = preset.Kind, Spec = preset.Spec };
            if (Host.Engine.TryResolve(probe, out long address, out _))
            {
                byte[] buffer = new byte[ValueCodec.Size(preset.Kind)];
                if (Host.Engine.Memory!.TryRead(address, buffer))
                    value = ValueCodec.Format(preset.Kind, buffer);
            }
        }

        return new CheatTarget
        {
            Name = preset.Name,
            Kind = preset.Kind,
            Spec = preset.Spec,
            Value = value,
            Max = preset.Max,
            MaxSpec = preset.MaxSpec,
            Hotkey = preset.Hotkey,
            Note = preset.Note,
            Enabled = true,
            Frozen = preset.FreezeByDefault
        };
    }
}
