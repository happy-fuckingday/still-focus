using System.Windows;
using StillFocus.Core;

namespace StillFocus.App;

public partial class SettingsWindow : Window
{
    public int Minutes { get; private set; }
    public bool Startup => StartupCheck.IsChecked == true;
    public bool ShowDays => DaysCheck.IsChecked == true;
    public bool Sound => SoundCheck.IsChecked == true;
    public bool Shake => ShakeCheck.IsChecked == true;
    public bool Toast => ToastCheck.IsChecked == true;
    public bool AutoHide => HideCheck.IsChecked == true;
    public bool ResetPosition => ResetCheck.IsChecked == true;
    public double WindowOpacity => OpacitySlider.Value / 100;
    public SettingsWindow(Settings settings, bool startup)
    {
        InitializeComponent();
        NativeMethods.DarkTitle(this);
        MinutesBox.Text = settings.Minutes.ToString();
        StartupCheck.IsChecked = startup; DaysCheck.IsChecked = settings.ShowDays;
        SoundCheck.IsChecked = settings.Sound; ShakeCheck.IsChecked = settings.Shake; ToastCheck.IsChecked = settings.Toast;
        HideCheck.IsChecked = settings.AutoHide; OpacitySlider.Value = settings.Opacity * 100;
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinutesBox.Text, out int minutes) || minutes is < 1 or > 99)
        { ErrorText.Text = "请输入 1–99 的整数分钟。"; MinutesBox.Focus(); return; }
        Minutes = minutes;
        DialogResult = true;
    }
}
