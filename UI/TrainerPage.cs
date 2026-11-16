namespace PhykroTrainer.UI;

/// <summary>Página del panel derecho. Se avisa al cambiar de perfil y en cada tick.</summary>
public abstract class TrainerPage : UserControl
{
    protected TrainerPage(MainForm host)
    {
        Host = host;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        Font = Theme.Ui;
    }

    protected MainForm Host { get; }

    public virtual void OnProfileChanged()
    {
    }

    public virtual void OnTick()
    {
    }
}
