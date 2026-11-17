using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

/// <summary>Entrada de coleccionables.json (editable a mano por el usuario).</summary>
public sealed class CollectibleDto
{
    [JsonPropertyName("Id")] public string Id { get; set; } = "";
    [JsonPropertyName("Zona")] public string Zona { get; set; } = "";
    [JsonPropertyName("Mapa")] public string Mapa { get; set; } = "";
    [JsonPropertyName("Tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("Nombre")] public string Nombre { get; set; } = "";
    [JsonPropertyName("UbicacionExacta")] public string UbicacionExacta { get; set; } = "";
    [JsonPropertyName("Epoca")] public string Epoca { get; set; } = "";
    [JsonPropertyName("WorldX")] public double WorldX { get; set; }
    [JsonPropertyName("WorldY")] public double WorldY { get; set; }
    [JsonPropertyName("WorldZ")] public double WorldZ { get; set; }
    [JsonPropertyName("Hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("DireccionBit")] public string DireccionBit { get; set; } = "";
    [JsonPropertyName("Bit")] public int Bit { get; set; }

    /// <summary>ID tÃ©cnico del mapa (respaldo a Â«ZonaÂ» si Â«MapaÂ» estÃ¡ vacÃ­o).</summary>
    [JsonIgnore]
    public string MapId => Mapa.Length > 0 ? Mapa : Zona;
}

/// <summary>
/// PÃ¡gina Â«ColeccionablesÂ» en dos sub-pestaÃ±as: Â«Mapa en vivoÂ» (lista del mapa actual +
/// visor con Link y pines) y Â«Catálogo y ProgresoÂ» (tabla completa con filtros y
/// contadores). Solo lectura del juego; el botÃ³n de guardar escribe coordenadas.
/// </summary>
public sealed class CollectiblesPage : TrainerPage
{
    private sealed class Row : INotifyPropertyChanged
    {
        private string _estado = "â€”";

        public Row(CollectibleDto dto) => Dto = dto;

        public CollectibleDto Dto { get; }

        public string Estado
        {
            get => _estado;
            set
            {
                if (_estado == value) return;
                _estado = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Estado)));
            }
        }

        public string Zona => Dto.Zona;
        public string Mapa => Dto.MapId;
        public string Tipo => Dto.Tipo;
        public string Epoca => string.IsNullOrEmpty(Dto.Epoca) ? "â€”" : Dto.Epoca;
        public string Nombre => Dto.Nombre;
        public string UbicacionExacta => Dto.UbicacionExacta;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private const string LinkPosSpecX = "0x7004E371D0,0x480,0x31C";
    private const string LinkPosSpecY = "0x7004E371D0,0x480,0x320";
    private const string LinkPosSpecZ = "0x7004E371D0,0x480,0x324";
    private string? _lastAutoFloor;
    private string? _lastAutoState;
    private const uint AgeHash = 0xcea220d3; // true = NiÃ±o, false = Adulto

    private readonly TabControl _tabs = new();
    private readonly DataGridView _liveGrid = Ui.Grid();
    private readonly DataGridView _grid = Ui.Grid();
    private readonly ComboBox _zoneFilter = Ui.Combo(160);
    private readonly ComboBox _typeFilter = Ui.Combo(150);
    private readonly ComboBox _floor = Ui.Combo(170);
    private readonly CheckBox _onlyMissing = Ui.Check("Solo faltantes", false);
    private readonly Label _count = Ui.Hint("");
    private readonly Label _liveCount = Ui.Hint("");
    private readonly Label _hudZoneTitle = new() { AutoSize = true, ForeColor = Color.FromArgb(236, 224, 198), Font = new Font("Georgia", 15f, FontStyle.Bold), BackColor = Color.Transparent };
    private readonly CheckBox _hudFollowLink = new() { Text = "Seguir a Link", AutoSize = true, ForeColor = Color.FromArgb(236, 224, 198), Font = Theme.Small, BackColor = Color.Transparent, Margin = new Padding(4, 5, 8, 0), Cursor = Cursors.Hand };
    private readonly Label _hudZoomLabel = new() { Text = "1.0x", AutoSize = true, ForeColor = Theme.Gold, Font = Theme.UiBold, BackColor = Color.Transparent, Margin = new Padding(6, 5, 6, 0) };
    private readonly CheckBox _hudOnlyMissing = new() { Text = "Solo pendientes", AutoSize = true, ForeColor = Color.FromArgb(215, 205, 185), Font = Theme.Small, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 6), Cursor = Cursors.Hand };
    private readonly FlowLayoutPanel _hudBadges = new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
    private readonly Panel _detailCard = new() { Visible = false, BackColor = Color.FromArgb(238, 22, 25, 31), Padding = new Padding(12), Width = 340, AutoSize = true, MaximumSize = new Size(360, 420) };
    private readonly Label _detailTitle = Ui.Label("", bold: true);
    private readonly FlowLayoutPanel _detailList = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, MaximumSize = new Size(330, 340), AutoScroll = true, BackColor = Color.Transparent };
    private string? _activeHudType;
    private readonly Label _age = Ui.Hint("Edad: â€”");
    private readonly Label _mapState = Ui.Hint("Mapa: â€”");
    private readonly Label _progressSkull = Ui.Label("Skulltulas 0/0", muted: true);
    private readonly Label _progressHearts = Ui.Label("Piezas de CorazÃ³n 0/0", muted: true);
    private readonly Label _progressBeans = Ui.Label("Frijoles MÃ¡gicos 0/0", muted: true);
    private readonly Label _progressDone = Ui.Label("Conseguidos 0/0", muted: true);

    private readonly MapView _map = new();
    private readonly List<string> _floorPaths = new();
    private bool _updatingFloors;

    private string? _currentBanc;
    private bool _isChild = true;
    private BindingList<Row> _rows = new();
    private BindingList<Row> _liveRows = new();
    private List<CollectibleDto> _items = new();

    public CollectiblesPage(MainForm host) : base(host)
    {
        BuildUi();
        LoadItems();
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = Theme.Ui;
        _tabs.BackColor = Theme.Bg;
        StyleTabs(_tabs);

        _tabs.TabPages.Add(BuildLiveTab());
        _tabs.TabPages.Add(BuildCatalogTab());
        Controls.Add(_tabs);
    }

    private static void StyleTabs(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.ItemSize = new Size(190, 32);
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.Padding = new Point(12, 6);
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            Rectangle rect = tabs.GetTabRect(e.Index);
            using (var fill = new SolidBrush(selected ? Theme.Card : Theme.Sidebar))
                e.Graphics.FillRectangle(fill, rect);
            using (var border = new Pen(selected ? Theme.Accent : Theme.Border))
                e.Graphics.DrawRectangle(border, rect);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
                selected ? Theme.UiBold : Theme.Ui, rect,
                selected ? Theme.Text : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }

    private TabPage BuildLiveTab()
    {
        var page = new TabPage("Mapa en vivo") { BackColor = Theme.Bg, ForeColor = Theme.Text, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var topBar = new Panel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Theme.Card, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 0, 8) };
        var header = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        header.Controls.Add(Ui.Section("MAPA EN VIVO"));
        _mapState.Margin = new Padding(10, 7, 0, 0);
        header.Controls.Add(_mapState);
        _age.Margin = new Padding(10, 7, 0, 0);
        _age.ForeColor = Theme.Gold;
        header.Controls.Add(_age);

        var rightControls = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        var floorLbl = Ui.Label("Piso:", muted: true, bold: true);
        floorLbl.Margin = new Padding(0, 6, 6, 0);
        rightControls.Controls.Add(floorLbl);
        _floor.Margin = new Padding(0, 2, 0, 0);
        _floor.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFloors || _floor.SelectedIndex < 0 || _floor.SelectedIndex >= _floorPaths.Count) return;
            _map.TextureOverride = _floorPaths[_floor.SelectedIndex];
            _map.ReloadMap();
        };
        rightControls.Controls.Add(_floor);
        topBar.Controls.Add(header);
        topBar.Controls.Add(rightControls);
        layout.Controls.Add(topBar, 0, 0);

        // Fondo cálido estilo pergamino oscuro del juego
        Color gameMapBg = Color.FromArgb(28, 25, 20);
        var mapHost = new Panel { Dock = DockStyle.Fill, BackColor = gameMapBg, Padding = new Padding(4) };
        _map.Dock = DockStyle.Fill;
        _map.BackColor = gameMapBg;

        var detailHeader = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
        _detailTitle.Dock = DockStyle.Left;
        _detailTitle.AutoSize = true;
        _detailTitle.ForeColor = Theme.Gold;
        var closeBtn = new Label { Text = "✕", Dock = DockStyle.Right, Width = 22, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Font = Theme.UiBold };
        closeBtn.Click += (_, _) => { _detailCard.Visible = false; _activeHudType = null; };
        detailHeader.Controls.Add(_detailTitle);
        detailHeader.Controls.Add(closeBtn);

        _detailList.Dock = DockStyle.Top;
        _detailCard.BackColor = Color.FromArgb(242, 26, 23, 18);
        _detailCard.Controls.Clear();
        _detailCard.Controls.Add(_detailList);
        _detailCard.Controls.Add(detailHeader);
        _detailCard.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(165, 138, 86), 1.5f); e.Graphics.DrawRectangle(pen, 0, 0, _detailCard.Width - 1, _detailCard.Height - 1); };

        var hudOverlay = new Panel
        {
            Location = new Point(16, 12),
            AutoSize = true,
            BackColor = Color.FromArgb(205, 22, 20, 16),
            Padding = new Padding(10, 8, 12, 8)
        };
        var hudStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        var goldLine = new Panel { Height = 2, Width = 175, BackColor = Color.FromArgb(195, 165, 95), Margin = new Padding(0, 2, 0, 6) };
        _hudOnlyMissing.CheckedChanged += (_, _) => _map.Invalidate();

        hudStack.Controls.Add(_hudZoneTitle);
        hudStack.Controls.Add(goldLine);
        hudStack.Controls.Add(_hudOnlyMissing);
        hudStack.Controls.Add(_hudBadges);
        hudOverlay.Controls.Add(hudStack);

        // Tarjeta desplegable situada justo a la derecha de las etiquetas verticales
        _detailCard.Location = new Point(215, 12);

        var zoomOverlay = new Panel
        {
            Width = 38,
            Height = 114,
            BackColor = Color.FromArgb(225, 24, 21, 17),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        var btnZoomIn = new Button
        {
            Text = "",
            Location = new Point(0, 0),
            Size = new Size(38, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        btnZoomIn.FlatAppearance.BorderSize = 0;
        btnZoomIn.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 46, 36);

        var btnFollow = new Button
        {
            Text = "",
            Location = new Point(0, 38),
            Size = new Size(38, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        btnFollow.FlatAppearance.BorderSize = 0;
        btnFollow.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 46, 36);

        var btnZoomOut = new Button
        {
            Text = "",
            Location = new Point(0, 76),
            Size = new Size(38, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        btnZoomOut.FlatAppearance.BorderSize = 0;
        btnZoomOut.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 46, 36);

        btnZoomIn.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Gold, 2.4f);
            e.Graphics.DrawLine(pen, 12, 19, 26, 19);
            e.Graphics.DrawLine(pen, 19, 12, 19, 26);
        };

        btnFollow.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color c = _map.FollowLink ? Theme.Gold : Color.FromArgb(145, 138, 124);
            using var pen = new Pen(c, 1.8f);
            using var dot = new SolidBrush(c);
            e.Graphics.DrawEllipse(pen, 11, 11, 16, 16);
            e.Graphics.FillEllipse(dot, 16.5f, 16.5f, 5f, 5f);
            e.Graphics.DrawLine(pen, 19, 7, 19, 11);
            e.Graphics.DrawLine(pen, 19, 27, 19, 31);
            e.Graphics.DrawLine(pen, 7, 19, 11, 19);
            e.Graphics.DrawLine(pen, 27, 19, 31, 19);
        };

        btnZoomOut.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Gold, 2.4f);
            e.Graphics.DrawLine(pen, 12, 19, 26, 19);
        };

        zoomOverlay.Paint += (_, e) =>
        {
            using var border = new Pen(Color.FromArgb(155, 128, 76), 1.3f);
            e.Graphics.DrawRectangle(border, 0, 0, zoomOverlay.Width - 1, zoomOverlay.Height - 1);
            e.Graphics.DrawLine(border, 4, 38, zoomOverlay.Width - 5, 38);
            e.Graphics.DrawLine(border, 4, 76, zoomOverlay.Width - 5, 76);
        };

        btnZoomIn.Click += (_, _) =>
        {
            _map.Zoom = Math.Min(4.5f, _map.Zoom + 0.35f);
            _map.Invalidate();
        };

        btnFollow.Click += (_, _) =>
        {
            _map.FollowLink = !_map.FollowLink;
            if (_map.FollowLink) _map.PanOffset = PointF.Empty;
            btnFollow.Invalidate();
            _map.Invalidate();
        };

        btnZoomOut.Click += (_, _) =>
        {
            _map.Zoom = Math.Max(0.75f, _map.Zoom - 0.35f);
            _map.Invalidate();
        };

        _map.ViewChanged += () => btnFollow.Invalidate();

        zoomOverlay.Controls.Add(btnZoomIn);
        zoomOverlay.Controls.Add(btnFollow);
        zoomOverlay.Controls.Add(btnZoomOut);

        mapHost.Resize += (_, _) =>
        {
            zoomOverlay.Location = new Point(Math.Max(12, mapHost.ClientSize.Width - zoomOverlay.Width - 18), Math.Max(12, mapHost.ClientSize.Height - zoomOverlay.Height - 18));
        };
        mapHost.Controls.Add(hudOverlay);
        mapHost.Controls.Add(_detailCard);
        mapHost.Controls.Add(zoomOverlay);
        mapHost.Controls.Add(_map);
        hudOverlay.BringToFront();
        _detailCard.BringToFront();
        zoomOverlay.BringToFront();

        layout.Controls.Add(mapHost, 0, 1);
        page.Controls.Add(layout);
        return page;
    }
    private TabPage BuildCatalogTab()
    {
        var page = new TabPage("Catálogo y Progreso")
        {
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Padding = new Padding(10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Contadores de progreso por tipo.
        var progress = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Card,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 8)
        };
        progress.Controls.Add(Ui.Section("PROGRESO"));
        foreach (Label label in new[] { _progressSkull, _progressHearts, _progressBeans, _progressDone })
        {
            label.Margin = new Padding(14, 6, 0, 0);
            label.Font = Theme.UiBold;
        }
        _progressSkull.ForeColor = Theme.Gold;
        _progressHearts.ForeColor = Color.FromArgb(220, 110, 120);
        _progressBeans.ForeColor = Color.FromArgb(150, 220, 120);
        _progressDone.ForeColor = Theme.Accent;
        progress.Controls.Add(_progressSkull);
        progress.Controls.Add(_progressHearts);
        progress.Controls.Add(_progressBeans);
        progress.Controls.Add(_progressDone);
        layout.Controls.Add(progress, 0, 0);

        // Filtros del catÃ¡logo.
        var filters = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 6)
        };
        filters.Controls.Add(Ui.Label("Zona:", muted: true, bold: true));
        _zoneFilter.Margin = new Padding(6, 0, 14, 0);
        _zoneFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        filters.Controls.Add(_zoneFilter);

        filters.Controls.Add(Ui.Label("Tipo:", muted: true, bold: true));
        _typeFilter.Margin = new Padding(6, 0, 14, 0);
        _typeFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        filters.Controls.Add(_typeFilter);

        _onlyMissing.Margin = new Padding(0, 2, 14, 0);
        _onlyMissing.CheckedChanged += (_, _) => ApplyFilters();
        filters.Controls.Add(_onlyMissing);

        _count.Margin = new Padding(0, 8, 0, 0);
        filters.Controls.Add(_count);
        layout.Controls.Add(filters, 0, 1);

        // Tabla completa a ancho total.
        _grid.Columns.Clear();
        _grid.Columns.Add(Ui.TextColumn("Estado", nameof(Row.Estado), 100, format: "notmono"));
        _grid.Columns.Add(Ui.TextColumn("Zona", nameof(Row.Zona), 120, format: "notmono"));
        _grid.Columns.Add(Ui.TextColumn("Tipo", nameof(Row.Tipo), 110, format: "notmono"));
        _grid.Columns.Add(Ui.TextColumn("Ã‰poca", nameof(Row.Epoca), 90, format: "notmono"));
        _grid.Columns.Add(Ui.TextColumn("Nombre", nameof(Row.Nombre), 180, format: "notmono"));
        _grid.Columns.Add(Ui.FillColumn("UbicaciÃ³n", nameof(Row.UbicacionExacta)));
        _grid.DataSource = _rows;
        _grid.SelectionChanged += (_, _) => UpdateSelection(_grid);
        _grid.CellFormatting += (sender, e) =>
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0 &&
                _grid.Rows[e.RowIndex].DataBoundItem is Row row)
            {
                e.CellStyle!.ForeColor = row.Estado.Contains("Conseguida") ? Theme.Accent : Theme.Gold;
            }
        };
        layout.Controls.Add(_grid, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    // ---------------------------------------------------------------- datos

    private static string[] CandidatePaths => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "coleccionables.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhykroTrainer", "coleccionables.json")
    };

    private void LoadItems()
    {
        foreach (string path in CandidatePaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
                _items = JsonSerializer.Deserialize<List<CollectibleDto>>(File.ReadAllText(path), options)
                         ?? new List<CollectibleDto>();
                break;
            }
            catch
            {
                // JSON corrupto: se cae al siguiente candidato o a la lista vacÃ­a.
            }
        }

        _zoneFilter.Items.Clear();
        _zoneFilter.Items.Add("Todas las zonas");
        foreach (string zone in _items.Select(i => i.Zona).Where(z => z.Length > 0).Distinct().OrderBy(z => z))
            _zoneFilter.Items.Add(zone);
        _zoneFilter.SelectedIndex = 0;

        _typeFilter.Items.Clear();
        _typeFilter.Items.Add("Todos los tipos");
        foreach (string tipo in _items.Select(i => i.Tipo).Where(t => t.Length > 0).Distinct().OrderBy(t => t))
            _typeFilter.Items.Add(tipo);
        _typeFilter.SelectedIndex = 0;

        RebuildRows();
    }

    private void RebuildRows()
    {
        _rows = new BindingList<Row>(_items.Select(i => new Row(i)).ToList());
        _liveRows = new BindingList<Row>(_items.Select(i => new Row(i)).ToList());
        _grid.DataSource = _rows;
        _liveGrid.DataSource = _liveRows;

        ApplyFilters();
        ApplyLiveFilter();
        UpdateProgress();
    }

    /// <summary>Filtra la pestaÃ±a Â«Catálogo y ProgresoÂ» (zona, tipo y solo faltantes).</summary>
    private void ApplyFilters()
    {
        string zone = _zoneFilter.SelectedItem as string ?? "Todas las zonas";
        string tipo = _typeFilter.SelectedItem as string ?? "Todos los tipos";
        bool onlyMissing = _onlyMissing.Checked;

        // Ocultar filas cambia la posiciÃ³n del CurrencyManager: limpiamos la selecciÃ³n
        // antes para evitar Â«Row ... cannot be made invisibleÂ».
        _grid.ClearSelection();
        _grid.CurrentCell = null;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not Row item) continue;
            row.Visible = (zone == "Todas las zonas" || item.Zona == zone) &&
                          (tipo == "Todos los tipos" || item.Tipo == tipo) &&
                          (!onlyMissing || item.Estado != "Conseguida");
        }

        int visibleCount = _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
        _count.Text = $"{visibleCount} de {_items.Count} coleccionable(s)";
    }

    /// <summary>La lista Â«Mapa en vivoÂ» siempre muestra solo el mapa actual.</summary>

    private void ApplyLiveFilter()
    {
        string? selectedId = (_liveGrid.CurrentRow?.DataBoundItem as Row)?.Dto.Id;
        _liveRows.Clear();

        if (_currentBanc is not { Length: > 0 } banc)
        {
            _liveCount.Text = "Sin mapa (conecta el juego).";
            return;
        }

        foreach (Row row in _rows)
        {
            if (string.Equals(row.Dto.MapId.Replace("_Child","").Replace("_Adult",""), banc.Replace("_Child","").Replace("_Adult",""), StringComparison.OrdinalIgnoreCase))
                _liveRows.Add(row);
        }

        _liveCount.Text = $"{_liveRows.Count} en {banc}";
        string prettyZone = _liveRows.FirstOrDefault()?.Zona ?? banc;
        _hudZoneTitle.Text = prettyZone;
        RefreshHudBadges();

        if (selectedId is not null)
        {
            for (int i = 0; i < _liveRows.Count; i++)
            {
                if (_liveRows[i].Dto.Id == selectedId)
                {
                    _liveGrid.Rows[i].Selected = true;
                    break;
                }
            }
        }
    }

    private void UpdateSelection(DataGridView grid)
    {
        _map.SelectedId = (grid.CurrentRow?.DataBoundItem as Row)?.Dto.Id;
        _map.Invalidate();
    }

    private bool MatchesCurrentMapAndAge(CollectibleDto dto)
    {
        if (_currentBanc is not { Length: > 0 } banc) return false;
        string baseItem = (dto.Mapa ?? "").Replace("_Child", "", StringComparison.OrdinalIgnoreCase)
                                            .Replace("_Adult", "", StringComparison.OrdinalIgnoreCase);
        string baseBanc = banc.Replace("_Child", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("_Adult", "", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(baseItem, baseBanc, StringComparison.OrdinalIgnoreCase)) return false;

        string ep = dto.Epoca ?? "";
        if (string.IsNullOrWhiteSpace(ep)) return true;
        if (ep.Contains("Niño", StringComparison.OrdinalIgnoreCase) && ep.Contains("Adulto", StringComparison.OrdinalIgnoreCase)) return true;
        return _isChild
            ? ep.Contains("Niño", StringComparison.OrdinalIgnoreCase)
            : ep.Contains("Adulto", StringComparison.OrdinalIgnoreCase);
    }

    private string _lastHudSignature = "";

    private static string GetHudCategory(string tipo)
    {
        if (tipo.Contains("Skulltula", StringComparison.OrdinalIgnoreCase)) return "Skulltulas";
        if (tipo.Contains("Corazón", StringComparison.OrdinalIgnoreCase)) return "Piezas de Corazón";
        if (tipo.Contains("Frijol", StringComparison.OrdinalIgnoreCase)) return "Frijoles Mágicos";
        return "Hojas Mágicas";
    }

    private void RefreshHudBadges()
    {
        string[] cats = { "Skulltulas", "Piezas de Corazón", "Frijoles Mágicos", "Hojas Mágicas" };
        var parts = new List<string>();
        foreach (string cat in cats)
        {
            var group = _liveRows.Where(r => GetHudCategory(r.Dto.Tipo) == cat).ToList();
            if (group.Count > 0)
            {
                int done = group.Count(r => r.Estado == "Conseguida");
                parts.Add($"{cat}:{done}/{group.Count}");
            }
        }

        string sig = string.Join("|", parts);
        if (sig == _lastHudSignature && _hudBadges.Controls.Count > 0) return;
        _lastHudSignature = sig;

        _hudBadges.SuspendLayout();
        _hudBadges.Controls.Clear();

        foreach (string cat in cats)
        {
            var group = _liveRows.Where(r => GetHudCategory(r.Dto.Tipo) == cat).ToList();
            if (group.Count == 0) continue;

            int done = group.Count(r => r.Estado == "Conseguida");
            _hudBadges.Controls.Add(CreateHudBadge(cat, done, group.Count));
        }

        _hudBadges.ResumeLayout(false);
    }

    private Control CreateHudBadge(string category, int done, int total)
    {
        var panel = new Panel
        {
            Width = 86,
            Height = 28,
            Margin = new Padding(0, 0, 0, 5),
            BackColor = Color.FromArgb(46, 42, 36),
            Cursor = Cursors.Hand
        };

        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool activeNow = _detailCard.Visible && _activeHudType == category;
            using var border = new Pen(activeNow ? Theme.Gold : Color.FromArgb(120, 102, 72), 1.4f);
            e.Graphics.DrawRectangle(border, 0, 0, panel.Width - 1, panel.Height - 1);

            float cx = 16f, cy = 14f;
            if (category == "Skulltulas")
            {
                using var gold = new SolidBrush(Color.FromArgb(232, 190, 74));
                using var dark = new SolidBrush(Color.FromArgb(75, 50, 15));
                e.Graphics.FillEllipse(gold, cx - 7, cy - 7, 14, 12);
                e.Graphics.FillRectangle(gold, cx - 4, cy + 2, 8, 5);
                e.Graphics.FillEllipse(dark, cx - 4.5f, cy - 3, 3.5f, 3.5f);
                e.Graphics.FillEllipse(dark, cx + 1f, cy - 3, 3.5f, 3.5f);
            }
            else if (category == "Piezas de Corazón")
            {
                float s = 8.5f;
                float midX = cx + 1.5f;
                using var silverFill = new SolidBrush(Color.FromArgb(168, 178, 190));
                using var silverRim = new Pen(Color.FromArgb(72, 82, 94), 1.1f);
                using var rubyFill = new SolidBrush(Color.FromArgb(224, 36, 48));

                using var outerPath = new System.Drawing.Drawing2D.GraphicsPath();
                outerPath.AddBezier(midX, cy - s * 0.42f, midX - s * 0.45f, cy - s * 1.05f, midX - s * 1.25f, cy - s * 0.35f, midX - s * 0.85f, cy + s * 0.35f);
                outerPath.AddLine(midX - s * 0.85f, cy + s * 0.35f, midX, cy + s * 1.05f);
                outerPath.CloseFigure();
                e.Graphics.FillPath(silverFill, outerPath);
                e.Graphics.DrawPath(silverRim, outerPath);

                using var innerPath = new System.Drawing.Drawing2D.GraphicsPath();
                float inX = midX - 1f;
                innerPath.AddBezier(inX, cy - s * 0.24f, inX - s * 0.32f, cy - s * 0.68f, inX - s * 0.85f, cy - s * 0.22f, inX - s * 0.56f, cy + s * 0.24f);
                innerPath.AddLine(inX - s * 0.56f, cy + s * 0.24f, inX, cy + s * 0.72f);
                innerPath.CloseFigure();
                e.Graphics.FillPath(rubyFill, innerPath);
            }
            else
            {
                using var green = new SolidBrush(Color.FromArgb(110, 210, 110));
                e.Graphics.FillEllipse(green, cx - 7, cy - 4, 14, 8);
            }

            string text = $"{done}/{total}";
            using var fg = new SolidBrush(done == total ? Color.FromArgb(130, 230, 140) : Theme.Text);
            e.Graphics.DrawString(text, Theme.UiBold, fg, 30f, 5f);
        };

        panel.Click += (_, _) =>
        {
            if (_detailCard.Visible && _activeHudType == category)
            {
                _detailCard.Visible = false;
                _activeHudType = null;
            }
            else
            {
                ShowHudDetailsForType(category, null);
            }
        };

        return panel;
    }

    private void ShowHudDetailsForType(string category, string? highlightId)
    {
        _activeHudType = category;
        var items = _liveRows.Where(r => GetHudCategory(r.Dto.Tipo) == category).ToList();
        int done = items.Count(r => r.Estado == "Conseguida");
        _detailTitle.Text = $"{category} ({done}/{items.Count})";

        _detailList.SuspendLayout();
        _detailList.Controls.Clear();

        foreach (Row row in items)
        {
            bool isSel = row.Dto.Id == (highlightId ?? _map.SelectedId);
            bool isDone = row.Estado == "Conseguida";
            string epoca = row.Dto.Epoca ?? "";
            bool blockedByAge = !isDone && (
                (_isChild && epoca.Contains("Adulto", StringComparison.OrdinalIgnoreCase) && !epoca.Contains("Niño", StringComparison.OrdinalIgnoreCase)) ||
                (!_isChild && epoca.Contains("Niño", StringComparison.OrdinalIgnoreCase) && !epoca.Contains("Adulto", StringComparison.OrdinalIgnoreCase))
            );

            string checkBox = isDone ? "[✓]" : (blockedByAge ? "[⨯]" : "[ ]");
            Color textColor = isDone
                ? Color.FromArgb(120, 220, 130)
                : (blockedByAge ? Color.FromArgb(165, 135, 115) : Theme.Text);

            string shortName = System.Text.RegularExpressions.Regex.Replace(row.Nombre, @"\s*\([^)]*\)\s*$", "").Trim();

            var rowTable = new TableLayoutPanel
            {
                Width = 304,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = isSel ? Color.FromArgb(68, 58, 40) : Color.FromArgb(36, 32, 26),
                Margin = new Padding(0, 4, 0, 0),
                Padding = new Padding(8, 5, 6, 5)
            };
            rowTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rowTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var itemLabel = new Label
            {
                Text = $"{checkBox} {shortName}",
                ForeColor = textColor,
                BackColor = Color.Transparent,
                Font = Theme.UiBold,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 3, 0, 0),
                Cursor = Cursors.Hand
            };

            var tpBtn = new Button
            {
                Text = "Ir aquí",
                Width = 60,
                Height = 24,
                Anchor = AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(74, 62, 40),
                ForeColor = Theme.Gold,
                Font = Theme.Small,
                Cursor = Cursors.Hand,
                Margin = new Padding(6, 0, 0, 0)
            };
            tpBtn.FlatAppearance.BorderColor = Color.FromArgb(155, 128, 76);

            CollectibleDto targetDto = row.Dto;
            string rowId = row.Dto.Id;
            itemLabel.Click += (_, _) =>
            {
                _map.SelectedId = rowId;
                _map.Invalidate();
            };
            tpBtn.Click += (_, _) =>
            {
                _map.SelectedId = rowId;
                _map.Invalidate();
                TeleportToCollectible(targetDto);
            };

            rowTable.Controls.Add(itemLabel, 0, 0);
            rowTable.Controls.Add(tpBtn, 1, 0);
            _detailList.Controls.Add(rowTable);
        }

        _detailList.AutoScrollPosition = Point.Empty;
        _detailList.ResumeLayout(true);
        _detailCard.PerformLayout();
        _detailCard.Visible = true;
        _detailCard.BringToFront();
    }
    private void TeleportToCollectible(CollectibleDto dto)
    {
        if (Host.Memory is not { } memory)
        {
            Host.SetStatus("Conecta el juego primero para teletransportar.", true);
            return;
        }
        if (dto.WorldX == 0 && dto.WorldY == 0 && dto.WorldZ == 0)
        {
            Host.SetStatus("Este coleccionable aún no tiene coordenadas.", true);
            return;
        }
        if (!AddressSpec.TryParse(LinkPosSpecX, out AddressSpec sx, out _) ||
            !AddressSpec.TryParse(LinkPosSpecY, out AddressSpec sy, out _) ||
            !AddressSpec.TryParse(LinkPosSpecZ, out AddressSpec sz, out _) ||
            !sx.TryResolve(memory, out long ax, out _) ||
            !sy.TryResolve(memory, out long ay, out _) ||
            !sz.TryResolve(memory, out long az, out _))
        {
            Host.SetStatus("No se pudo resolver la posición de Link en memoria.", true);
            return;
        }

        memory.TryWrite(ax, BitConverter.GetBytes(dto.WorldX));
        memory.TryWrite(ay, BitConverter.GetBytes(dto.WorldY + 0.5f));
        memory.TryWrite(az, BitConverter.GetBytes(dto.WorldZ));
        Host.Notify($"Teletransportado a {dto.Nombre}");
    }
    private void SelectRowInGrids(Row row)
    {
        _map.SelectedId = row.Dto.Id;
        _map.Invalidate();
    }
    private void UpdateProgress()
    {
        int skullTotal = 0, skullDone = 0;
        int heartTotal = 0, heartDone = 0;
        int beanTotal = 0, beanDone = 0;
        int done = 0;

        foreach (Row row in _rows)
        {
            bool conseguida = row.Estado == "Conseguida";
            if (conseguida) done++;

            string tipo = row.Tipo;
            if (tipo.Contains("Skulltula", StringComparison.OrdinalIgnoreCase))
            {
                skullTotal++;
                if (conseguida) skullDone++;
            }
            else if (tipo.Contains("Pieza de Coraz", StringComparison.OrdinalIgnoreCase))
            {
                heartTotal++;
                if (conseguida) heartDone++;
            }
            else if (tipo.Contains("Frijol", StringComparison.OrdinalIgnoreCase))
            {
                beanTotal++;
                if (conseguida) beanDone++;
            }
        }

        _progressSkull.Text = $"Skulltulas {skullDone}/{skullTotal}";
        _progressHearts.Text = $"Piezas de CorazÃ³n {heartDone}/{heartTotal}";
        _progressBeans.Text = $"Frijoles MÃ¡gicos {beanDone}/{beanTotal}";
        _progressDone.Text = $"Conseguidos {done}/{_rows.Count}";
    }

    // ---------------------------------------------------------------- tick

    public override void OnProfileChanged()
    {
        LoadItems();
        TryStartMapExtraction();
    }

    private bool _extractionStarted;
    private List<MapCalibration> _calibration = new();

    private void TryStartMapExtraction()
    {
        if (_extractionStarted || GameMapExtractor.IsExtracted(AppContext.BaseDirectory)) return;
        if (Host.Memory is not { IsAlive: true } memory) return;

        string configured = Host.Profile.GamePath;
        string? gamePath = Directory.Exists(Path.Combine(configured, "romfs"))
            ? configured
            : GameMapExtractor.FindGamePath(memory.ProcessId);
        if (gamePath is null)
        {
            Host.SetStatus("No encontrÃ© la carpeta del juego (romfs) para extraer los mapas.", true);
            return;
        }

        _extractionStarted = true;
        Host.SetStatus("Extrayendo mapas del juego... (solo la primera vez)");
        Log.Write("Mapas: maps/ incompleta al conectar; iniciando extracciÃ³n.");
        _ = GameMapExtractor.ExtractAsync(gamePath, AppContext.BaseDirectory,
            status => { try { Host.SetStatus(status); } catch { /* UI cerrada */ } },
            () =>
            {
                try
                {
                    _calibration = LoadCalibration();
                    _map.SetCalibration(_calibration);
                    _map.ReloadMap();
                    UpdateFloorSelector(force: true);
                }
                catch (Exception ex)
                {
                    Log.Error("mapas:recarga UI", ex);
                }
            });
    }

    private static List<MapCalibration> LoadCalibration()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "maps", "map-calibration.json");
            if (!File.Exists(path)) return new List<MapCalibration>();
            return System.Text.Json.JsonSerializer.Deserialize<List<MapCalibration>>(
                       File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<MapCalibration>();
        }
        catch
        {
            return new List<MapCalibration>();
        }
    }

    public override void OnTick()
    {
        GameMemory? memory = Host.Memory;
        if (memory is not { IsAlive: true })
        {
            SetAgeLabel(null);
            return;
        }

        bool? rawAge = GameFlagsReader.ReadFlagByHash(memory, AgeHash); bool? isChild = rawAge is null ? null : !rawAge.Value;
        SetAgeLabel(isChild);
        bool childNow = isChild != false;
        if (_isChild != childNow || _map.AdultLink == childNow)
        {
            _isChild = childNow;
            _map.AdultLink = !childNow;
            _map.ReloadMap();
            UpdateFloorSelector(force: true);
        }

        string? mapName = GameFlagsReader.ReadText64(memory, "Sequence_CurrentBanc");
        _mapState.Text = string.IsNullOrEmpty(mapName) ? "Mapa: â€”" : $"Mapa: {mapName}";

        if (_calibration.Count == 0 && GameMapExtractor.IsExtracted(AppContext.BaseDirectory))
        {
            _calibration = LoadCalibration();
            _map.SetCalibration(_calibration);
        }

        string? previousBanc = _currentBanc;
        _currentBanc = string.IsNullOrEmpty(mapName) ? null : mapName;
        if (previousBanc != _currentBanc) { _lastAutoFloor = null; _lastAutoState = null; }
        if (previousBanc != _currentBanc) { _lastAutoFloor = null; _lastAutoState = null; }
        if (previousBanc != _currentBanc)
        {
            ApplyLiveFilter();
            _map.CurrentMapName = _currentBanc;
            _map.TextureOverride = null;
            _map.ReloadMap();
            UpdateFloorSelector(force: true);
        }

        HashSet<ulong> bool64Active = GameFlagsReader.ReadBool64Set(memory);
        bool changed = false;
        var estados = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Row row in _rows)
        {
            string estado = ReadEstado(memory, row.Dto, bool64Active);
            estados[row.Dto.Id] = estado;
            if (row.Estado != estado)
            {
                row.Estado = estado;
                changed = true;
            }
        }
        foreach (Row row in _liveRows)
        {
            if (estados.TryGetValue(row.Dto.Id, out string? estado) && row.Estado != estado)
                row.Estado = estado;
        }
        if (changed)
        {
            ApplyFilters();
            UpdateProgress();
            RefreshHudBadges();
        }

        // PosiciÃ³n de Link en vivo (X y Z flotantes a travÃ©s del mismo puntero).
        _map.LinkPosition = ReadLinkPosition(memory);
        _map.IsChild = _isChild; _map.LinkAngle = ReadLinkAngle(memory);

        string? autoState = DetectAutoStateSuffix(memory, _currentBanc, _isChild);
        if (autoState is not null && autoState != _lastAutoState)
        {
            _lastAutoState = autoState;
            SelectTextureBySuffix(autoState);
        }

        string? autoFloor = DetectFloorTag(_currentBanc, ReadLinkY(memory));
        if (autoFloor is not null && autoFloor != _lastAutoFloor)
        {
            _lastAutoFloor = autoFloor;
            SelectFloorByTag(autoFloor);
        }

        // Solo los pines del mapa actual (nunca los 185 amontonados).
        _map.Pins = _currentBanc is { Length: > 0 } current
            ? _items
                .Where(dto => string.Equals(dto.Mapa.Replace("_Child","").Replace("_Adult",""), current.Replace("_Child","").Replace("_Adult",""), StringComparison.OrdinalIgnoreCase))
                .Where(dto => dto.WorldX != 0 || dto.WorldZ != 0)
                .Where(dto => MatchesFloor(dto, current, GetSelectedFloorTag()))
                .Select(dto => (dto, ReadEstadoCached(estados, dto)))
                .ToList()
            : new List<(CollectibleDto, bool)>();
        _map.Invalidate();
    }

        private static bool IsEventCompleted(GameMemory memory, params string[] names)
    {
        foreach (string name in names)
        {
            uint h = GameFlagsReader.Murmur3(name);
            if (GameFlagsReader.ReadFlagByHash(memory, h) == true) return true;
        }
        return false;
    }

    private static string? DetectAutoStateSuffix(GameMemory memory, string? banc, bool isChild)
    {
        if (string.IsNullOrEmpty(banc)) return null;
        string b = banc.Replace("_Child", "").Replace("_Adult", "");

        if (b.Equals("Graveyard", StringComparison.OrdinalIgnoreCase))
            return IsEventCompleted(memory, "GraveYard_RoyalFamilyTomb_Breaked") ? "_01" : "_00";

        if (b.Equals("KakarikoVillage", StringComparison.OrdinalIgnoreCase) && isChild)
            return IsEventCompleted(memory, "KakarikoVillage_Npc_Hylia_009_Child_OcarinaPlayedFirsttime", "KakarikoVillage_Npc_Hylia_009_SoonAfterStormSong", "Blow_Ocarina") ? "_00" : "_01";

        if (b.Equals("KokiriForest", StringComparison.OrdinalIgnoreCase) && isChild)
            return IsEventCompleted(memory, "Dm_Kokiri_0030", "KokiriForest_OpenDekuTree") ? "_01" : "_00";

        if (b.Equals("LakeHylia", StringComparison.OrdinalIgnoreCase) && !isChild)
            return IsEventCompleted(memory, "Dm_Awake_Water", "Dm_LakeHrl_0000", "Dm_WaterTemple_0000") ? "_01" : "_00";

        if (b.Equals("GerudoValley", StringComparison.OrdinalIgnoreCase) && !isChild)
            return IsEventCompleted(memory, "GerudoFortress_Saved_Npc_Hylia_016_Adult", "Dm_GerudoValley_0000") ? "_01" : "_00";

        if (b.Equals("DesertColossus", StringComparison.OrdinalIgnoreCase))
            return IsEventCompleted(memory, "DesertColossus_IsStormSongPlayed_01", "DesertColossus_IsStormSongPlayed_02") ? "_01" : "_00";

        if (b.Equals("GanonCastle", StringComparison.OrdinalIgnoreCase) && !isChild)
            return IsEventCompleted(memory, "GanonCastle_Adult_Field_Rainbow") ? "_01" : "_00";

        if ((b.Equals("ZoraDomain", StringComparison.OrdinalIgnoreCase) ||
             b.Equals("ZoraFountain", StringComparison.OrdinalIgnoreCase)) && !isChild)
            return IsEventCompleted(memory, "ZoraDomain_ExistWater") ? "_01" : "_00";

        return null;
    }

    private void SelectTextureBySuffix(string suffix)
    {
        for (int i = 0; i < _floorPaths.Count; i++)
        {
            string file = Path.GetFileNameWithoutExtension(_floorPaths[i]);
            if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                if (_floor.SelectedIndex != i) _floor.SelectedIndex = i;
                return;
            }
        }
    }
    private float? ReadLinkY(GameMemory memory)
    {
        if (!AddressSpec.TryParse(LinkPosSpecY, out AddressSpec specY, out _) ||
            !specY.TryResolve(memory, out long addrY, out _)) return null;
        byte[] buf = new byte[] { 0, 0, 0, 0 };
        return memory.TryRead(addrY, buf) ? BitConverter.ToSingle(buf, 0) : null;
    }

    internal static string? DetectFloorTag(string? banc, float? y)
    {
        if (string.IsNullOrEmpty(banc) || y is not { } py) return null;
        string b = banc.Replace("_Child", "").Replace("_Adult", "");
        if (b.Equals("InsideTheDekuTree", StringComparison.OrdinalIgnoreCase))
            return py >= 127f ? "3F" : py >= 117f ? "2F" : py >= 106f ? "1F" : py >= 93f ? "B1" : "B2";
        if (b.Equals("DodongoCavern", StringComparison.OrdinalIgnoreCase))
            return py >= 108f ? "2F" : "1F";
        if (b.Equals("InsideJabuJabuBelly", StringComparison.OrdinalIgnoreCase))
            return py >= 108f ? "2F" : py >= 98f ? "1F" : "B1";
        if (b.Equals("BottomOfTheWell", StringComparison.OrdinalIgnoreCase))
            return py >= 98f ? "B1" : "B2";
        if (b.Equals("ForestTemple", StringComparison.OrdinalIgnoreCase))
            return py >= 122f ? "3F" : py >= 111f ? "2F" : py >= 100f ? "1F" : py >= 90f ? "B1" : "B2";
        if (b.Equals("FireTemple", StringComparison.OrdinalIgnoreCase))
            return py >= 142f ? "5F" : py >= 131f ? "4F" : py >= 120f ? "3F" : py >= 109f ? "2F" : "1F";
        if (b.Equals("WaterTemple", StringComparison.OrdinalIgnoreCase))
            return py >= 122f ? "3F" : py >= 111f ? "2F" : py >= 100f ? "1F" : "B1";
        if (b.Equals("ShadowTemple", StringComparison.OrdinalIgnoreCase))
            return py >= 95f ? "B1" : py >= 82f ? "B2" : py >= 68f ? "B3" : "B4";
        if (b.Equals("SpiritTemple", StringComparison.OrdinalIgnoreCase))
            return py >= 134f ? "4F" : py >= 122f ? "3F" : py >= 110f ? "2F" : "1F";
        return null;
    }

    private void SelectFloorByTag(string tag)
    {
        for (int i = 0; i < _floor.Items.Count; i++)
        {
            if ((_floor.Items[i]?.ToString() ?? "").Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                if (_floor.SelectedIndex != i) _floor.SelectedIndex = i;
                return;
            }
        }
    }

    private string? GetSelectedFloorTag()
    {
        if (!_floor.Visible || _floor.SelectedItem is not string label) return null;
        if (label.Contains("All", StringComparison.OrdinalIgnoreCase) || label.Contains("Todo", StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (string tag in new[] { "5F", "4F", "3F", "2F", "1F", "B1", "B2", "B3", "B4" })
            if (label.Contains(tag, StringComparison.OrdinalIgnoreCase)) return tag;
        return null;
    }

    private static bool MatchesFloor(CollectibleDto dto, string currentBanc, string? selectedFloor)
    {
        if (selectedFloor is null) return true;
        string? itemFloor = DetectFloorTag(currentBanc, (float)dto.WorldY);
        return itemFloor is null || itemFloor.Equals(selectedFloor, StringComparison.OrdinalIgnoreCase);
    }
private static bool ReadEstadoCached(Dictionary<string, string> estados, CollectibleDto dto) =>
        estados.TryGetValue(dto.Id, out string? estado) && estado == "Conseguida";

    /// <summary>Rellena el selector de piso con los PNG disponibles del mapa actual.</summary>
    private void UpdateFloorSelector(bool force = false)
    {
        if (_currentBanc is not { Length: > 0 } banc)
        {
            if (_floorPaths.Count == 0 && !force) return;
            _updatingFloors = true;
            _floorPaths.Clear();
            _floor.Items.Clear();
            _floor.Enabled = false;
            _updatingFloors = false;
            return;
        }

        var paths = GameMapExtractor.ListTextures(
            Path.Combine(AppContext.BaseDirectory, "maps"), banc, _isChild);

        if (!force && paths.SequenceEqual(_floorPaths, StringComparer.OrdinalIgnoreCase)) return;

        _updatingFloors = true;
        _floorPaths.Clear();
        _floorPaths.AddRange(paths);
        _floor.Items.Clear();
        foreach (string path in paths)
            _floor.Items.Add(FloorLabel(path, banc));

        bool multiple = paths.Count > 1;
        _floor.Enabled = multiple;
        _floor.Visible = multiple;
        if (paths.Count > 0) _floor.SelectedIndex = 0;
        _updatingFloors = false;

        _map.TextureOverride = paths.Count > 0 ? paths[0] : null;
        _map.ReloadMap();
    }

    private static string FloorLabel(string path, string bancName)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith("_00", StringComparison.OrdinalIgnoreCase))
            return bancName.Equals("KakarikoVillage", StringComparison.OrdinalIgnoreCase) ? "Estado 2 (Pozo drenado)" : "Estado 1";
        if (name.EndsWith("_01", StringComparison.OrdinalIgnoreCase))
            return bancName.Equals("KakarikoVillage", StringComparison.OrdinalIgnoreCase) ? "Estado 1 (Pozo con agua)" : "Estado 2";
        if (name.Contains("_All", StringComparison.OrdinalIgnoreCase))
            return "Mapa completo";
        if (name.StartsWith(bancName + "_", StringComparison.OrdinalIgnoreCase))
            return name[(bancName.Length + 1)..];
        return name;
    }

    /// <summary>
    /// Lee la matriz 3x3 de rotaciÃ³n en [[0x7004E371D0]+0x480]+0x328 (9 Floats) y
    /// calcula el Ã¡ngulo de mirada con el vector forward (X, Z) vÃ­a Math.Atan2.
    /// </summary>
    private float ReadLinkAngle(GameMemory memory)
    {
        if (!AddressSpec.TryParse("0x7004E371D0,0x480,0x328", out AddressSpec spec, out _) ||
            !spec.TryResolve(memory, out long address, out _))
        {
            return 0f;
        }

        byte[] buffer = new byte[36];
        if (!memory.TryRead(address, buffer)) return 0f;

        float forwardX = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(24)); float forwardZ = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(32));
        if (forwardX == 0 && forwardZ == 0) return 0f;

        return MathF.Atan2(forwardZ, -forwardX);
    }

    private void SetAgeLabel(bool? isChild)
    {
        string text = isChild is null ? "Edad: â€”" : (isChild.Value ? "Edad: NiÃ±o" : "Edad: Adulto");
        if (_age.Text != text) _age.Text = text;
    }

    private static string ReadEstado(GameMemory memory, CollectibleDto dto, HashSet<ulong> bool64Active)
    {
        if (dto.Hash.Length > 0)
        {
            string hex = dto.Hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? dto.Hash[2..] : dto.Hash;
            if (hex.Length > 8 && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hash64))
                return bool64Active.Contains(hash64) ? "Conseguida" : "Pendiente";
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
            {
                return GameFlagsReader.ReadFlagByHash(memory, hash) is { } flag
                    ? (flag ? "Conseguida" : "Pendiente")
                    : "â€”";
            }
        }

        if (dto.DireccionBit.Length > 0 &&
            AddressSpec.TryParse(dto.DireccionBit, out AddressSpec spec, out _) &&
            spec.TryResolve(memory, out long address, out _))
        {
            return GameFlagsReader.ReadBit(memory, (ulong)address, dto.Bit) is { } bit
                ? (bit ? "Conseguida" : "Pendiente")
                : "â€”";
        }

        return "â€”";
    }

    private (float X, float Z)? ReadLinkPosition(GameMemory memory)
    {
        if (!AddressSpec.TryParse(LinkPosSpecX, out AddressSpec specX, out _) ||
            !AddressSpec.TryParse(LinkPosSpecZ, out AddressSpec specZ, out _))
        {
            return null;
        }

        if (!specX.TryResolve(memory, out long addressX, out _) ||
            !specZ.TryResolve(memory, out long addressZ, out _))
        {
            return null;
        }

        byte[] bufferX = new byte[4];
        byte[] bufferZ = new byte[4];
        if (!memory.TryRead(addressX, bufferX) || !memory.TryRead(addressZ, bufferZ)) return null;

        return (BinaryPrimitives.ReadSingleLittleEndian(bufferX),
                BinaryPrimitives.ReadSingleLittleEndian(bufferZ));
    }

    private void SaveCurrentPositionToRow()
    {
        if (Host.Memory is not { IsAlive: true } memory)
        {
            Host.SetStatus("Conecta el juego primero.", true);
            return;
        }

        DataGridView active = _tabs.SelectedIndex == 0 ? _liveGrid : _grid;
        if (active.CurrentRow?.DataBoundItem is not Row row)
        {
            Host.SetStatus("Selecciona un coleccionable en la tabla.", true);
            return;
        }

        (float X, float Z)? position = ReadLinkPosition(memory);
        if (position is not { } pos)
        {
            Host.SetStatus("No se pudo leer la posiciÃ³n de Link.", true);
            return;
        }

        string? banc = _currentBanc;
        row.Dto.WorldX = pos.X;
        row.Dto.WorldZ = pos.Z;
        if (!string.IsNullOrEmpty(banc)) row.Dto.Mapa = banc;

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(_items, options);
            string target = CandidatePaths.FirstOrDefault(File.Exists) ?? CandidatePaths[0];
            File.WriteAllText(target, json, System.Text.Encoding.UTF8);
            _grid.Refresh();
            _liveGrid.Refresh();
            ApplyLiveFilter();

            Clipboard.SetText($"{pos.X.ToString("0.###", CultureInfo.InvariantCulture)}, {pos.Z.ToString("0.###", CultureInfo.InvariantCulture)}");
            Host.SetStatus($"Guardado en Â«{row.Nombre}Â»: X {pos.X:0.###}, Z {pos.Z:0.###}" +
                           (banc is null ? "" : $" (mapa {banc})") + " Â· copiado al portapapeles.");
            _map.Invalidate();
        }
        catch (Exception ex)
        {
            Host.SetStatus("No se pudo copiar: " + ex.Message, true);
        }
    }

    // ---------------------------------------------------------------- mapa

    /// <summary>
    /// Visor de mapa: carga la textura PNG extraÃ­da del juego (o una variante de piso) y
    /// dibuja a Link y los pines del mapa actual; si no hay textura, usa una rejilla X/Z.
    /// </summary>
    private sealed class MapView : Panel
    {
        private string? _loadedPath;
        private DateTime _loadedStamp;
        private Image? _mapImage;
        private List<MapCalibration> _calibration = new();
        private MapCalibration? _activeCalibration;

        /// <summary>Carga la calibraciÃ³n extraÃ­da del juego (maps\map-calibration.json).</summary>
        public void SetCalibration(List<MapCalibration> calibration)
        {
            _calibration = calibration;
            _loadedPath = null;
        }

        /// <summary>Ruta PNG elegida en el selector de piso (anula la bÃºsqueda automÃ¡tica).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? TextureOverride { get; set; }

        /// <summary>True si Link es adulto (elige texturas *_Adult_00 en vez de *_Child_00).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AdultLink { get; set; }

        /// <summary>Busca la calibraciÃ³n del mapa actual comparando LocationName o TextureName.</summary>
        private MapCalibration? FindCalibration()
        {
            string? current = CurrentMapName;
            if (string.IsNullOrEmpty(current) || _calibration.Count == 0) return null;

            string name = current!;
            return _calibration.FirstOrDefault(c =>
                       string.Equals(c.LocationName, name, StringComparison.OrdinalIgnoreCase)) ??
                   _calibration.FirstOrDefault(c =>
                       c.TextureName.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? SelectedId { get => _selectedId; set { _selectedId = value; } }
        private string? _selectedId;

        /// <summary>Nombre del mapa actual del juego (anula la zona si no es nulo).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? CurrentMapName { get; set; }

        public void ReloadMap()
        {
            _loadedPath = null; // fuerza recarga en el prÃ³ximo pintado
            Invalidate();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public (float X, float Z)? LinkPosition { get; set; }

        /// <summary>Ãngulo de mirada de Link en el plano del mapa (radianes, atan2(fz, fx)).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float LinkAngle { get; set; } [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool IsChild { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<(CollectibleDto Dto, bool Obtenido)> Pins { get; set; } = new();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Zoom { get; set; } = 1.0f;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool FollowLink { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public PointF PanOffset { get; set; } = PointF.Empty;
        public event Action? ViewChanged;
        private Point? _dragStart;
        private PointF _dragOrigin;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float step = e.Delta > 0 ? 0.25f : -0.25f;
            Zoom = Math.Clamp(Zoom + step, 0.75f, 4.5f);
            if (Math.Abs(Zoom - 1.0f) < 0.05f) { Zoom = 1.0f; }
            ViewChanged?.Invoke();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = e.Location;
                _dragOrigin = PanOffset;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragStart is { } start && e.Button == MouseButtons.Left)
            {
                FollowLink = false;
                PanOffset = new PointF(_dragOrigin.X + (e.X - start.X), _dragOrigin.Y + (e.Y - start.Y));
                ViewChanged?.Invoke();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragStart = null;
        }

        public MapView()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        private static DateTime Stamp(string? path)
        {
            if (path is null) return default;
            try { return File.GetLastWriteTimeUtc(path); } catch { return default; }
        }

        private Image? MapFor(string? zone)
        {            _activeCalibration = FindCalibration();
            string key = _activeCalibration is { } cal && cal.TextureName.Length > 0
                ? cal.TextureName
                : CurrentMapName is { Length: > 0 } current ? current : (zone ?? "");

            string? path = TextureOverride;
            if (path is null && key.Length > 0)
            {
                string mapsDir = Path.Combine(AppContext.BaseDirectory, "maps");
                path = GameMapExtractor.FindTextureFile(mapsDir, key, !AdultLink);
                if (path is null)
                {
                    path = GameMapExtractor.FindTextureFile(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "PhykroTrainer", "maps"), key, !AdultLink);
                }
            }

            if (path == _loadedPath && Stamp(path) == _loadedStamp) return _mapImage;

            _mapImage?.Dispose();
            _mapImage = null;
            _loadedPath = path;
            _loadedStamp = Stamp(path);

            if (path is not null)
            {
                try { _mapImage = Image.FromFile(path); }
                catch { _mapImage = null; }
            }

            return _mapImage;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            string zone = Pins.Select(p => p.Dto.Zona).FirstOrDefault(z => z.Length > 0) ?? "";
            Image? image = MapFor(zone);

            var points = new List<(float X, float Z, bool Selected, string Name, string Tipo, bool Obtenido)>();
            foreach (var (dto, obtenido) in Pins)
            {
                if (dto.WorldX == 0 && dto.WorldZ == 0) continue;
                points.Add(((float)dto.WorldX, (float)dto.WorldZ, dto.Id == SelectedId, dto.Nombre, dto.Tipo, obtenido));
            }

            if (points.Count == 0 && LinkPosition is null)
            {
                DrawHint(g, image is null
                    ? "Sin conexiÃ³n o sin datos.\nUsa Â«Guardar X, Z en filaÂ» en el juego para mapear."
                    : "");
                if (image is null) return;
            }

            // Escala: con imagen, el mundo completo [-8000, 8000] se proyecta al Ã¡rea.
            float minX, maxX, minZ, maxZ;
            if (image is not null)
            {
                minX = -8000f; maxX = 8000f; minZ = -8000f; maxZ = 8000f;
            }
            else if (points.Count > 0)
            {
                minX = points.Min(p => p.X) - 500;
                maxX = points.Max(p => p.X) + 500;
                minZ = points.Min(p => p.Z) - 500;
                maxZ = points.Max(p => p.Z) + 500;
                float span = Math.Max(maxX - minX, maxZ - minZ);
                if (span < 1000) { maxX = minX + 1000; maxZ = minZ + 1000; }
            }
            else
            {
                // Sin puntos (p. ej. solo posiciÃ³n de Link): rejilla con el mundo completo.
                minX = -8000f; maxX = 8000f; minZ = -8000f; maxZ = 8000f;
            }

            RectangleF baseArea = ClientRectangle;
            baseArea.Inflate(-24, -24);
            if (image is not null)
            {
                float scale = Math.Min(baseArea.Width / image.Width, baseArea.Height / image.Height);
                baseArea = new RectangleF(
                    baseArea.X + (baseArea.Width - image.Width * scale) / 2f,
                    baseArea.Y + (baseArea.Height - image.Height * scale) / 2f,
                    image.Width * scale, image.Height * scale);
            }

            float orthoScale = _activeCalibration is { CameraHeight: > 0 } c ? baseArea.Height / (c.CameraHeight * 0.72f) : 1f;
            float BaseX(float x) => _activeCalibration is { CameraHeight: > 0 } cal && image is not null
                ? baseArea.Left + baseArea.Width / 2f + (x - cal.CameraX) * orthoScale
                : baseArea.Left + (x - minX) / (maxX - minX) * baseArea.Width;
            float BaseZ(float z) => _activeCalibration is { CameraHeight: > 0 } cal && image is not null
                ? baseArea.Top + baseArea.Height / 2f + (z - cal.CameraZ) * orthoScale
                : baseArea.Top + (z - minZ) / (maxZ - minZ) * baseArea.Height;

            float viewCx = ClientRectangle.Width / 2f;
            float viewCy = ClientRectangle.Height / 2f;
            float pivotX = viewCx;
            float pivotY = viewCy;
            float offsetX = PanOffset.X;
            float offsetY = PanOffset.Y;

            if (FollowLink && LinkPosition is { } lp)
            {
                pivotX = BaseX(lp.X);
                pivotY = BaseZ(lp.Z);
                offsetX = 0f;
                offsetY = 0f;
            }

            float MapX(float x) => viewCx + (BaseX(x) - pivotX) * Zoom + offsetX;
            float MapZ(float z) => viewCy + (BaseZ(z) - pivotY) * Zoom + offsetY;

            RectangleF area = new RectangleF(
                viewCx + (baseArea.Left - pivotX) * Zoom + offsetX,
                viewCy + (baseArea.Top - pivotY) * Zoom + offsetY,
                baseArea.Width * Zoom,
                baseArea.Height * Zoom);

            if (image is not null)
                g.DrawImage(image, area);
            else
                DrawGrid(g, area, minX, maxX, minZ, maxZ);

            Point mouse = PointToClient(Control.MousePosition);
            string? hoverName = null;
            float hoverX = 0, hoverY = 0;

            foreach ((float x, float z, bool selected, string name, string tipo, bool obtenido) in points)
            {
                float cx = MapX(x), cy = MapZ(z);
                DrawPin(g, cx, cy, tipo, selected, obtenido);

                bool isHover = MathF.Sqrt((mouse.X - cx) * (mouse.X - cx) + (mouse.Y - cy) * (mouse.Y - cy)) <= 12f;
                if (isHover && name.Length > 0)
                {
                    hoverName = name;
                    hoverX = cx;
                    hoverY = cy;
                }
                else if (selected && name.Length > 0)
                {
                    DrawBadge(g, name, cx + 11, cy - 9, true);
                }
            }

            if (hoverName is not null)
                DrawBadge(g, hoverName, hoverX + 11, hoverY - 9, false);

            if (LinkPosition is { } linkPos)
            {
                DrawLinkArrow(g, MapX(linkPos.X), MapZ(linkPos.Z), LinkAngle);
            }
        }

        private static void DrawBadge(Graphics g, string text, float x, float y, bool accent)
        {
            SizeF sz = g.MeasureString(text, Theme.Small);
            var rect = new RectangleF(x - 3, y - 2, sz.Width + 6, sz.Height + 4);
            using var bg = new SolidBrush(Color.FromArgb(220, 20, 22, 28));
            using var border = new Pen(accent ? Theme.Gold : Theme.Muted, 1f);
            using var fg = new SolidBrush(accent ? Theme.Text : Theme.Muted);
            g.FillRectangle(bg, rect);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawString(text, Theme.Small, fg, x, y);
        }

        /// <summary>Icono segÃºn el tipo: calavera, corazÃ³n, brote o cofre; Â«âœ“Â» si ya estÃ¡ conseguido.</summary>
        private void DrawPin(Graphics g, float cx, float cy, string tipo, bool selected, bool obtenido)
        {
            float r = selected ? 9f : 6f;

            if (tipo.Contains("Skulltula"))
            {
                // Cráneo dorado estilo ficha de Skulltula: bóveda craneal + mandíbula con dientes + cuencas + nariz.
                float s = r * 1.18f;
                using var skullFill = new SolidBrush(Color.FromArgb(232, 190, 74));
                using var skullRim = new Pen(Color.FromArgb(115, 78, 18), 1.3f);
                using var cavity = new SolidBrush(Color.FromArgb(105, 72, 26));

                // 1. Mandíbula inferior con 3 dientes
                var jaw = new PointF[]
                {
                    new(cx - s * 0.52f, cy + s * 0.25f),
                    new(cx + s * 0.52f, cy + s * 0.25f),
                    new(cx + s * 0.46f, cy + s * 0.92f),
                    new(cx + s * 0.28f, cy + s * 0.72f),
                    new(cx + s * 0.14f, cy + s * 0.95f),
                    new(cx,             cy + s * 0.72f),
                    new(cx - s * 0.14f, cy + s * 0.95f),
                    new(cx - s * 0.28f, cy + s * 0.72f),
                    new(cx - s * 0.46f, cy + s * 0.92f)
                };
                g.FillPolygon(skullFill, jaw);
                g.DrawPolygon(skullRim, jaw);

                // 2. Bóveda superior del cráneo
                var domeRect = new RectangleF(cx - s * 0.95f, cy - s * 0.95f, s * 1.9f, s * 1.55f);
                g.FillEllipse(skullFill, domeRect);
                g.DrawEllipse(skullRim, domeRect);

                // Suavizar unión entre bóveda y mandíbula
                g.FillRectangle(skullFill, cx - s * 0.46f, cy + s * 0.20f, s * 0.92f, s * 0.35f);

                // 3. Cuencas oculares grandes
                g.FillEllipse(cavity, cx - s * 0.68f, cy - s * 0.25f, s * 0.52f, s * 0.55f);
                g.FillEllipse(cavity, cx + s * 0.16f, cy - s * 0.25f, s * 0.52f, s * 0.55f);

                // 4. Cavidad nasal triangular
                var nose = new PointF[]
                {
                    new(cx,             cy + s * 0.18f),
                    new(cx - s * 0.16f, cy + s * 0.44f),
                    new(cx + s * 0.16f, cy + s * 0.44f)
                };
                g.FillPolygon(cavity, nose);
            }
            else if (tipo.Contains("Corazón", StringComparison.OrdinalIgnoreCase) || tipo.Contains("Corazon"))
            {
                // CorazÃ³n: dos cÃ­rculos + triÃ¡ngulo.
                float s = r * 0.9f;
                using (var heart = new SolidBrush(Color.FromArgb(220, 200, 60, 70)))
                {
                    g.FillEllipse(heart, cx - s, cy - s * 0.7f, s, s);
                    g.FillEllipse(heart, cx, cy - s * 0.7f, s, s);
                    var tip = new PointF[] { new(cx - s, cy - s * 0.1f), new(cx + s, cy - s * 0.1f), new(cx, cy + s) };
                    g.FillPolygon(heart, tip);
                }
            }
            else if (tipo.Contains("Frijol"))
            {
                if (!obtenido)
                {
                    // Tierra blanda con agujero oscuro en el centro (sin plantar aún).
                    using (var soil = new SolidBrush(Color.FromArgb(141, 110, 99)))
                        g.FillEllipse(soil, cx - r, cy - r * 0.75f, r * 2, r * 1.5f);
                    using (var hole = new SolidBrush(Color.FromArgb(45, 32, 28)))
                        g.FillEllipse(hole, cx - r * 0.45f, cy - r * 0.35f, r * 0.9f, r * 0.7f);
                }
                else if (IsChild)
                {
                    // Niño con frijol plantado: tierra + brote verde tierno.
                    using (var soil = new SolidBrush(Color.FromArgb(121, 85, 72)))
                        g.FillEllipse(soil, cx - r * 0.85f, cy + r * 0.15f, r * 1.7f, r * 0.75f);
                    using (var stem = new Pen(Color.FromArgb(120, 200, 90), 2f))
                        g.DrawLine(stem, cx, cy + r * 0.4f, cx, cy - r * 0.4f);
                    using (var leaf = new SolidBrush(Color.FromArgb(150, 230, 120)))
                    {
                        g.FillEllipse(leaf, cx - r * 0.95f, cy - r * 0.85f, r * 0.9f, r * 0.55f);
                        g.FillEllipse(leaf, cx + r * 0.05f, cy - r * 0.85f, r * 0.9f, r * 0.55f);
                    }
                }
                else
                {
                    // Adulto con frijol plantado: hoja plataforma grande desarrollada (sin agujero de tierra).
                    using (var pad = new SolidBrush(Color.FromArgb(46, 204, 113)))
                        g.FillEllipse(pad, cx - r * 1.25f, cy - r * 0.95f, r * 2.5f, r * 1.9f);
                    using (var rim = new Pen(Color.FromArgb(163, 247, 191), 1.6f))
                        g.DrawEllipse(rim, cx - r * 1.25f, cy - r * 0.95f, r * 2.5f, r * 1.9f);
                    using (var vein = new Pen(Color.FromArgb(27, 130, 72), 1.4f))
                    {
                        g.DrawLine(vein, cx - r * 0.8f, cy, cx + r * 0.8f, cy);
                        g.DrawLine(vein, cx, cy - r * 0.6f, cx, cy + r * 0.6f);
                    }
                }
            }
            else
            {
                // Cofre dorado/marrÃ³n para el resto.
                using (var chest = new SolidBrush(Theme.Gold))
                    g.FillRectangle(chest, cx - r, cy - r * 0.8f, r * 2, r * 1.6f);
                using (var line = new Pen(Theme.Bg, 1.5f))
                    g.DrawLine(line, cx - r, cy, cx + r, cy);
            }

            if (obtenido)
            {
                // CÃ­rculo oscuro con palomita blanca encima del icono.
                float or = r + 3f;
                using (var dark = new SolidBrush(Color.FromArgb(180, 20, 22, 26)))
                    g.FillEllipse(dark, cx - or, cy - or, or * 2, or * 2);
                using (var check = new Pen(Color.White, 2.2f))
                {
                    g.DrawLine(check, cx - or * 0.5f, cy + or * 0.05f, cx - or * 0.12f, cy + or * 0.45f);
                    g.DrawLine(check, cx - or * 0.12f, cy + or * 0.45f, cx + or * 0.55f, cy - or * 0.45f);
                }
            }

            if (selected)
            {
                using var ring = new Pen(Theme.Text, 2f);
                g.DrawEllipse(ring, cx - r - 3, cy - r - 3, (r + 3) * 2, (r + 3) * 2);
            }
        }

        /// <summary>Flecha roja con borde blanco y cono semitransparente amarillo/blanco apuntando a la vista de Link.</summary>
        private void DrawLinkArrow(Graphics g, float cx, float cy, float angle)
        {
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle * 180f / MathF.PI);

            // Cono de visiÃ³n semitransparente (amarillo/blanco) delante de la flecha.
            var cone = new PointF[]
            {
                new(0, 0),
                new(46f, -24f),
                new(58f, 0),
                new(46f, 24f)
            };
            using (var coneBrush = new SolidBrush(Color.FromArgb(70, 255, 240, 150)))
                g.FillPolygon(coneBrush, cone);
            using (var coneEdge = new Pen(Color.FromArgb(140, 255, 250, 220), 1.5f))
                g.DrawPolygon(coneEdge, cone);

            // Flecha roja con borde blanco.
            var arrow = new PointF[]
            {
                new(14f, 0),
                new(-8f, -8f),
                new(-3f, 0),
                new(-8f, 8f)
            };
            using (var red = new SolidBrush(Color.FromArgb(225, 225, 60, 60)))
                g.FillPolygon(red, arrow);
            using (var border = new Pen(Color.White, 2f))
                g.DrawPolygon(border, arrow);

            g.ResetTransform();
        }

        private void DrawGrid(Graphics g, RectangleF area, float minX, float maxX, float minZ, float maxZ)
        {
            using var pen = new Pen(Theme.Border, 1f);
            using var brush = new SolidBrush(Theme.Muted);
            g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);

            float step = 2000f;
            for (float x = MathF.Ceiling(minX / step) * step; x <= maxX; x += step)
            {
                float sx = area.Left + (x - minX) / (maxX - minX) * area.Width;
                g.DrawLine(pen, sx, area.Top, sx, area.Bottom);
                g.DrawString($"X {x:N0}", Theme.Small, brush, sx + 2, area.Top + 2);
            }

            for (float z = MathF.Ceiling(minZ / step) * step; z <= maxZ; z += step)
            {
                float sy = area.Top + (z - minZ) / (maxZ - minZ) * area.Height;
                g.DrawLine(pen, area.Left, sy, area.Right, sy);
                g.DrawString($"Z {z:N0}", Theme.Small, brush, area.Left + 2, sy + 2);
            }
        }

        private void DrawHint(Graphics g, string text)
        {
            if (text.Length == 0) return;
            using var brush = new SolidBrush(Theme.Muted);
            var layout = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, Theme.Ui, brush, new RectangleF(PointF.Empty, Size), layout);
        }
    }
}





















































