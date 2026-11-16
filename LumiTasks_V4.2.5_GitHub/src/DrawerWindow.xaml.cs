using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using System.Media;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using System.IO;

namespace LumiTasks.VisualPrototype;
public partial class DrawerWindow : Window
{
    private readonly TaskStore _store;
    private bool _ready;
    private int _settingsSection;
    private readonly DispatcherTimer _opacitySaveDelay=new() {Interval=TimeSpan.FromMilliseconds(280)};
    private readonly DispatcherTimer _themeSaveDelay=new() {Interval=TimeSpan.FromMilliseconds(350)};
    private bool _updatingCategories;
    private string _categorySignature="";
    private bool _updatingThemeUi;
    private string _previousCategory="学习";
    private string _previousDefaultCategory="学习";
    private const string CustomCategoryChoice="自定义…";
    private const string LegacyCategoryChoice="保留原有分类（已删除）";
    private string? _editingLegacyCategory;
    private DateTime _selectedDay=DateTime.Today;
    private string? _editingID;
    private int _duration; // zero means no countdown; use positive elapsed timer
    private int _durationEditHour;
    private int _durationEditMinute;
    private int _priority;
    private string _reminderMode="both";
    private int _hour=9;
    private int _minute=30;
    private bool _hasReminder;
    private readonly Dictionary<string,string> _repeats=new()
    {
        ["仅一次"]="once",["每天"]="daily",["工作日"]="weekdays",["每周"]="weekly"
    };
    public DrawerWindow(TaskStore store)
    {
        InitializeComponent();
        _store=store;
        _opacitySaveDelay.Tick+=(_,_)=> {_opacitySaveDelay.Stop();_store.Save();};
        _themeSaveDelay.Tick+=(_,_)=> {_themeSaveDelay.Stop();_store.Save();};
        _store.Changed+=OnStoreChanged;
        RepeatCombo.ItemsSource=_repeats.Keys.ToList();
        RepeatCombo.SelectedIndex=0;
        _previousCategory=_store.Settings.DefaultCategory;
        _previousDefaultCategory=_store.Settings.DefaultCategory;
        RefreshCategories();
        OpacitySlider.Value=_store.Settings.Opacity is >= 40 and <= 100 ? _store.Settings.Opacity : 100;
        ShowTimeToggle.IsChecked=_store.Settings.ShowTime;
        ShowDoneToggle.IsChecked=_store.Settings.ShowDone;
        AutoCarryToggle.IsChecked=_store.Settings.AutoCarry;
        _store.Settings.Startup=StartupRegistration.IsRegistered();
        StartupToggle.IsChecked=_store.Settings.Startup;
        GlowToggle.IsChecked=_store.Settings.Glow;
        NotifyToggle.IsChecked=_store.Settings.TimerNotify;
        ThemeManager.Apply(_store.Settings.Theme);
        _ready=true;
        RefreshSettingsButtons();
        Appearance_Click(this,new RoutedEventArgs());
        ShowTasksPage();
    }
    protected override void OnClosed(EventArgs e)
    {
        if(_opacitySaveDelay.IsEnabled || _themeSaveDelay.IsEnabled)
        {
            _opacitySaveDelay.Stop();_themeSaveDelay.Stop();_store.Save();
        }
        _store.Changed-=OnStoreChanged;
        base.OnClosed(e);
    }
    private void OnStoreChanged()
    {
        if(!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnStoreChanged);return; }
        RefreshCategories();
        if (TaskView.Visibility==Visibility.Visible) RefreshTasks();
        if (StatisticsView.Visibility==Visibility.Visible) RefreshStatistics();
    }
    private void Window_Loaded(object sender,RoutedEventArgs e)
    {
        Left=Math.Max(5,SystemParameters.WorkArea.Right-Width-16);
        Top=Math.Max(5,SystemParameters.WorkArea.Top+(SystemParameters.WorkArea.Height-Height)/2);
        if (SystemParameters.WorkArea.Height<Height)
        {
            Height=Math.Max(560,SystemParameters.WorkArea.Height-20);
            Top=SystemParameters.WorkArea.Top+10;
        }
        if (EditorView.Visibility==Visibility.Visible) FocusTextBox(TaskName);
    }
    private void FocusTextBox(TextBox box)
    {
        // Show()/page changes can occur before the transparent WPF window has
        // completed activation and layout. Focus on the input dispatcher turn.
        // The ComboBox popup can still be closing during SelectionChanged.
        // Focus only after the pending input events have finished.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsVisible || !box.IsVisible || !box.IsEnabled) return;
            Activate();
            box.Focus();
            Keyboard.Focus(box);
            box.CaretIndex=box.Text.Length;
        }));
    }
    private void Input_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocusWithin)
        {
            box.Focus();
            Keyboard.Focus(box);
        }
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void ChangePage(ScrollViewer page)
    {
        TaskView.Visibility=Visibility.Collapsed;
        EditorView.Visibility=Visibility.Collapsed;
        SettingsView.Visibility=Visibility.Collapsed;
        StatisticsView.Visibility=Visibility.Collapsed;
        page.Visibility=Visibility.Visible;
        // Top tabs remain Task, Settings, Stats; editor is within Task.
        SetMainTab(TaskTab,page==TaskView||page==EditorView);
        SetMainTab(SettingTab,page==SettingsView);
        SetMainTab(StatsTab,page==StatisticsView);
    }
    private void SetMainTab(Button btn,bool selected)
    {
        btn.Style=(Style)FindResource(selected?"NavSelected":"NavIdle");
        btn.FontSize=17;
    }
    private void SetTab(Button btn,bool selected)
    {
        btn.Style=(Style)FindResource(selected?"PrimaryButton":"PillTab");
        btn.FontSize=17;
    }
    private void ShowTasksPage(){ChangePage(TaskView);RefreshTasks();}
    private void TaskTab_Click(object sender,RoutedEventArgs e)=>ShowTasksPage();
    private void SettingTab_Click(object sender,RoutedEventArgs e)=>OpenSettings();
    public void OpenSettings() => ChangePage(SettingsView);
    private void StatsTab_Click(object sender,RoutedEventArgs e){ChangePage(StatisticsView);RefreshStatistics();}
    private void Today_Click(object sender,RoutedEventArgs e){_selectedDay=DateTime.Today;RefreshTasks();}
    private void Tomorrow_Click(object sender,RoutedEventArgs e){_selectedDay=DateTime.Today.AddDays(1);RefreshTasks();}
    private void RefreshTasks()
    {
        var isToday=_selectedDay.Date==DateTime.Today;
        TodayButton.Style=(Style)FindResource(isToday?"PrimaryButton":"PillTab");
        TomorrowButton.Style=(Style)FindResource(!isToday?"PrimaryButton":"PillTab");
        TaskList.Children.Clear();
        var items=_store.GetTasks(_selectedDay);
        if(items.Count==0)
        {
            TaskList.Children.Add(new TextBlock { Text="暂无任务，可点击右上方添加。", Foreground=(Brush)FindResource("Muted"),FontSize=14,Margin=new Thickness(12,20,0,0) });
            return;
        }
        foreach(var t in items)
        {
            var card=new Border { Background=new SolidColorBrush(Color.FromRgb(251,249,255)),CornerRadius=new CornerRadius(18),
                BorderBrush=new SolidColorBrush(Color.FromRgb(219,204,249)),BorderThickness=new Thickness(1),
                Padding=new Thickness(13,10,13,10),Margin=new Thickness(0,0,0,10) };
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var check=new Button {Content=t.Done?"✓":"○",Width=37,Height=37,FontSize=23,Padding=new Thickness(0),Margin=new Thickness(0,0,11,0),
                Background=t.Done?new SolidColorBrush(Color.FromRgb(116,190,147)) : new SolidColorBrush(Color.FromRgb(226,215,254)),
                Style=(Style)FindResource("GlassButton")};
            check.Click+=(_,_)=>_store.ToggleComplete(_selectedDay,t.ID);
            grid.Children.Add(check);
            var title=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            title.Children.Add(new TextBlock {Text=t.Title,FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("Ink"),TextTrimming=TextTrimming.CharacterEllipsis});
            title.Children.Add(new TextBlock {Text=$"{t.Category}  ·  {TaskTimerEngine.Label(t)}",FontSize=12,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,5,0,0)});
            Grid.SetColumn(title,1);grid.Children.Add(title);
            var edit=new Button {Content="✎",Width=34,Height=34,Padding=new Thickness(0),FontSize=17,Style=(Style)FindResource("GlassButton")};
            edit.Click+=(_,_)=> ShowEditorFor(t);
            Grid.SetColumn(edit,2);grid.Children.Add(edit);
            var delete=new Button {Content="×",Width=34,Height=34,Padding=new Thickness(0),FontSize=21,Background=Brushes.Transparent,Style=(Style)FindResource("GlassButton")};
            delete.Click+=(_,_)=> { if(MessageBox.Show("确定删除此任务吗？","删除任务",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes) _store.Remove(_selectedDay,t.ID); };
            Grid.SetColumn(delete,3);grid.Children.Add(delete);
            card.Child=grid;TaskList.Children.Add(card);
        }
    }
    private void NewTask_Click(object sender,RoutedEventArgs e)=>ShowEditor(false);
    public void ShowEditor(bool tomorrow)
    {
        if(tomorrow) _selectedDay=DateTime.Today.AddDays(1);
        _editingID=null;_editingLegacyCategory=null;TaskName.Text="";TaskNotes.Text="";
        CustomCategoryBox.Visibility=Visibility.Collapsed;
        RefreshCategories();
        CategoryCombo.SelectedItem=_store.Settings.DefaultCategory;RepeatCombo.SelectedItem="仅一次";
        _duration=0;_priority=0;_reminderMode="both";_hasReminder=false;_hour=9;_minute=30;
        RefreshForm();ChangePage(EditorView);FocusTextBox(TaskName);
    }
    private void ShowEditorFor(TaskItem task)
    {
        _editingID=task.ID;
        _editingLegacyCategory=_store.Categories.Contains(task.Category, StringComparer.OrdinalIgnoreCase)
            ? null : task.Category;
        TaskName.Text=task.Title;TaskNotes.Text=task.Note;
        CustomCategoryBox.Visibility=Visibility.Collapsed;
        RefreshCategories();
        CategoryCombo.SelectedItem=_editingLegacyCategory is null ? task.Category : LegacyCategoryChoice;
        RepeatCombo.SelectedItem=_repeats.FirstOrDefault(x=>x.Value==task.Repeat).Key??"仅一次";
        _duration=task.Duration;_priority=task.Priority;_reminderMode=task.RemindMode;
        _hasReminder=TimeSpan.TryParseExact(task.Reminder,"hh\\:mm",null,out var time);
        if(_hasReminder){_hour=time.Hours;_minute=time.Minutes;}
        RefreshForm();ChangePage(EditorView);FocusTextBox(TaskName);
    }
    private void RefreshForm()
    {
        DateBadge.Content=_selectedDay.Date==DateTime.Today?"▦ 今天":"▦ 明天";
        TimeButton.Content=_hasReminder?$"◷  {_hour:00}:{_minute:00}  ⌄":"◷  选择时间  ⌄";
        HourText.Text=_hour.ToString("00");MinuteText.Text=_minute.ToString("00");
        DurationButton.Content=_duration>0 ? $"◷  {_duration/60:00}:{_duration%60:00}  ⌄" : "◷  选择时长  ⌄";
        RefreshPriorityVisuals();
        SetTab(RemindBoth,_reminderMode=="both");SetTab(RemindGlow,_reminderMode=="glow");SetTab(RemindSystem,_reminderMode=="notification");
    }
    // Priority selection has intentionally different contrast rules for each pastel.
    // Unlike PrimaryButton, important/urgent must NEVER turn white on their light background.
    private void RefreshPriorityVisuals()
    {
        SetPriorityVisual(NormalPriority, "普通", _priority==0, "#7F4DDF", "#FFFFFF", "#F3EEFE", "#59418F", "#8655E7");
        SetPriorityVisual(ImportantPriority, "重要", _priority==1, "#FFE6D3", "#884116", "#FFF1E6", "#96501E", "#DA914D");
        SetPriorityVisual(UrgentPriority, "紧急", _priority==2, "#F9DFE8", "#9F3054", "#FDEEF4", "#A13759", "#D56E95");
    }
    private void SetPriorityVisual(Button button,string label,bool selected,
        string selectedBackground,string selectedInk,string idleBackground,string idleInk,string border)
    {
        button.Style=(Style)FindResource("GlassButton");
        button.Background=selected && label=="普通" ? (Brush)FindResource("VioletGradient") :
            (Brush)new BrushConverter().ConvertFromString(selected?selectedBackground:idleBackground)!;
        button.Foreground=(Brush)new BrushConverter().ConvertFromString(selected?selectedInk:idleInk)!;
        button.BorderBrush=selected ? (Brush)new BrushConverter().ConvertFromString(border)! : Brushes.Transparent;
        button.BorderThickness=new Thickness(selected?1.8:0);
        button.Content=selected ? "✓  "+label : label;
        button.FontSize=15;
        button.Padding=new Thickness(8,6,8,6);
    }
    private void DateBadge_Click(object sender,RoutedEventArgs e) { if (_editingID != null) { MessageBox.Show("编辑已有任务时暂不支持跨日期移动。请先返回任务列表。"); return; } _selectedDay=_selectedDay.Date==DateTime.Today ? DateTime.Today.AddDays(1):DateTime.Today;RefreshForm(); }
    private void Return_Click(object sender,RoutedEventArgs e)=>ShowTasksPage();
    private void Time_Click(object sender,RoutedEventArgs e){HourText.Text=$"{_hour:00}";MinuteText.Text=$"{_minute:00}";TimePopup.IsOpen=!TimePopup.IsOpen;}
    private void HourPlus_Click(object sender,RoutedEventArgs e){_hour=(_hour+1)%24;HourText.Text=$"{_hour:00}";}
    private void HourMinus_Click(object sender,RoutedEventArgs e){_hour=(_hour+23)%24;HourText.Text=$"{_hour:00}";}
    private void MinutePlus_Click(object sender,RoutedEventArgs e){_minute=(_minute+1)%60;MinuteText.Text=$"{_minute:00}";}
    private void MinuteMinus_Click(object sender,RoutedEventArgs e){_minute=(_minute+59)%60;MinuteText.Text=$"{_minute:00}";}
    private void NoTime_Click(object sender,RoutedEventArgs e){_hasReminder=false;TimePopup.IsOpen=false;RefreshForm();}
    private void ConfirmTime_Click(object sender,RoutedEventArgs e){_hasReminder=true;TimePopup.IsOpen=false;RefreshForm();}
    // The duration picker follows the existing reminder-time popup's styling;
    // editing values does not mutate an existing timer until Confirm is pressed.
    private void Duration_Click(object sender,RoutedEventArgs e)
    {
        if(!DurationPopup.IsOpen)
        {
            _durationEditHour=Math.Clamp(_duration/60,0,24);
            _durationEditMinute=Math.Clamp(_duration%60,0,59);
            RefreshDurationDigits();
        }
        DurationPopup.IsOpen=!DurationPopup.IsOpen;
    }
    private void RefreshDurationDigits()
    {
        DurationHourText.Text=_durationEditHour.ToString("00");
        DurationMinuteText.Text=_durationEditMinute.ToString("00");
    }
    private void ReadDurationDigits()
    {
        if(int.TryParse(DurationHourText.Text,out int hour)) _durationEditHour=Math.Clamp(hour,0,24);
        if(int.TryParse(DurationMinuteText.Text,out int minute)) _durationEditMinute=Math.Clamp(minute,0,59);
        if(_durationEditHour==24) _durationEditMinute=0;
    }
    private void DurationDigits_PreviewTextInput(object sender,System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled=!e.Text.All(char.IsDigit);
    }
    private void DurationHourPlus_Click(object sender,RoutedEventArgs e)
    {
        ReadDurationDigits();_durationEditHour=Math.Min(24,_durationEditHour+1);RefreshDurationDigits();
    }
    private void DurationHourMinus_Click(object sender,RoutedEventArgs e)
    {
        ReadDurationDigits();_durationEditHour=Math.Max(0,_durationEditHour-1);RefreshDurationDigits();
    }
    private void DurationMinutePlus_Click(object sender,RoutedEventArgs e)
    {
        ReadDurationDigits();if(_durationEditHour<24)_durationEditMinute=(_durationEditMinute+1)%60;RefreshDurationDigits();
    }
    private void DurationMinuteMinus_Click(object sender,RoutedEventArgs e)
    {
        ReadDurationDigits();if(_durationEditHour<24)_durationEditMinute=(_durationEditMinute+59)%60;RefreshDurationDigits();
    }
    private void DurationNone_Click(object sender,RoutedEventArgs e)
    {
        _duration=0;DurationPopup.IsOpen=false;RefreshForm();
    }
    private void DurationConfirm_Click(object sender,RoutedEventArgs e)
    {
        if(!int.TryParse(DurationHourText.Text,out var hour) ||
           !int.TryParse(DurationMinuteText.Text,out var minute) ||
           hour<0 || hour>24 || minute<0 || minute>59 || (hour==24&&minute!=0))
        {
            MessageBox.Show("请输入有效时长（0 到 24 小时），分钟为 0–59。", "时间格式错误");return;
        }
        _duration=hour*60+minute;
        DurationPopup.IsOpen=false;RefreshForm();
    }
    private void PriorityNormal_Click(object sender,RoutedEventArgs e){_priority=0;RefreshForm();}
    private void PriorityImportant_Click(object sender,RoutedEventArgs e){_priority=1;RefreshForm();}
    private void PriorityUrgent_Click(object sender,RoutedEventArgs e){_priority=2;RefreshForm();}
    private void ReminderBoth_Click(object sender,RoutedEventArgs e){_reminderMode="both";RefreshForm();}
    private void ReminderGlow_Click(object sender,RoutedEventArgs e){_reminderMode="glow";RefreshForm();}
    private void ReminderSystem_Click(object sender,RoutedEventArgs e){_reminderMode="notification";RefreshForm();}
    private void SaveTask_Click(object sender,RoutedEventArgs e)
    {
        var title=TaskName.Text.Trim();
        if(title.Length==0){MessageBox.Show("请输入任务名称。");TaskName.Focus();return;}
        if(title.Length>120){MessageBox.Show("任务名称最多120个字符。");return;}
        int duration=_duration;
        var selected=_store.GetTasks(_selectedDay).FirstOrDefault(x=>x.ID==_editingID);
        var task=selected??new TaskItem {ID=Guid.NewGuid().ToString("N")};
        task.Title=title;task.Note=TaskNotes.Text;task.Duration=duration;task.Priority=_priority;
        task.Reminder=_hasReminder?$"{_hour:00}:{_minute:00}":"";
        var chosenCategory=CategoryCombo.SelectedItem?.ToString();
        if(chosenCategory==CustomCategoryChoice)
        {
            MessageBox.Show("请先输入并确认自定义分类名称。","LumiTasks");
            CustomCategoryName.Focus();return;
        }
        // Editing an existing task with a deleted category must not silently rewrite its history.
        task.Category=chosenCategory==LegacyCategoryChoice && selected is not null
            ? selected.Category : chosenCategory??"学习";
        task.Repeat=_repeats.GetValueOrDefault(RepeatCombo.SelectedItem?.ToString()??"仅一次","once");
        task.RemindMode=_reminderMode;
        // Editing an old task preserves its timer, color and other legacy fields.
        if(selected is null) _store.Add(_selectedDay,task);else _store.Update(_selectedDay,task);
        ShowTasksPage();
    }
    private void RefreshStatistics()
    {
        var today=_store.GetStatistics(DateTime.Today);
        var days=_store.GetRecentDays(DateTime.Today,7).ToList();
        var weekDone=days.Sum(x=>x.tasks.Count(t=>t.Done));
        var weekTotal=days.Sum(x=>x.tasks.Count);
        var allElapsed=_store.GetAllTasks().Sum(TaskTimerEngine.Elapsed);
        StatisticsSummary.Text=$"{today.done} / {today.total}";
        StatisticsRate.Text=today.total==0?"0%":$"{100.0*today.done/today.total:0}%";
        StatisticsTime.Text=$"{allElapsed / 3600}时 {allElapsed % 3600 / 60}分";
        StatisticsWeek.Text=$"{weekDone} / {weekTotal}";
        StatisticsEmpty.Visibility=weekTotal==0?Visibility.Visible:Visibility.Collapsed;
        SevenDayChart.Children.Clear();
        var barPanel = new UniformGrid { Columns=7 };
        for(var i=0;i<days.Count;i++)
        {
            var item=days[i];
            int count=item.tasks.Count;
            int done=item.tasks.Count(x=>x.Done);
            double fraction=count==0 ? 0 : (double)done/count;
            var column=new StackPanel { VerticalAlignment=VerticalAlignment.Bottom, HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch,Margin=new Thickness(4,0,4,0) };
            column.Children.Add(new TextBlock{Text=count==0?"·":$"{fraction*100:0}%",TextAlignment=TextAlignment.Center,Foreground=(Brush)FindResource("Muted"),FontSize=11,Margin=new Thickness(0,0,0,6)});
            var track=new Border { Background=new SolidColorBrush(Color.FromRgb(225,215,247)),CornerRadius=new CornerRadius(9),Height=105,Width=26,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom };
            var grid=new Grid();grid.Children.Add(track);
            var foreground=new Border { Background=(Brush)FindResource("VioletGradient"),CornerRadius=new CornerRadius(9),Width=26,Height=Math.Max(0,105*fraction),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=System.Windows.HorizontalAlignment.Center };
            grid.Children.Add(foreground);
            column.Children.Add(grid);
            column.Children.Add(new TextBlock { Text=item.date.ToString("ddd",System.Globalization.CultureInfo.GetCultureInfo("zh-CN")),HorizontalAlignment=System.Windows.HorizontalAlignment.Center,FontSize=12,Foreground=(Brush)FindResource("Ink"),Margin=new Thickness(0,8,0,0) });
            barPanel.Children.Add(column);
        }
        SevenDayChart.Children.Add(barPanel);
    }
    private void Appearance_Click(object sender,RoutedEventArgs e)=>ShowSettingsSection(0);
    private void More_Click(object sender,RoutedEventArgs e)=>ShowSettingsSection(1);
    private void ShowSettingsSection(int number)
    {
        _settingsSection=number;
        AppearanceSection.Visibility=number==0?Visibility.Visible:Visibility.Collapsed;
        BehaviorSection.Visibility=number==0?Visibility.Visible:Visibility.Collapsed;
        MoreSection.Visibility=number==1?Visibility.Visible:Visibility.Collapsed;
        AppearanceTab.Style=(Style)FindResource(number==0?"NavSelected":"NavIdle");
        MoreTab.Style=(Style)FindResource(number==1?"NavSelected":"NavIdle");
        AppearanceTab.FontSize=MoreTab.FontSize=16;
    }
    private void RefreshSettingsButtons()
    {
        SelectSet(Math.Clamp(_store.Settings.IslandSize, 0, 2),new[]{IslandSmall,IslandMedium,IslandLarge});
        SelectSet(_store.Settings.BallSize,new[]{BallSmall,BallMedium,BallLarge});
        SelectSet(_store.Settings.BallLength,new[]{WireShort,WireMedium,WireLong});
        RefreshThemeSelector();
        SelectSet(_store.Settings.ReminderLead switch {5=>1,10=>2,15=>3,_=>0},new[]{Lead0,Lead5,Lead10,Lead15});
        OpacityLabel.Text=$"{_store.Settings.Opacity}%";
    }
    private void SelectSet(int selected,Button[] buttons)
    {
        for(int i=0;i<buttons.Length;i++)
        {
            buttons[i].Style=(Style)FindResource(i==selected?"PrimaryButton":"PillTab");
            buttons[i].FontSize=buttons.Length==4?12:14;
            buttons[i].Padding=new Thickness(buttons.Length==4?1:5);
        }
    }
    private void IslandSmall_Click(object sender,RoutedEventArgs e)=>ChangeIslandSize(0);
    private void IslandMedium_Click(object sender,RoutedEventArgs e)=>ChangeIslandSize(1);
    private void IslandLarge_Click(object sender,RoutedEventArgs e)=>ChangeIslandSize(2);
    private void ChangeIslandSize(int size)
    {
        if (_store.Settings.IslandSize == size) return;
        _store.Settings.IslandSize=size;
        RefreshSettingsButtons();
        _store.Save();
    }
    private void BallSmall_Click(object sender,RoutedEventArgs e)=>ChangeSize(0);
    private void BallMedium_Click(object sender,RoutedEventArgs e)=>ChangeSize(1);
    private void BallLarge_Click(object sender,RoutedEventArgs e)=>ChangeSize(2);
    private void ChangeSize(int size){_store.Settings.BallSize=size;RefreshSettingsButtons();_store.Save();}
    private void WireShort_Click(object sender,RoutedEventArgs e)=>ChangeWire(0);
    private void WireMedium_Click(object sender,RoutedEventArgs e)=>ChangeWire(1);
    private void WireLong_Click(object sender,RoutedEventArgs e)=>ChangeWire(2);
    private void ChangeWire(int value){_store.Settings.BallLength=value;RefreshSettingsButtons();_store.Save();}
    private void RefreshThemeSelector()
    {
        _updatingThemeUi=true;
        var key=_store.Settings.Theme;
        int hue=ThemeManager.TryGetCustomHue(key,out var customHue) ? customHue :
            key switch {"blue"=>210,"pink"=>322,"dark"=>267,_=>270};
        HueSlider.Value=hue;
        ThemeStatus.Text=key=="purple" ? "默认 · 梦幻紫" : "自选主题色";
        ThemeDefault.Style=(Style)FindResource(key=="purple"?"PrimaryButton":"GlassButton");
        _updatingThemeUi=false;
    }
    private void UpdateLiveTheme(string key)
    {
        _store.Settings.Theme=key;
        ThemeManager.Apply(key);
        ThemeStatus.Text=key=="purple"?"默认 · 梦幻紫":"自选主题色";
        ThemeDefault.Style=(Style)FindResource(key=="purple"?"PrimaryButton":"GlassButton");
        _themeSaveDelay.Stop();_themeSaveDelay.Start();
    }
    private void HueSlider_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!_ready || _updatingThemeUi)return;
        UpdateLiveTheme(ThemeManager.CustomKey((int)Math.Round(HueSlider.Value)));
    }
    private void ThemeDefault_Click(object sender,RoutedEventArgs e)
    {
        UpdateLiveTheme("purple");
        RefreshThemeSelector();
    }
    private void ThemeRandom_Click(object sender,RoutedEventArgs e)
    {
        var hue=Random.Shared.Next(0,360);
        _updatingThemeUi=true;
        HueSlider.Value=hue;
        _updatingThemeUi=false;
        UpdateLiveTheme(ThemeManager.CustomKey(hue));
    }
    private void Lead0_Click(object sender,RoutedEventArgs e)=>ChangeLead(0);
    private void Lead5_Click(object sender,RoutedEventArgs e)=>ChangeLead(5);
    private void Lead10_Click(object sender,RoutedEventArgs e)=>ChangeLead(10);
    private void Lead15_Click(object sender,RoutedEventArgs e)=>ChangeLead(15);
    private void ChangeLead(int mins){_store.Settings.ReminderLead=mins;RefreshSettingsButtons();_store.Save();}
    private void RefreshCategoryDeleteButtons()
    {
        DeleteCategoryButton.Visibility=_store.CanDeleteCategory(CategoryCombo.SelectedItem?.ToString())
            ? Visibility.Visible : Visibility.Collapsed;
        DeleteDefaultCategoryButton.Visibility=_store.CanDeleteCategory(DefaultCategoryCombo.SelectedItem?.ToString())
            ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshCategories()
    {
        if(_updatingCategories)return;
        _updatingCategories=true;
        try
        {
            // A historic task may retain a removed category. Present a sentinel only in
            // that task's editor; the deleted name never reappears as a global choice.
            if (_editingID is not null)
            {
                var old=_store.GetTasks(_selectedDay).FirstOrDefault(x=>x.ID==_editingID)?.Category;
                _editingLegacyCategory=old is not null &&
                    !_store.Categories.Contains(old, StringComparer.OrdinalIgnoreCase) ? old : null;
            }
            var current=CategoryCombo.SelectedItem?.ToString();
            var currentDefault=DefaultCategoryCombo.SelectedItem?.ToString();
            var defaultItems=_store.Categories.Append(CustomCategoryChoice).ToArray();
            var taskItems=(_editingLegacyCategory is null
                ? defaultItems : defaultItems.Prepend(LegacyCategoryChoice)).ToArray();
            var signature=string.Join("\u001F",taskItems)+"\u001E"+string.Join("\u001F",defaultItems);
            if(_categorySignature!=signature)
            {
                _categorySignature=signature;
                CategoryCombo.ItemsSource=taskItems;
                DefaultCategoryCombo.ItemsSource=defaultItems;
                var selected=current==CustomCategoryChoice ? _previousCategory : current;
                var selectedDefault=currentDefault==CustomCategoryChoice ? _previousDefaultCategory : currentDefault;
                CategoryCombo.SelectedItem=taskItems.Contains(selected) ? selected :
                    _editingLegacyCategory is not null ? LegacyCategoryChoice :
                    taskItems.Contains(_store.Settings.DefaultCategory) ? _store.Settings.DefaultCategory : "学习";
                DefaultCategoryCombo.SelectedItem=defaultItems.Contains(selectedDefault) ? selectedDefault :
                    defaultItems.Contains(_store.Settings.DefaultCategory) ? _store.Settings.DefaultCategory : "学习";
            }
            RefreshCategoryDeleteButtons();
        }
        finally { _updatingCategories=false; }
    }
    private void DeleteCategory_Click(object sender,RoutedEventArgs e) => DeleteSelectedCategory(CategoryCombo);
    private void DeleteDefaultCategory_Click(object sender,RoutedEventArgs e) => DeleteSelectedCategory(DefaultCategoryCombo);
    private void DeleteSelectedCategory(ComboBox combo)
    {
        var name=combo.SelectedItem?.ToString();
        if(!_store.CanDeleteCategory(name))return;
        var message=$"确定删除自定义分类‘{name}’吗？\n\n已有任务仍保留原分类文字；两个分类下拉框将不再显示它。";
        if(MessageBox.Show(this,message,"删除自定义分类",MessageBoxButton.YesNo,MessageBoxImage.Question)
            !=MessageBoxResult.Yes)return;
        if (!_store.DeleteCustomCategory(name))return;
        _categorySignature="";
        RefreshCategories();
        _previousCategory=CategoryCombo.SelectedItem?.ToString()??"学习";
        _previousDefaultCategory=DefaultCategoryCombo.SelectedItem?.ToString()??"学习";
    }
    private void Category_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready || _updatingCategories)return;
        var selected=CategoryCombo.SelectedItem?.ToString();
        if(selected==CustomCategoryChoice)
        {
            CustomCategoryBox.Visibility=Visibility.Visible;
            CustomCategoryName.Clear();FocusTextBox(CustomCategoryName);
        }
        else if(!string.IsNullOrWhiteSpace(selected))
        {
            _previousCategory=selected;
            CustomCategoryBox.Visibility=Visibility.Collapsed;
        }
        RefreshCategoryDeleteButtons();
    }
    private void DefaultCategory_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready || _updatingCategories)return;
        var selected=DefaultCategoryCombo.SelectedItem?.ToString();
        if(selected==CustomCategoryChoice)
        {
            DefaultCustomCategoryBox.Visibility=Visibility.Visible;
            DefaultCustomCategoryName.Clear();FocusTextBox(DefaultCustomCategoryName);
        }
        else if(!string.IsNullOrWhiteSpace(selected))
        {
            _previousDefaultCategory=selected;
            DefaultCustomCategoryBox.Visibility=Visibility.Collapsed;
            _store.Settings.DefaultCategory=selected;
            _store.Save();
        }
        RefreshCategoryDeleteButtons();
    }
    private void CancelCategory_Click(object sender,RoutedEventArgs e)
    {
        CustomCategoryBox.Visibility=Visibility.Collapsed;
        CategoryCombo.SelectedItem=_previousCategory;
    }
    private void CancelDefaultCategory_Click(object sender,RoutedEventArgs e)
    {
        DefaultCustomCategoryBox.Visibility=Visibility.Collapsed;
        DefaultCategoryCombo.SelectedItem=_previousDefaultCategory;
    }
    private void SaveCategory_Click(object sender,RoutedEventArgs e)
    {
        if(!_store.AddCustomCategory(CustomCategoryName.Text,out var name))
        {
            MessageBox.Show("请输入 1–20 个字符的有效分类名称。","LumiTasks");return;
        }
        CustomCategoryBox.Visibility=Visibility.Collapsed;
        RefreshCategories();CategoryCombo.SelectedItem=name;
        _previousCategory=name;
    }
    private void SaveDefaultCategory_Click(object sender,RoutedEventArgs e)
    {
        if(!_store.AddCustomCategory(DefaultCustomCategoryName.Text,out var name))
        {
            MessageBox.Show("请输入 1–20 个字符的有效分类名称。","LumiTasks");return;
        }
        DefaultCustomCategoryBox.Visibility=Visibility.Collapsed;
        RefreshCategories();DefaultCategoryCombo.SelectedItem=name;
        _previousDefaultCategory=name;
        // For an existing name, AddCustomCategory does not save; selection does.
        if(_store.Settings.DefaultCategory!=name){_store.Settings.DefaultCategory=name;_store.Save();}
    }
    private void BehaviorToggle_Changed(object sender,RoutedEventArgs e)
    {
        if(!_ready)return;
        var s=_store.Settings;
        s.ShowTime=ShowTimeToggle.IsChecked==true;
        s.ShowDone=ShowDoneToggle.IsChecked==true;
        s.AutoCarry=AutoCarryToggle.IsChecked==true;
        bool start=StartupToggle.IsChecked==true;
        if(start!=s.Startup)
        {
            try {StartupRegistration.Set(start);s.Startup=start;}
            catch(Exception ex) {MessageBox.Show("无法更新开机启动设置："+ex.Message,"LumiTasks",MessageBoxButton.OK,MessageBoxImage.Warning);StartupToggle.IsChecked=s.Startup;return;}
        }
        _store.Save();
    }
    private void Export_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Filter="JSON 备份 (*.json)|*.json",FileName=$"LumiTasks_{DateTime.Now:yyyyMMdd_HHmmss}.json",Title="导出任务备份"};
        if(dialog.ShowDialog(this)==true)
        {
            try {_store.ExportBackupTo(dialog.FileName);MessageBox.Show("任务备份已导出。","LumiTasks");}
            catch(Exception ex){MessageBox.Show(ex.Message,"LumiTasks");}
        }
    }
    private void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="JSON 备份 (*.json)|*.json",Title="导入任务备份"};
        if(dialog.ShowDialog(this)!=true)return;
        if(MessageBox.Show("将覆盖 LumiTasks 当前的任务副本（原版数据不受影响）。继续？","LumiTasks",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        try
        {
            _store.ImportBackup(dialog.FileName);
            ThemeManager.Apply(_store.Settings.Theme);
            RefreshCategories();
            _ready=false;
            DefaultCategoryCombo.SelectedItem=_store.Settings.DefaultCategory;
            _previousDefaultCategory=_store.Settings.DefaultCategory;
            ShowTimeToggle.IsChecked=_store.Settings.ShowTime;
            ShowDoneToggle.IsChecked=_store.Settings.ShowDone;
            AutoCarryToggle.IsChecked=_store.Settings.AutoCarry;
                GlowToggle.IsChecked=_store.Settings.Glow;
            NotifyToggle.IsChecked=_store.Settings.TimerNotify;
            OpacitySlider.Value=_store.Settings.Opacity;
            _ready=true;
            RefreshSettingsButtons();RefreshTasks();
            MessageBox.Show("导入成功。","LumiTasks");
        }
        catch(Exception ex) {MessageBox.Show(ex.Message,"LumiTasks",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void OpacitySlider_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!_ready)return;
        var value=(int)Math.Round(OpacitySlider.Value);
        OpacityLabel.Text=$"{value}%";
        _store.Settings.Opacity=value;
        // Debounce writes and orb redraw during slider dragging.
        _opacitySaveDelay.Stop();_opacitySaveDelay.Start();
    }
    private void GlowToggle_Changed(object sender,RoutedEventArgs e){if(!_ready)return;_store.Settings.Glow=GlowToggle.IsChecked==true;_store.Save();}
    private void NotifyToggle_Changed(object sender,RoutedEventArgs e){if(!_ready)return;_store.Settings.TimerNotify=NotifyToggle.IsChecked==true;_store.Save();}
}
