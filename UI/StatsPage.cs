using System.ComponentModel;
using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

/// <summary>
/// Página «Estadísticas»: muestra en una tabla los contadores y capacidades de progreso del
/// módulo de juego activo. Es solo lectura: nunca se escribe ni se congela memoria.
/// </summary>
public sealed class StatsPage : TrainerPage
{
    private sealed record Row(GameStat Stat)
    {
        public string Name { get; set; } = Stat.Name;
        public string Value { get; set; } = "—";
        public string PreviousValue { get; set; } = "—";
        public string Max { get; set; } = Stat.Max > 0 ? Stat.Max.ToString() : "";
        public string Note { get; set; } = Stat.Note;
        public DateTime? Updated { get; set; }
        public string UpdatedDisplay => Updated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    }

    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _count = Ui.Hint("");
    private DataGridViewColumn? _prevCol;
    private BindingList<Row> _rows = new();

    public StatsPage(MainForm host) : base(host)
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
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        header.Controls.Add(Ui.Section("ESTADÍSTICAS"));
        _count.Margin = new Padding(16, 10, 0, 0);
        header.Controls.Add(_count);

        var prevCheck = Ui.Check("Mostrar valor anterior", true);
        prevCheck.BackColor = Color.Transparent;
        prevCheck.ForeColor = Theme.Muted;
        prevCheck.Font = Theme.Small;
        prevCheck.Margin = new Padding(16, 4, 0, 0);
        prevCheck.CheckedChanged += (_, _) => { if (_prevCol != null) _prevCol.Visible = prevCheck.Checked; };
        header.Controls.Add(prevCheck);

        var hint = Ui.Hint("Contadores y capacidades de la partida, solo lectura. El juego los " +
                           "recalcula desde sus propios datos: no se pueden forzar escribiendo memoria. " +
                           "Se actualizan solos mientras está conectado.");

        var top = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top
        };
        for (int i = 0; i < 2; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(hint, 0, 0);

        _grid.Columns.Clear();
        _grid.Columns.Add(Ui.TextColumn("Dato", nameof(Row.Name), 260, format: "notmono"));
        _grid.Columns.Add(Ui.TextColumn("Valor", nameof(Row.Value), 90));
        _prevCol = Ui.TextColumn("Valor anterior", nameof(Row.PreviousValue), 90);
        _grid.Columns.Add(_prevCol);
        _grid.Columns.Add(Ui.TextColumn("Máximo", nameof(Row.Max), 80));
        _grid.Columns.Add(Ui.FillColumn("Detalle", nameof(Row.Note)));
        _grid.Columns.Add(Ui.TextColumn("Actualizado", nameof(Row.UpdatedDisplay), 150));
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.DataSource = _rows;

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(top, 0, 1);

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0) };
        gridHost.Controls.Add(_grid);
        root.Controls.Add(gridHost, 0, 2);

        Controls.Add(root);
    }

    public override void OnProfileChanged()
    {
        _rows = new BindingList<Row>(Host.GameModule.Stats.Select(s => new Row(s)).ToList());
        _grid.DataSource = _rows;
        _grid.Visible = _rows.Count > 0;
        _count.Text = _rows.Count > 0
            ? $"{_rows.Count} estadística(s)"
            : "Sin estadísticas conocidas para este juego.";
    }

    public override void OnTick()
    {
        if (!Host.Engine.IsConnected || _rows.Count == 0) return;
        GameMemory? memory = Host.Memory;
        if (memory is not { IsAlive: true }) return;

        foreach (Row row in _rows)
        {
            string text = "—";
            if (AddressSpec.TryParse(row.Stat.Spec, out AddressSpec spec, out _) &&
                spec.TryResolve(memory, out long address, out _))
            {
                byte[] buffer = new byte[ValueCodec.Size(row.Stat.Kind)];
                if (memory.TryRead(address, buffer))
                {
                    string formatted = ValueCodec.Format(row.Stat.Kind, buffer);
                    if (IsPlausible(row.Stat, formatted)) text = formatted;
                }
            }

            string oldValue = row.Value;
            if (text != "—" && oldValue != text) row.PreviousValue = oldValue;
            if (row.Value != text) row.Value = text;
            if (text != "—" && oldValue != text) row.Updated = DateTime.Now;

            // Leer Máximo desde MaxSpec (estilo TrainerEngine.SyncMax).
            // IsPlausible usa el Max fijo; esto solo afecta a la columna "Máximo" de la UI.
            string maxText = "";
            if (row.Stat.MaxSpec.Length > 0 &&
                AddressSpec.TryParse(row.Stat.MaxSpec, out AddressSpec maxSpec, out _) &&
                maxSpec.TryResolve(memory, out long maxAddress, out _))
            {
                byte[] maxBuffer = new byte[ValueCodec.Size(row.Stat.Kind)];
                if (memory.TryRead(maxAddress, maxBuffer))
                {
                    maxText = ValueCodec.Format(row.Stat.Kind, maxBuffer);
                }
            }

            string displayMax = maxText.Length > 0
                ? maxText
                : (row.Stat.Max > 0 ? row.Stat.Max.ToString() : "");

            if (row.Max != displayMax) row.Max = displayMax;
        }

        // Re-ordenar por Updated descendente (nulls al final) solo si cambió el orden.
        var sorted = _rows.OrderByDescending(r => r.Updated).ThenBy(r => _rows.IndexOf(r)).ToList();
        if (!sorted.SequenceEqual(_rows))
        {
            _rows = new BindingList<Row>(sorted);
            _grid.DataSource = _rows;
        }
    }

    /// <summary>Descarta lecturas que no parezcan reales (negativas o por encima del máximo).</summary>
    private static bool IsPlausible(GameStat stat, string text)
    {
        if (stat.Kind is not (ValueKind.Byte or ValueKind.Int16 or ValueKind.Int32)) return true;
        if (!long.TryParse(text, out long number)) return false;
        return number >= 0 && (stat.Max <= 0 || number <= stat.Max);
    }
}
