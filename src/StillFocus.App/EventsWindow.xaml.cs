using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StillFocus.Core;

namespace StillFocus.App;

public partial class EventsWindow : Window
{
    private readonly ObservableCollection<DayEvent> items;
    private DayEvent? editing;
    private bool switching;
    public List<DayEvent> Events => items.ToList();
    public Guid? Selected { get; private set; }
    public EventsWindow(Settings settings)
    {
        InitializeComponent();
        NativeMethods.DarkTitle(this);
        items = new(settings.Events.Select(x => new DayEvent { Id = x.Id, Name = x.Name, Date = x.Date, Enabled = x.Enabled }));
        Selected = settings.Current?.Id;
        EventList.ItemsSource = items;
        if (items.Count > 0) EventList.SelectedItem = items.FirstOrDefault(x => x.Id == Selected) ?? items[0];
        else ClearEditor();
    }
    private void ClearEditor()
    {
        editing = null; NameBox.Text = ""; DateBox.SelectedDate = DateTime.Today;
        EnabledCheck.IsChecked = true; SelectedCheck.IsChecked = false;
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switching) return;
        var next = EventList.SelectedItem as DayEvent;
        if (editing != null && !Apply())
        {
            switching = true; EventList.SelectedItem = editing; switching = false; return;
        }
        editing = next;
        if (editing == null) { ClearEditor(); return; }
        NameBox.Text = editing.Name; DateBox.SelectedDate = editing.Date.ToDateTime(TimeOnly.MinValue);
        EnabledCheck.IsChecked = editing.Enabled; SelectedCheck.IsChecked = editing.Id == Selected;
        ErrorText.Text = "";
    }
    private bool Apply()
    {
        if (editing == null) return true;
        if (string.IsNullOrWhiteSpace(NameBox.Text) || DateBox.SelectedDate == null ||
            !DateTime.TryParse(DateBox.Text, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var parsedDate))
        { ErrorText.Text = "请填写事件名称和有效日期。"; return false; }
        editing.Name = NameBox.Text.Trim(); editing.Date = DateOnly.FromDateTime(parsedDate); editing.Enabled = EnabledCheck.IsChecked == true;
        if (SelectedCheck.IsChecked == true && editing.Enabled) Selected = editing.Id;
        else if (Selected == editing.Id) Selected = items.FirstOrDefault(x => x.Enabled && x.Id != editing.Id)?.Id;
        EventList.Items.Refresh(); ErrorText.Text = "";
        return true;
    }
    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        var item = new DayEvent { Name = "新事件", Date = DateOnly.FromDateTime(DateTime.Today.AddDays(15)) };
        items.Add(item); EventList.SelectedItem = item;
        NameBox.Focus(); NameBox.SelectAll();
    }
    private void ApplyClick(object sender, RoutedEventArgs e) => Apply();
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (editing == null) return;
        if (MessageBox.Show(this, $"删除“{editing.Name}”？", "删除事件", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var removed = editing; editing = null; items.Remove(removed);
        if (Selected == removed.Id) Selected = items.FirstOrDefault(x => x.Enabled)?.Id;
        if (items.Count > 0) EventList.SelectedIndex = 0; else ClearEditor();
    }
    private void SaveClick(object sender, RoutedEventArgs e) { if (Apply()) DialogResult = true; }
}
