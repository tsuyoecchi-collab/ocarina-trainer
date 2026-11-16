using PhykroTrainer.Core;

namespace PhykroTrainer.UI;

internal static class Prompt
{
    public static string? Show(IWin32Window owner, string title, string label, string value = "")
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Font = Theme.Ui,
            ClientSize = new Size(420, 150)
        };

        TableLayoutPanel layout = Ui.Stack();
        layout.Padding = new Padding(16);
        layout.Dock = DockStyle.Fill;
        layout.AutoScroll = false;
        if (owner is Form ownerForm) form.Icon = ownerForm.Icon;

        var text = Ui.TextBox(value, width: 380);
        text.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 42,
            BackColor = Color.Transparent
        };
        var ok = Ui.Button("Aceptar", primary: true);
        var cancel = Ui.Button("Cancelar");
        ok.Click += (_, _) => { form.DialogResult = DialogResult.OK; form.Close(); };
        cancel.Click += (_, _) => { form.DialogResult = DialogResult.Cancel; form.Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        layout.Controls.Clear();
        Ui.AddBlock(layout, Ui.Label(label));
        Ui.AddBlock(layout, text);
        form.Controls.Add(layout);
        form.Controls.Add(buttons);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { text.Focus(); text.SelectAll(); };
        Ui.DarkTitleBar(form);

        return form.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
    }
}
