using System.Windows;
using System.Windows.Controls;

namespace LumiTasks.VisualPrototype;

public partial class ReminderPopupWindow : Window
{
    public event Action<int?>? ChoiceMade;
    private bool _decided;
    public ReminderPopupWindow(string title,string message)
    {
        InitializeComponent();
        TaskTitle.Text=title;
        TaskMessage.Text=message;
    }
    private void Popup_Loaded(object sender,RoutedEventArgs e)
    {
        // Keep the exact working V4.2 manual-placement path instead of switching
        // WindowStartupLocation to CenterScreen (the unverified V4.3 approach).
        // SystemParameters.WorkArea uses WPF device-independent units, matching
        // this Window's Width/Height and avoiding pixel/DPI coordinate mixing.
        var area=SystemParameters.WorkArea;
        var actualWidth=ActualWidth>0 ? ActualWidth : Width;
        var actualHeight=ActualHeight>0 ? ActualHeight : Height;
        Left=Math.Round(area.Left+(area.Width-actualWidth)/2.0);
        Top=Math.Round(area.Top+(area.Height-actualHeight)/2.0);
    }
    private void Decide(int? snoozeMinutes)
    {
        if(_decided)return;
        _decided=true;
        ChoiceMade?.Invoke(snoozeMinutes);
        Close();
    }
    private void Snooze_Click(object sender,RoutedEventArgs e)
    {
        if(SnoozeMinutes.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(),out var value))
            Decide(value);
        else Decide(10);
    }
    private void Acknowledge_Click(object sender,RoutedEventArgs e)=>Decide(null);
    private void Close_Click(object sender,RoutedEventArgs e)=>Decide(null);
    protected override void OnClosed(EventArgs e)
    {
        if(!_decided) { _decided=true; ChoiceMade?.Invoke(null); }
        base.OnClosed(e);
    }
}
