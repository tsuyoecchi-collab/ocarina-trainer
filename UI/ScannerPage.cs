using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

public sealed class ScannerPage : TrainerPage
{
    private const int PageSize = 2500;

    private readonly ComboBox _kind = Ui.Combo(220);
    private readonly ComboBox _mode = Ui.Combo(185);
    private readonly ComboBox _area = Ui.Combo(278);
    private readonly TextBox _value = Ui.TextBox(width: 110);
    private readonly CheckBox _aligned = Ui.Check("Alinear", true);
    private readonly Button _scanButton = Ui.Button("Buscar", primary: true);
    private readonly Button _newButton = Ui.Button("Nueva búsqueda");
    private readonly Button _cancelButton = Ui.Button("Cancelar");
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Top,
        Height = 8,
        Style = ProgressBarStyle.Continuous,
        Maximum = 1000,
        BackColor = Theme.Card,
        ForeColor = Theme.Accent
    };

    private readonly Label _info = Ui.Hint("Escribe un valor y pulsa Buscar.");
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _pageLabel = Ui.Hint("");
    private readonly Button _prevButton = Ui.Button("◀ Anterior");
    private readonly Button _nextButton = Ui.Button("Siguiente ▶");
    private readonly Button _refreshButton = Ui.Button("Actualizar valores");
    private readonly Button _addButton = Ui.Button("Añadir a trucos", primary: true);

    private ScanSession? _session;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private int _page;

    public ScannerPage(MainForm host) : base(host)
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
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        header.Controls.Add(Ui.Section("ESCÁNER DE MEMORIA"));

        var hint = Ui.Hint("1) Busca el valor que ves en pantalla.  2) Vuelve al juego y cámbialo.  " +
                           "3) Pulsa «Siguiente» con la comparación adecuada (p. ej. Disminuyó si perdiste vida).  " +
                           "Repite hasta quedarte con 1–5 direcciones y añádelas como trucos.  " +
                           "Si no aparece nada en «Memoria pequeña», prueba «Módulo del juego» o el resto.");

        var controls = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Card,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 4, 0, 6),
            Dock = DockStyle.Top
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var row1 = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        row1.Controls.Add(Ui.Label("Tipo:", muted: true));
        row1.Controls.Add(_kind);
        row1.Controls.Add(Ui.Label("Comparación:", muted: true));
        row1.Controls.Add(_mode);
        row1.Controls.Add(Ui.Label("Valor:", muted: true));
        row1.Controls.Add(_value);

        var row2 = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 0)
        };
        row2.Controls.Add(Ui.Label("Área:", muted: true));
        row2.Controls.Add(_area);
        row2.Controls.Add(_aligned);
        row2.Controls.Add(_scanButton);
        row2.Controls.Add(_newButton);
        row2.Controls.Add(_cancelButton);

        controls.Controls.Add(row1, 0, 0);
        controls.Controls.Add(row2, 0, 1);

        _kind.Items.Clear();
        foreach (ValueKind kind in Enum.GetValues<ValueKind>())
            _kind.Items.Add(new KindItem(kind, ValueCodec.LongLabel(kind)));
        _kind.SelectedIndex = 2;

        _area.Items.Clear();
        _area.Items.Add(new AreaItem(ScanArea.Fast, Scanner.AreaLabel(ScanArea.Fast)));
        _area.Items.Add(new AreaItem(ScanArea.MainModule, Scanner.AreaLabel(ScanArea.MainModule)));
        _area.Items.Add(new AreaItem(ScanArea.Writable, Scanner.AreaLabel(ScanArea.Writable)));
        _area.Items.Add(new AreaItem(ScanArea.All, Scanner.AreaLabel(ScanArea.All)));
        _area.SelectedIndex = 0;

        _scanButton.Click += async (_, _) => await RunScanAsync();
        _newButton.Click += (_, _) => ResetSession();
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _value.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await RunScanAsync();
            }
        };

        _progress.Visible = false;
        var progressHost = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
        _info.Dock = DockStyle.Fill;
        _progress.Dock = DockStyle.Bottom;
        _progress.Height = 6;
        progressHost.Controls.Add(_info);
        progressHost.Controls.Add(_progress);

        BuildGrid();

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 6),
            Dock = DockStyle.Bottom
        };
        _prevButton.Click += (_, _) => { if (_page > 0) { _page--; RenderPage(); } };
        _nextButton.Click += (_, _) => { _page++; RenderPage(); };
        _refreshButton.Click += async (_, _) => await RefreshPageAsync();
        _addButton.Click += (_, _) => AddSelectedToCheats();
        _pageLabel.Margin = new Padding(12, 8, 12, 0);
        footer.Controls.Add(_prevButton);
        footer.Controls.Add(_nextButton);
        footer.Controls.Add(_pageLabel);
        footer.Controls.Add(_refreshButton);
        footer.Controls.Add(_addButton);

        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Card
        };
        gridHost.Controls.Add(_grid);
        gridHost.Controls.Add(footer);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(controls, 0, 2);
        root.Controls.Add(progressHost, 0, 3);
        root.Controls.Add(gridHost, 0, 4);

        Controls.Add(root);
        SetBusy(false);
        UpdateModeChoices();
    }

    private void BuildGrid()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "#",
            Width = 70,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Dirección",
            Width = 180,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Font = Theme.Mono, ForeColor = Theme.Text }
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Valor",
            Width = 150,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Anterior",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _grid.MultiSelect = true;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) AddSelectedToCheats();
        };
    }

    public override void OnProfileChanged()
    {
        _session = null;
        _page = 0;
        _grid.Rows.Clear();
        UpdateModeChoices();
        UpdatePageUi();
        SetBusy(false);
        _info.Text = "Escribe un valor y pulsa Buscar.";
        _progress.Visible = false;
    }

    private ValueKind SelectedKind => _kind.SelectedItem is KindItem item ? item.Kind : ValueKind.Int32;

    private ScanMode SelectedMode => _mode.SelectedItem is ModeItem item ? item.Mode : ScanMode.Exact;

    private ScanArea SelectedArea => _area.SelectedItem is AreaItem item ? item.Area : ScanArea.Fast;

    private void UpdateModeChoices()
    {
        ScanMode? keep = _mode.SelectedItem is ModeItem current ? current.Mode : null;

        _mode.BeginUpdate();
        _mode.Items.Clear();
        _mode.Items.Add(new ModeItem(ScanMode.Exact, Scanner.ModeLabel(ScanMode.Exact)));
        _mode.Items.Add(new ModeItem(ScanMode.NotEqual, Scanner.ModeLabel(ScanMode.NotEqual)));
        _mode.Items.Add(new ModeItem(ScanMode.Greater, Scanner.ModeLabel(ScanMode.Greater)));
        _mode.Items.Add(new ModeItem(ScanMode.Less, Scanner.ModeLabel(ScanMode.Less)));

        if (_session is not null)
        {
            _mode.Items.Add(new ModeItem(ScanMode.Increased, Scanner.ModeLabel(ScanMode.Increased)));
            _mode.Items.Add(new ModeItem(ScanMode.Decreased, Scanner.ModeLabel(ScanMode.Decreased)));
            _mode.Items.Add(new ModeItem(ScanMode.Changed, Scanner.ModeLabel(ScanMode.Changed)));
            _mode.Items.Add(new ModeItem(ScanMode.Unchanged, Scanner.ModeLabel(ScanMode.Unchanged)));
        }

        _mode.EndUpdate();

        int index = 0;
        if (keep is not null)
        {
            for (int i = 0; i < _mode.Items.Count; i++)
            {
                if (_mode.Items[i] is ModeItem item && item.Mode == keep)
                {
                    index = i;
                    break;
                }
            }
        }

        _mode.SelectedIndex = index;
    }

    private bool TryGetTarget(ScanMode mode, out byte[]? target)
    {
        target = null;
        if (!Scanner.NeedsValue(mode)) return true;

        if (!ValueCodec.TryParse(SelectedKind, _value.Text, out byte[] bytes, out string error))
        {
            Host.SetStatus(error, true);
            _value.Focus();
            return false;
        }

        target = bytes;
        return true;
    }

    private async Task RunScanAsync()
    {
        if (_busy) return;

        GameMemory? memory = Host.Memory;
        if (memory is null || !memory.IsAlive)
        {
            Host.SetStatus("Conecta con el juego antes de escanear.", true);
            return;
        }

        ScanMode mode = SelectedMode;
        if (!TryGetTarget(mode, out byte[]? target)) return;

        ValueKind kind = SelectedKind;
        bool first = _session is null || _session.Kind != kind;

        if (first)
        {
            long estimate = Scanner.EstimateBytes(memory, SelectedArea);
            if (estimate > 2L * 1024 * 1024 * 1024)
            {
                double gigabytes = estimate / (1024.0 * 1024.0 * 1024.0);
                if (MessageBox.Show(FindForm(),
                        $"El área elegida tiene {gigabytes:N1} GB. La búsqueda puede tardar entre 10 y 90 segundos " +
                        "y usar toda la CPU.\n\n¿Continuar?",
                        "Phykro Trainer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }
        }

        if (!first && mode is ScanMode.Increased or ScanMode.Decreased or ScanMode.Changed or ScanMode.Unchanged &&
            _session!.Count > 400_000)
        {
            if (MessageBox.Show(FindForm(),
                    $"Hay {_session.Count:N0} resultados. El siguiente escaneo puede tardar unos segundos. ¿Continuar?",
                    "Phykro Trainer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        var progress = new Progress<ScanProgress>(UpdateProgress);
        int previousCount = _session?.Count ?? 0;

        try
        {
            ScanSession result = await Task.Run(() =>
            {
                if (first)
                {
                    return Scanner.FirstScan(memory, kind, mode, target, _aligned.Checked, SelectedArea,
                        progress, token);
                }

                return Scanner.NextScan(memory, _session!, mode, target, progress, token);
            }, token);

            _session = result;
            _page = 0;
            UpdateModeChoices();
            RenderPage();

            _progress.Value = 1000;
            _info.Text = result.Count == 0
                ? "Listo · sin resultados."
                : $"Listo · {result.Count:N0} resultado(s).";

            if (result.Truncated)
            {
                Host.SetStatus($"Demasiados resultados (límite {Scanner.MaxResults:N0}). " +
                               "Reduce el área o usa un valor más concreto.", true);
            }
            else if (result.Count == 0)
            {
                Host.SetStatus("Sin resultados. Prueba otra comparación, otro tipo (Int32/Float) u otra área.", true);
            }
            else
            {
                string prefix = first ? "Primera búsqueda" : "Filtro";
                Host.SetStatus($"{prefix}: {result.Count:N0} dirección(es) de {previousCount:N0} candidatas.");
            }
        }
        catch (OperationCanceledException)
        {
            _info.Text = "Búsqueda cancelada.";
            _progress.Value = 0;
            Host.SetStatus("Búsqueda cancelada.");
        }
        catch (Exception ex)
        {
            Log.Error("escaneo", ex);
            _info.Text = "Error al escanear.";
            _progress.Value = 0;
            Host.SetStatus("Error al escanear: " + ex.Message, true);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void UpdateProgress(ScanProgress progress)
    {
        if (progress.Filtering)
        {
            int percent = progress.BytesTotal > 0
                ? (int)Math.Clamp(progress.BytesScanned * 100 / progress.BytesTotal, 0, 100)
                : 0;
            _info.Text = $"Filtrando… {percent}% · {progress.BytesScanned:N0} / {progress.BytesTotal:N0} · " +
                         $"{progress.Found:N0} resultado(s)";
            _progress.Value = percent * 10;
            return;
        }

        if (progress.BytesTotal > 0 && progress.BytesScanned > 0)
        {
            _progress.Value = (int)Math.Clamp(progress.BytesScanned * 1000 / progress.BytesTotal, 0, 1000);
            _info.Text = $"Escaneando… {progress.BytesScanned * 100 / Math.Max(1, progress.BytesTotal)}% · " +
                         $"{progress.BytesScanned / (1024.0 * 1024.0):N0} MB · {progress.Found:N0} resultado(s)";
        }
        else
        {
            _info.Text = "Preparando búsqueda…";
        }
    }

    private void RenderPage()
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();

        if (_session is null || _session.Count == 0)
        {
            _grid.ResumeLayout();
            UpdatePageUi();
            return;
        }

        int start = _page * PageSize;
        if (start >= _session.Count)
        {
            _page = Math.Max(0, (_session.Count - 1) / PageSize);
            start = _page * PageSize;
        }

        int end = Math.Min(_session.Count, start + PageSize);
        var rows = new List<DataGridViewRow>(end - start);
        for (int i = start; i < end; i++)
        {
            var row = new DataGridViewRow();
            row.CreateCells(_grid,
                (i + 1).ToString("N0"),
                $"0x{_session.Addresses[i]:X}",
                _session.ValueText(i),
                _session.PreviousText(i),
                "");
            rows.Add(row);
        }

        _grid.Rows.AddRange(rows.ToArray());
        _grid.ResumeLayout();
        UpdatePageUi();
    }

    private void UpdatePageUi()
    {
        if (_session is null || _session.Count == 0)
        {
            _pageLabel.Text = "0 resultados";
            _prevButton.Enabled = false;
            _nextButton.Enabled = false;
            _refreshButton.Enabled = false;
            _addButton.Enabled = false;
            return;
        }

        int pages = (_session.Count + PageSize - 1) / PageSize;
        _pageLabel.Text = $"Página {_page + 1:N0} / {pages:N0} · {_session.Count:N0} resultados";
        _prevButton.Enabled = _page > 0;
        _nextButton.Enabled = _page < pages - 1;
        _refreshButton.Enabled = true;
        _addButton.Enabled = true;
    }

    private async Task RefreshPageAsync()
    {
        if (_session is null || _busy) return;

        GameMemory? memory = Host.Memory;
        if (memory is null || !memory.IsAlive)
        {
            Host.SetStatus("Sin conexión con el juego.", true);
            return;
        }

        int start = _page * PageSize;
        int end = Math.Min(_session.Count, start + PageSize);
        int size = _session.ValueSize;
        var addresses = new List<long>(end - start);
        for (int i = start; i < end; i++) addresses.Add(_session.Addresses[i]);

        byte[] values = new byte[addresses.Count * size];
        bool[] valid = new bool[addresses.Count];

        try
        {
            _refreshButton.Enabled = false;
            await Task.Run(() => memory.ReadMany(addresses, size, values, valid));

            for (int i = 0; i < addresses.Count; i++)
            {
                if (!valid[i]) continue;
                Buffer.BlockCopy(values, i * size, _session.Values, (start + i) * size, size);
            }

            RenderPage();
            Host.SetStatus("Valores actualizados.");
        }
        catch (Exception ex)
        {
            Log.Error("actualizar valores del escáner", ex);
            Host.SetStatus("No se pudieron actualizar los valores: " + ex.Message, true);
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private void AddSelectedToCheats()
    {
        if (_session is null || _session.Count == 0) return;

        GameMemory? memory = Host.Memory;
        if (memory is null)
        {
            Host.SetStatus("Conecta con el juego primero.", true);
            return;
        }

        var indices = new List<int>();
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            int index = _page * PageSize + row.Index;
            if (index >= 0 && index < _session.Count && !indices.Contains(index)) indices.Add(index);
        }

        if (indices.Count == 0 && _grid.CurrentRow is { } current)
        {
            int index = _page * PageSize + current.Index;
            if (index >= 0 && index < _session.Count) indices.Add(index);
        }

        if (indices.Count == 0)
        {
            Host.SetStatus("Selecciona una o varias filas del resultado.", true);
            return;
        }

        if (indices.Count > 50 &&
            MessageBox.Show(FindForm(), $"Vas a añadir {indices.Count} trucos. ¿Seguro?",
                "Phykro Trainer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        string? name = Prompt.Show(FindForm()!, "Añadir a trucos",
            "Nombre para el truco (se numeran si añades varios):");
        if (string.IsNullOrWhiteSpace(name)) return;

        string cleanName = name.Trim();
        (bool ok, string text) = Host.TestAddress(AddressSpec.Describe(_session.Addresses[indices[0]], memory.MainModule),
            _session.Kind);
        if (!ok) Host.SetStatus("Aviso: " + text, true);

        var targets = new List<CheatTarget>();
        for (int n = 0; n < indices.Count; n++)
        {
            int index = indices[n];
            string spec = AddressSpec.Describe(_session.Addresses[index], memory.MainModule);
            targets.Add(new CheatTarget
            {
                Name = indices.Count == 1 ? cleanName : $"{cleanName} {n + 1}",
                Kind = _session.Kind,
                Spec = spec,
                Value = _session.ValueText(index),
                Enabled = true,
                Frozen = true,
                Note = $"Encontrado con el escáner el {DateTime.Now:dd/MM/yyyy HH:mm} " +
                       $"(valor {_session.ValueText(index)})."
            });
        }

        Host.AddTargets(targets);
    }

    private void ResetSession()
    {
        _session = null;
        _page = 0;
        _grid.Rows.Clear();
        UpdateModeChoices();
        UpdatePageUi();
        _progress.Value = 0;
        _progress.Visible = false;
        _info.Text = "Escribe un valor y pulsa Buscar.";
        Host.SetStatus("Búsqueda reiniciada. Se empieza de cero.");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _scanButton.Enabled = !busy;
        _newButton.Enabled = !busy;
        _kind.Enabled = !busy;
        _mode.Enabled = !busy;
        _area.Enabled = !busy;
        _value.Enabled = !busy;
        _aligned.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _progress.Visible = busy;
        if (busy)
        {
            _progress.Value = 0;
            _info.Text = "Preparando búsqueda…";
            _grid.Enabled = false;
        }
        else
        {
            _grid.Enabled = true;
        }
    }

    private sealed record KindItem(ValueKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ModeItem(ScanMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record AreaItem(ScanArea Area, string Label)
    {
        public override string ToString() => Label;
    }
}
