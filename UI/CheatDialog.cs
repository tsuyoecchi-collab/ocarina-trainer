using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

public sealed class CheatDialog : Form
{
    private readonly TextBox _name = Ui.TextBox(width: 360);
    private readonly ComboBox _kind = Ui.Combo(360);
    private readonly TextBox _spec = Ui.TextBox(width: 360);
    private readonly TextBox _value = Ui.TextBox(width: 160);
    private readonly TextBox _max = Ui.TextBox(width: 160);
    private readonly TextBox _maxSpec = Ui.TextBox(width: 360);
    private readonly TextBox _hotkey = Ui.TextBox(width: 160);
    private readonly TextBox _note = Ui.TextBox(multiline: true, width: 360);
    private readonly CheckBox _enabled = Ui.Check("Activo", true);
    private readonly CheckBox _frozen = Ui.Check("Congelar al conectar");
    private readonly CheckBox _limit = Ui.Check("Limitar al máximo (Sí)", true);
    private readonly Label _testResult = Ui.Hint("");
    private readonly MainForm _host;

    public CheatTarget Result { get; }

    public CheatDialog(MainForm host, CheatTarget? source, string title, string? presetName = null,
        ValueKind? presetKind = null, string? presetValue = null)
    {
        _host = host;

        Result = new CheatTarget();
        if (source is not null)
        {
            Result.Id = source.Id;
            Result.Name = source.Name;
            Result.Kind = source.Kind;
            Result.Spec = source.Spec;
            Result.Value = source.Value;
            Result.Max = source.Max;
            Result.MaxSpec = source.MaxSpec;
            Result.Hotkey = source.Hotkey;
            Result.Enabled = source.Enabled;
            Result.Frozen = source.Frozen;
            Result.LimitToMax = source.LimitToMax;
            Result.Note = source.Note;
        }
        else if (presetName is not null)
        {
            Result.Name = presetName;
            Result.Kind = presetKind ?? ValueKind.Int32;
            Result.Value = presetValue ?? "";
            Result.Enabled = true;
        }

        Text = title;
        Icon = host.Icon;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 600);

        BuildUi();
        LoadValues();
        Ui.DarkTitleBar(this);
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 13,
            Padding = new Padding(18, 14, 18, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int row = 0; row < 12; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(Ui.Label("Nombre"), 0, 0);
        layout.Controls.Add(_name, 1, 0);

        layout.Controls.Add(Ui.Label("Tipo"), 0, 1);
        layout.Controls.Add(_kind, 1, 1);

        layout.Controls.Add(Ui.Label("Dirección"), 0, 2);
        layout.Controls.Add(_spec, 1, 2);

        var help = new Label
        {
            Text = "Formatos:  0x7FF7A2B01234   ·   ocarina.exe+0x1A2B3C   ·   " +
                   "ocarina.exe+0x1A2B3C,0x10,0x8  (cadena de punteros, offsets en hexadecimal)",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            BackColor = Color.Transparent
        };
        layout.Controls.Add(new Label(), 0, 3);
        layout.Controls.Add(help, 1, 3);

        var testRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 4)
        };
        var testButton = Ui.Button("Probar dirección");
        testButton.Click += (_, _) => TestAddress();
        testRow.Controls.Add(testButton);
        _testResult.Margin = new Padding(10, 8, 0, 0);
        testRow.Controls.Add(_testResult);
        layout.Controls.Add(new Label(), 0, 4);
        layout.Controls.Add(testRow, 1, 4);

        layout.Controls.Add(Ui.Label("Valor"), 0, 5);
        layout.Controls.Add(_value, 1, 5);

        var maxRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2)
        };
        maxRow.Controls.Add(_max);
        maxRow.Controls.Add(Ui.Hint("Vacío = sin límite. No se escribirá nada por encima."));
        layout.Controls.Add(Ui.Label("Máximo"), 0, 6);
        layout.Controls.Add(maxRow, 1, 6);

        var maxSpecRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2)
        };
        maxSpecRow.Controls.Add(_maxSpec);
        maxSpecRow.Controls.Add(Ui.Hint("Opcional: dirección con la capacidad del juego. El máximo se actualiza solo."));
        layout.Controls.Add(Ui.Label("Dir. máximo"), 0, 7);
        layout.Controls.Add(maxSpecRow, 1, 7);

        var hotkeyRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2)
        };
        hotkeyRow.Controls.Add(_hotkey);
        var capture = Ui.Button("Capturar tecla");
        capture.Click += (_, _) =>
        {
            _hotkey.Text = "";
            _hotkey.Focus();
        };
        hotkeyRow.Controls.Add(capture);
        hotkeyRow.Controls.Add(Ui.Hint("Clic y pulsa la tecla."));
        layout.Controls.Add(Ui.Label("Atajo"), 0, 8);
        layout.Controls.Add(hotkeyRow, 1, 8);

        var checks = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 2)
        };
        checks.Controls.Add(_enabled);
        checks.Controls.Add(_frozen);
        checks.Controls.Add(_limit);
        layout.Controls.Add(new Label(), 0, 9);
        layout.Controls.Add(checks, 1, 9);

        layout.Controls.Add(Ui.Label("Nota"), 0, 10);
        _note.Height = 90;
        _note.ScrollBars = ScrollBars.Vertical;
        _note.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        layout.Controls.Add(_note, 1, 10);

        var helpValue = Ui.Hint("Ejemplos: vida 20, rupias 999, munición 30. " +
                                "El valor se escribe cada pocos milisegundos mientras «Congelar» esté marcado.");
        layout.Controls.Add(new Label(), 0, 11);
        layout.Controls.Add(helpValue, 1, 11);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Theme.Bg,
            Padding = new Padding(18, 6, 18, 8)
        };
        var ok = Ui.Button("Guardar truco", primary: true);
        var cancel = Ui.Button("Cancelar");
        ok.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        _hotkey.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            uint modifiers = 0;
            if ((e.Modifiers & Keys.Control) != 0) modifiers |= Native.ModControl;
            if ((e.Modifiers & Keys.Alt) != 0) modifiers |= Native.ModAlt;
            if ((e.Modifiers & Keys.Shift) != 0) modifiers |= Native.ModShift;

            _hotkey.Text = HotkeyManager.Format(new HotkeySpec(e.KeyCode, modifiers));
            e.SuppressKeyPress = true;
        };

        _kind.Items.Clear();
        foreach (ValueKind kind in Enum.GetValues<ValueKind>())
            _kind.Items.Add(new KindItem(kind, ValueCodec.LongLabel(kind)));
    }

    private void LoadValues()
    {
        _name.Text = Result.Name;
        _spec.Text = Result.Spec;
        _value.Text = Result.Value;
        _max.Text = Result.Max;
        _maxSpec.Text = Result.MaxSpec;
        _hotkey.Text = Result.Hotkey;
        _note.Text = Result.Note;
        _enabled.Checked = Result.Enabled;
        _frozen.Checked = Result.Frozen;
        _limit.Checked = Result.LimitToMax;

        for (int i = 0; i < _kind.Items.Count; i++)
        {
            if (_kind.Items[i] is KindItem item && item.Kind == Result.Kind)
            {
                _kind.SelectedIndex = i;
                break;
            }
        }

        if (_kind.SelectedIndex < 0) _kind.SelectedIndex = 0;
    }

    private ValueKind SelectedKind => _kind.SelectedItem is KindItem item ? item.Kind : ValueKind.Int32;

    private void TestAddress()
    {
        (bool ok, string text) = _host.TestAddress(_spec.Text, SelectedKind);
        _testResult.Text = text;
        _testResult.ForeColor = ok ? Theme.Accent : Theme.Danger;

        if (ok && _value.Text.Trim().Length == 0)
        {
            int arrow = text.LastIndexOf('→');
            if (arrow >= 0) _value.Text = text[(arrow + 1)..].Trim();
        }
    }

    private void Save()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Ponle un nombre al truco.", "Phykro Trainer",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _name.Focus();
            return;
        }

        string spec = _spec.Text.Trim();
        if (spec.Length > 0 && !AddressSpec.TryParse(spec, out _, out string specError))
        {
            MessageBox.Show(this, specError, "Dirección no válida", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _spec.Focus();
            return;
        }

        string value = _value.Text.Trim();
        if (value.Length > 0 && !ValueCodec.TryParse(SelectedKind, value, out _, out string valueError))
        {
            MessageBox.Show(this, valueError, "Valor no válido", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _value.Focus();
            return;
        }

        string hotkey = _hotkey.Text.Trim();
        if (hotkey.Length > 0 && !HotkeyManager.TryParse(hotkey, out _, out string hotkeyError))
        {
            MessageBox.Show(this, hotkeyError, "Atajo no válido", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _hotkey.Focus();
            return;
        }

        string max = _max.Text.Trim();
        if (max.Length > 0 && !ValueCodec.TryParse(SelectedKind, max, out _, out string maxError))
        {
            MessageBox.Show(this, maxError, "Máximo no válido", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _max.Focus();
            return;
        }

        string maxSpec = _maxSpec.Text.Trim();
        if (maxSpec.Length > 0 && !AddressSpec.TryParse(maxSpec, out _, out string maxSpecError))
        {
            MessageBox.Show(this, maxSpecError, "Dirección del máximo no válida", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _maxSpec.Focus();
            return;
        }

        Result.Name = name;
        Result.Kind = SelectedKind;
        Result.Spec = spec;
        Result.Value = value;
        Result.Max = max;
        Result.MaxSpec = maxSpec;
        Result.Hotkey = hotkey;
        Result.Note = _note.Text.Trim();
        Result.Enabled = _enabled.Checked;
        Result.Frozen = _frozen.Checked;
        Result.LimitToMax = _limit.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record KindItem(ValueKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}
