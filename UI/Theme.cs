namespace PhykroTrainer.UI;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(44, 34, 21);
    public static readonly Color Sidebar = Color.FromArgb(56, 42, 24);
    public static readonly Color Card = Color.FromArgb(66, 50, 29);
    public static readonly Color CardAlt = Color.FromArgb(104, 78, 40);
    public static readonly Color Border = Color.FromArgb(132, 102, 56);
    public static readonly Color Text = Color.FromArgb(246, 236, 212);
    public static readonly Color Muted = Color.FromArgb(196, 176, 138);
    public static readonly Color Accent = Color.FromArgb(245, 206, 96);
    public static readonly Color AccentDark = Color.FromArgb(118, 88, 42);
    public static readonly Color Danger = Color.FromArgb(235, 110, 96);
    public static readonly Color Gold = Color.FromArgb(248, 212, 104);
    public static readonly Color Selection = Color.FromArgb(112, 84, 42);

    public static readonly Font Ui = new("Segoe UI", 9.5f);
    public static readonly Font UiBold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Title = new("Georgia", 14.5f, FontStyle.Bold);
    public static readonly Font Section = new("Georgia", 11f, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 9f);
}
