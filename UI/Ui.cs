using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

/// <summary>Fábrica de controles con el estilo oscuro de la app.</summary>
internal static class Ui
{
    public static Label Label(string text, bool muted = false, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = muted ? Theme.Muted : Theme.Text,
        Font = bold ? Theme.UiBold : Theme.Ui,
        BackColor = Color.Transparent
    };

    public static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Accent,
        Font = Theme.Section,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 6, 0, 6)
    };

    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(920, 0),
        ForeColor = Theme.Muted,
        Font = Theme.Small,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 2, 0, 6)
    };

    public static Button Button(string text, bool primary = false, int width = 0) => new()
    {
        Text = text,
        AutoSize = width <= 0,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(width, 30),
        Padding = new Padding(10, 4, 10, 4),
        FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Theme.Accent : Theme.CardAlt,
        ForeColor = primary ? Color.FromArgb(12, 32, 20) : Theme.Text,
        Font = primary ? Theme.UiBold : Theme.Ui,
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
        FlatAppearance = { BorderSize = 1, BorderColor = primary ? Theme.Accent : Theme.Border }
    };

    public static TextBox TextBox(string text = "", bool multiline = false, int width = 0) => new()
    {
        Text = text,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.CardAlt,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Multiline = multiline,
        Width = width > 0 ? width : 200,
        AutoSize = !multiline
    };

    public static ComboBox Combo(int width = 200) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.CardAlt,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Width = width
    };

    public static CheckBox Check(string text, bool value = false) => new FlatCheckBox
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Margin = new Padding(0, 5, 14, 5)
    };

    public static NumericUpDown Number(int min, int max, int value, int width = 90) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = width,
        BackColor = Theme.CardAlt,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        BorderStyle = BorderStyle.FixedSingle
    };

    public static FlowLayoutPanel Toolbar() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        BackColor = Color.Transparent,
        Padding = new Padding(0, 0, 0, 6)
    };

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Theme.Card,
            GridColor = Theme.Border,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            AutoGenerateColumns = false,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 31 },
            Font = Theme.Ui
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(54, 40, 22);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Gold;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiBold;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(54, 40, 22);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Gold;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

        grid.DefaultCellStyle.BackColor = Theme.Card;
        grid.DefaultCellStyle.ForeColor = Theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = Theme.Selection;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(52, 39, 22);
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.Text;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.Selection;

        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (grid.Columns[e.ColumnIndex] is not DataGridViewCheckBoxColumn) return;

            e.PaintBackground(e.ClipBounds, true);
            bool isChecked = e.Value is bool b && b;
            int sz = 20;
            int bx = e.CellBounds.X + (e.CellBounds.Width - sz) / 2;
            int by = e.CellBounds.Y + (e.CellBounds.Height - sz) / 2;
            var box = new Rectangle(bx, by, sz, sz);

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var stoneFill = new SolidBrush(isChecked ? Color.FromArgb(58, 48, 30) : Color.FromArgb(34, 32, 30)))
                e.Graphics.FillRectangle(stoneFill, box);
            using (var outerGold = new Pen(isChecked ? Color.FromArgb(245, 206, 96) : Color.FromArgb(156, 128, 74), 1.6f))
                e.Graphics.DrawRectangle(outerGold, box);
            using (var innerInset = new Pen(Color.FromArgb(22, 18, 14), 1f))
                e.Graphics.DrawRectangle(innerInset, bx + 2, by + 2, sz - 4, sz - 4);

            if (isChecked)
            {
                using var checkPen = new Pen(Color.FromArgb(255, 224, 118), 2.6f)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                };
                e.Graphics.DrawLines(checkPen, new[]
                {
                    new PointF(bx + 4.5f, by + sz * 0.54f),
                    new PointF(bx + sz * 0.42f, by + sz - 4.8f),
                    new PointF(bx + sz - 4.2f, by + 4.5f)
                });
            }

            e.Handled = true;
        };

        grid.CellMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (grid.Columns[e.ColumnIndex] is not DataGridViewCheckBoxColumn) return;
            var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            bool current = cell.Value is bool b && b;
            cell.Value = !current;
            grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
        };

        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || grid.CurrentCell is null) return;
            int col = grid.CurrentCell.ColumnIndex;
            int row = grid.CurrentCell.RowIndex;
            if (row < 0 || col < 0 || grid.Columns[col] is not DataGridViewCheckBoxColumn) return;
            bool current = grid.CurrentCell.Value is bool b && b;
            grid.CurrentCell.Value = !current;
            grid.InvalidateCell(col, row);
            e.Handled = true;
        };

        return grid;
    }

    public static DataGridViewCheckBoxColumn CheckColumn(string header, string property, int width) => new()
    {
        HeaderText = header,
        DataPropertyName = property,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        ReadOnly = true,
        FlatStyle = FlatStyle.Flat,
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.CardAlt,
            SelectionBackColor = Theme.Selection,
            ForeColor = Theme.Accent,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        }
    };

    public static DataGridViewTextBoxColumn TextColumn(string header, string property, int width,
        bool readOnly = true, string? format = null) => new()
    {
        HeaderText = header,
        DataPropertyName = property,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        ReadOnly = readOnly,
        DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = format == "mono" ? Theme.Mono : Theme.Ui,
            ForeColor = Theme.Text
        }
    };

    public static DataGridViewTextBoxColumn FillColumn(string header, string property) => new()
    {
        HeaderText = header,
        DataPropertyName = property,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        ReadOnly = true
    };

    /// <summary>Contenedor vertical para apilar secciones con margen.</summary>
    public static TableLayoutPanel Stack()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 0,
            AutoScroll = true,
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return panel;
    }

    public static void AddBlock(TableLayoutPanel stack, Control block, bool fill = false)
    {
        int row = stack.RowStyles.Count;
        stack.RowCount = row + 1;
        stack.RowStyles.Add(fill
            ? new RowStyle(SizeType.Percent, 100f)
            : new RowStyle(SizeType.AutoSize));

        block.Dock = fill ? DockStyle.Fill : DockStyle.Top;
        block.Margin = new Padding(0, 0, 0, 10);
        stack.Controls.Add(block, 0, row);
    }

    public static Panel Spacer(int height = 10) => new()
    {
        Height = height,
        Dock = DockStyle.Top,
        BackColor = Color.Transparent
    };

    public static void DarkTitleBar(Form form)
    {
        void Apply()
        {
            try
            {
                int dark = 1;
                Native.DwmSetWindowAttribute(form.Handle, Native.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch
            {
                // no es crítico
            }
        }

        if (form.IsHandleCreated) Apply();
        else form.HandleCreated += (_, _) => Apply();
    }

    public static void StyleTree(Control control)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = Theme.Bg;
                form.ForeColor = Theme.Text;
                form.Font = Theme.Ui;
                break;
            case DataGridView grid:
                grid.BackgroundColor = Theme.Card;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                break;
        }

        foreach (Control child in control.Controls) StyleTree(child);
    }
}

/// <summary>
/// CheckBox dibujado a mano para el tema oscuro: la casilla y la marca usan colores
/// con contraste (relleno verde acento al marcar), porque el control nativo se pierde
/// sobre fondo oscuro y el usuario no distinguía si estaba marcado.
/// </summary>
internal sealed class FlatCheckBox : CheckBox
{
    private const int BoxSize = 19;

    public FlatCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font);
        return new Size(BoxSize + 7 + text.Width, Math.Max(24, text.Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(EffectiveBackColor());

        int top = Math.Max(0, (Height - BoxSize) / 2);
        var box = new Rectangle(0, top, BoxSize, BoxSize);

        using (var fill = new SolidBrush(Checked ? Color.FromArgb(62, 50, 30) : Color.FromArgb(34, 32, 30)))
            g.FillRectangle(fill, box);
        using (var border = new Pen(Checked ? Color.FromArgb(245, 206, 96) : Color.FromArgb(156, 128, 74), 1.6f))
            g.DrawRectangle(border, box);

        if (Checked)
        {
            using var mark = new Pen(Color.FromArgb(255, 224, 118), 2.5f);
            g.DrawLines(mark, new[]
            {
                new PointF(3f, top + BoxSize / 2f),
                new PointF(BoxSize / 2f - 1f, top + BoxSize - 4f),
                new PointF(BoxSize - 3f, top + 3f)
            });
        }

        var textRect = new Rectangle(BoxSize + 7, 0, Width - BoxSize - 7, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private Color EffectiveBackColor()
    {
        Control? c = this;
        while (c is not null)
        {
            if (c.BackColor != Color.Transparent) return c.BackColor;
            c = c.Parent;
        }
        return Theme.Bg;
    }
}


