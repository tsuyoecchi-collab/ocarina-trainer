using System.ComponentModel;
using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

public sealed class WaypointsPage : TrainerPage
{
    private readonly TextBox _x = Ui.TextBox(width: 215);
    private readonly TextBox _y = Ui.TextBox(width: 215);
    private readonly TextBox _z = Ui.TextBox(width: 215);
    private readonly Label _coordsStatus = Ui.Hint("");
    private readonly Label _current = Ui.Hint("Posición actual: —");
    private readonly TextBox _name = Ui.TextBox(width: 210);
    private readonly DataGridView _grid = Ui.Grid();
    private BindingList<Waypoint> _items = new();

    public WaypointsPage(MainForm host) : base(host)
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        header.Controls.Add(Ui.Section("WAYPOINTS"));

        var hint = Ui.Hint("El teletransporte escribe tres floats (X, Y, Z). " +
                           "Busca esas coordenadas con el Escáner (tipo Float): muévete un poco y usa «Cambió». " +
                           "Suele haber tres valores juntos en memoria. Después pega las direcciones aquí.");

        var coords = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 6,
            RowCount = 2,
            BackColor = Theme.Card,
            Padding = new Padding(10, 8, 10, 8),
            Dock = DockStyle.Top
        };
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        coords.Controls.Add(Ui.Label("X:", muted: true), 0, 0);
        coords.Controls.Add(_x, 1, 0);
        coords.Controls.Add(Ui.Label("Y:", muted: true), 2, 0);
        coords.Controls.Add(_y, 3, 0);
        coords.Controls.Add(Ui.Label("Z:", muted: true), 4, 0);
        coords.Controls.Add(_z, 5, 0);

        var saveCoords = Ui.Button("Guardar direcciones", primary: true);
        saveCoords.Click += (_, _) => SaveCoordinates();
        var readNow = Ui.Button("Leer posición");
        readNow.Click += (_, _) => RefreshPosition(forceMessage: true);

        coords.Controls.Add(saveCoords, 1, 1);
        coords.Controls.Add(readNow, 3, 1);
        _coordsStatus.Margin = new Padding(8, 8, 0, 0);
        coords.Controls.Add(_coordsStatus, 5, 1);

        var position = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 6)
        };
        position.Controls.Add(_current);

        var waypointBar = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Card,
            Padding = new Padding(10, 8, 10, 8)
        };
        waypointBar.Controls.Add(Ui.Label("Nombre:", muted: true));
        waypointBar.Controls.Add(_name);
        var save = Ui.Button("Guardar posición (F9)", primary: true);
        save.Click += (_, _) => SaveQuick();
        waypointBar.Controls.Add(save);

        BuildGrid();

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };
        var teleport = Ui.Button("Teletransportar (F10)", primary: true);
        teleport.Click += (_, _) => TeleportSelected();
        var rename = Ui.Button("Renombrar");
        rename.Click += (_, _) => RenameSelected();
        var remove = Ui.Button("Eliminar");
        remove.Click += (_, _) => RemoveSelected();
        footer.Controls.Add(teleport);
        footer.Controls.Add(rename);
        footer.Controls.Add(remove);
        footer.Controls.Add(Ui.Hint("Doble clic en un waypoint para teletransportarte."));

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        gridHost.Controls.Add(_grid);

        root.Controls.Add(header, 0, 0);
        var top = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top
        };
        for (int i = 0; i < 4; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(hint, 0, 0);
        top.Controls.Add(coords, 0, 1);
        top.Controls.Add(position, 0, 2);
        top.Controls.Add(waypointBar, 0, 3);
        root.Controls.Add(top, 0, 1);
        root.Controls.Add(gridHost, 0, 3);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);
    }

    private void BuildGrid()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Nombre",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(Ui.TextColumn("X", nameof(Waypoint.X), 120));
        _grid.Columns.Add(Ui.TextColumn("Y", nameof(Waypoint.Y), 120));
        _grid.Columns.Add(Ui.TextColumn("Z", nameof(Waypoint.Z), 120));
        _grid.Columns.Add(Ui.TextColumn("Creado", nameof(Waypoint.CreatedText), 130));

        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) TeleportSelected();
        };
    }

    public override void OnProfileChanged()
    {
        _x.Text = Host.Profile.CoordX;
        _y.Text = Host.Profile.CoordY;
        _z.Text = Host.Profile.CoordZ;

        _items = new BindingList<Waypoint>(Host.Profile.Waypoints);
        _grid.DataSource = _items;
        _name.Text = $"Waypoint {Host.Profile.Waypoints.Count + 1}";
        _coordsStatus.Text = "";
        _current.Text = "Posición actual: —";
    }

    public override void OnTick()
    {
        if (!Host.Engine.IsConnected) return;
        if (string.IsNullOrWhiteSpace(Host.Profile.CoordX) ||
            string.IsNullOrWhiteSpace(Host.Profile.CoordY) ||
            string.IsNullOrWhiteSpace(Host.Profile.CoordZ))
        {
            return;
        }

        RefreshPosition(forceMessage: false);
    }

    private void SaveCoordinates()
    {
        TrainerProfile profile = Host.Profile;
        foreach ((TextBox box, string label) in new[] { (_x, "X"), (_y, "Y"), (_z, "Z") })
        {
            string text = box.Text.Trim();
            if (text.Length == 0) continue;
            if (!AddressSpec.TryParse(text, out _, out string error))
            {
                _coordsStatus.Text = $"{label}: {error}";
                _coordsStatus.ForeColor = Theme.Danger;
                return;
            }
        }

        profile.CoordX = _x.Text.Trim();
        profile.CoordY = _y.Text.Trim();
        profile.CoordZ = _z.Text.Trim();
        Host.SaveConfig();

        _coordsStatus.Text = "Direcciones guardadas.";
        _coordsStatus.ForeColor = Theme.Accent;
        Host.SetStatus("Direcciones de coordenadas guardadas.");
        RefreshPosition(forceMessage: true);
    }

    private void RefreshPosition(bool forceMessage)
    {
        if (!Host.Engine.TryReadPosition(out float x, out float y, out float z, out string error))
        {
            if (forceMessage)
            {
                _current.Text = "Posición actual: —";
                _coordsStatus.Text = error;
                _coordsStatus.ForeColor = Theme.Danger;
                Host.SetStatus(error, true);
            }

            return;
        }

        _current.Text = $"Posición actual:  X = {x:0.###}    Y = {y:0.###}    Z = {z:0.###}";
        if (forceMessage) Host.SetStatus("Coordenadas leídas.");
    }

    public void SaveQuick()
    {
        if (!Host.Engine.TryReadPosition(out float x, out float y, out float z, out string error))
        {
            Host.SetStatus(error, true);
            return;
        }

        string name = _name.Text.Trim();
        if (name.Length == 0) name = $"Waypoint {Host.Profile.Waypoints.Count + 1}";

        var waypoint = new Waypoint { Name = name, X = x, Y = y, Z = z, Created = DateTime.Now };
        Host.Profile.Waypoints.Add(waypoint);
        _items.ResetBindings();
        _name.Text = $"Waypoint {Host.Profile.Waypoints.Count + 1}";
        Host.SaveConfig();

        if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[^1].Cells[0];
        Host.Notify($"Guardado «{name}» en X={x:0.##} Y={y:0.##} Z={z:0.##}");
    }

    public void TeleportSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Waypoint waypoint)
        {
            Host.SetStatus("Selecciona un waypoint de la lista.", true);
            return;
        }

        if (!Host.Engine.TryWritePosition(waypoint.X, waypoint.Y, waypoint.Z, out string error))
        {
            Host.SetStatus(error, true);
            return;
        }

        Host.Notify($"Teletransportado a «{waypoint.Name}».");
        RefreshPosition(forceMessage: false);
    }

    private void RenameSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Waypoint waypoint) return;

        string? name = Prompt.Show(FindForm()!, "Renombrar waypoint", "Nuevo nombre:", waypoint.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        waypoint.Name = name.Trim();
        _items.ResetBindings();
        Host.SaveConfig();
    }

    private void RemoveSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Waypoint waypoint) return;

        if (MessageBox.Show(FindForm(), $"¿Eliminar «{waypoint.Name}»?", "Phykro Trainer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Host.Profile.Waypoints.Remove(waypoint);
        _items.ResetBindings();
        Host.SaveConfig();
    }
}
