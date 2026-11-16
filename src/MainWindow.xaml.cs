using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Interop;

namespace LumiTasks.VisualPrototype;

public partial class MainWindow : Window
{
    private readonly TaskStore _store = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private DrawerWindow? _drawer;
    private ReminderService? _reminder;
    private readonly Dictionary<string,TextBlock> _orbTimers=new();
    private readonly Dictionary<string,DateTime> _glowUntil=new(StringComparer.OrdinalIgnoreCase);
    private ContextMenu? _islandMenu;
    private bool _expanded = true;
    private bool _manualHidden;
    private bool _drawerHiddenByHotkey;
    private HwndSource? _hotkeySource;
    private bool _hotkeyRegistered;
    private const int HotkeyId = 0x423;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkL = 0x4C;
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _hotkeySource = HwndSource.FromHwnd(handle);
        _hotkeySource?.AddHook(HotkeyMessageHook);
        _hotkeyRegistered = RegisterHotKey(handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, VkL);
        if (!_hotkeyRegistered)
        {
            // Do not offer a hide command when the only global restore key is unavailable.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                MessageBox.Show(this,
                    "Ctrl + Alt + L 注册失败，可能已被其他程序占用。\n本次运行已禁用主动隐藏，请先检查快捷键冲突。\n仍可尝试使用系统托盘显示灵动岛。",
                    "LumiTasks 快捷键提示", MessageBoxButton.OK, MessageBoxImage.Warning)));
        }
    }

    private nint HotkeyMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == HotkeyId)
        {
            // The hotkey is registered on the main HWND, including when it is hidden.
            if (_manualHidden || !IsVisible) RestoreIsland();
            else HideIsland();
            handled = true;
        }
        return 0;
    }
    private bool _refreshQueued;
    private DateTime _lastDay = DateTime.Today;
    public MainWindow()
    {
        InitializeComponent();
        _store.LoadPreviewCopy();
        ThemeManager.Apply(_store.Settings.Theme);
        _expanded = _store.Settings.Expanded;
        _ticker.Tick += (_, _) => Tick();
        _store.Changed += StoreChanged;
    }
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyIslandSize();
        Refresh();
        try
        {
            _reminder=new ReminderService(_store);
            _reminder.GlowRequested+=id=> { _glowUntil[id]=DateTime.Now.AddMinutes(2);Refresh(); };
            _reminder.Acknowledged+=id=> { if(_glowUntil.Remove(id))Refresh(); };
            _reminder.ShowRequested+=()=>Dispatcher.BeginInvoke(new Action(RestoreIsland));
            _reminder.ExitRequested+=ExitApplication;
        }
        catch { /* tray permission must not prevent startup */ }
        BuildIslandMenu();
        try { SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged; }
        catch { /* timer correctness does not depend on power notifications */ }
        _ticker.Start();
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _ticker.Stop();
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
            _hotkeyRegistered = false;
        }
        _hotkeySource?.RemoveHook(HotkeyMessageHook);
        _hotkeySource = null;
        try { SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged; } catch { }
        _reminder?.Dispose();
        _drawer?.Close();
    }
    private void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Timers are based on UTC start timestamps, not dispatcher tick counts.
        // Never pause them when the display turns off or Windows suspends.
        if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) Tick(); }));
        else if (e.Mode == PowerModes.Suspend)
            Dispatcher.BeginInvoke(new Action(() => { try { _store.Save(); } catch { } }));
    }
    private void StoreChanged()
    {
        // Avoid rebuilding the clicked orb/button midway through a MouseUp or
        // Click routed event. Coalesce multiple saves into one deferred render.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(StoreChanged));
            return;
        }
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _refreshQueued = false;
            if (!IsLoaded) return;
            ThemeManager.Apply(_store.Settings.Theme);
            foreach(var id in _glowUntil.Keys.ToArray())
            {
                var current=_store.GetTasks(DateTime.Today).FirstOrDefault(t=>t.ID==id);
                if(current is null || current.Done)_glowUntil.Remove(id);
            }
            Refresh();
        }));
    }
    private void Tick()
    {
        // Re-evaluate the screen width after resolution / work-area changes.
        UpdateIslandSizeIfNeeded();
        if (_glowUntil.Count>0)
        {
            bool changed=false;
            foreach(var id in _glowUntil.Where(kv=>kv.Value<=DateTime.Now).Select(kv=>kv.Key).ToArray())
                changed=_glowUntil.Remove(id)||changed;
            if(changed)Refresh();
        }
        if (_lastDay != DateTime.Today)
        {
            _lastDay = DateTime.Today;
            _store.EnsureDate(DateTime.Today);
        }
        _reminder?.Tick(DateTime.Now);
        _store.UpdateTimers();
        UpdateTimerLabels();
        if (_manualHidden)
        {
            if (IsVisible) Hide();
            return;
        }
        // Manual hotkey is the only island visibility control; never auto-hide for fullscreen apps.
        if(!IsVisible)Show();
    }
    private void UpdateTimerLabels()
    {
        foreach(var task in _store.GetTasks(DateTime.Today))
            if(_orbTimers.TryGetValue(task.ID,out var label)) label.Text=task.Done?"已完成":TaskTimerEngine.Label(task);
    }
    private void BuildIslandMenu()
    {
        _islandMenu=new ContextMenu
        {
            Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Background=(Brush)FindResource("SoftFill"),
            Foreground=(Brush)FindResource("Ink"),
            BorderBrush=(Brush)FindResource("SoftStroke")
        };
        var tasks=new MenuItem {Header="打开任务列表"};
        tasks.Click+=(_,_)=>OpenDrawer(false);
        var settings=new MenuItem {Header="打开设置"};
        settings.Click+=(_,_)=>OpenSettings();
        var exit=new MenuItem {Header="退出 LumiTasks"};
        exit.Click+=(_,_)=>ExitApplication();
        _islandMenu.Items.Add(tasks);
        _islandMenu.Items.Add(settings);
        _islandMenu.Items.Add(new Separator());
        _islandMenu.Items.Add(exit);
    }
    private static bool IsWithinButton(DependencyObject? source)
    {
        var current=source;
        while(current!=null)
        {
            if(current is System.Windows.Controls.Primitives.ButtonBase) return true;
            DependencyObject? parent=null;
            try { parent=VisualTreeHelper.GetParent(current); } catch { }
            current=parent ?? LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // OriginalSource is often a TextBlock/Image inside the button template,
        // not the Button itself. Walk the visual tree so clicks are not stolen.
        if (IsWithinButton(e.OriginalSource as DependencyObject)) return;
        if (e.ClickCount==2) { _islandMenu!.IsOpen=false; OpenDrawer(false); }
        else if (_islandMenu!=null)
        {
            _islandMenu.PlacementTarget=sender as UIElement;
            _islandMenu.IsOpen=true;
        }
        e.Handled=true;
    }
    private void OpenSettings()
    {
        OpenDrawer(false);
        _drawer?.OpenSettings();
    }
    private void HideIsland()
    {
        // Without a registered global restore key, manual hiding is unsafe.
        if (!_hotkeyRegistered) return;
        _manualHidden=true;
        _drawerHiddenByHotkey=_drawer is { IsVisible: true };
        if (_drawerHiddenByHotkey) _drawer?.Hide();
        Hide();
    }
    private void RestoreIsland()
    {
        _manualHidden=false;
        if(!IsVisible) Show();
        WindowState=WindowState.Normal;
        Refresh();
        if (_drawerHiddenByHotkey && _drawer != null)
        {
            _drawer.Show();
            _drawer.Activate();
        }
        else Activate();
        _drawerHiddenByHotkey=false;
    }
    private void ExitApplication()
    {
        _ticker.Stop();
        try { _store.Save(); }
        catch(Exception ex)
        {
            if(MessageBox.Show("保存设置失败："+ex.Message+"\n仍然退出吗？","LumiTasks",
                MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)
            { _ticker.Start();return; }
        }
        System.Windows.Application.Current.Shutdown();
    }
    private void ApplyIslandSize()
    {
        // Preserve the V4.2.4 visual layout inside the scaled Viewbox.
        // Sizes are WPF device-independent units; cap them to the primary work area.
        double selectedWidth = Math.Clamp(_store.Settings.IslandSize, 0, 2) switch
        {
            0 => 620.0,
            2 => 760.0,
            _ => 690.0
        };
        var area = SystemParameters.WorkArea;
        double available = Math.Max(240, area.Width - 24);
        double actualWidth = Math.Min(selectedWidth, available);
        IslandSizeHost.Width = actualWidth;
        IslandSizeHost.Height = actualWidth * 92.0 / 690.0;

        // Keep the transparent host, hanging strings and buttons centered.
        // Do not change any orb image/rope dimensions from the existing settings.
        double hostWidth = Math.Min(Math.Max(240, area.Width - 8), Math.Max(780, actualWidth + 90));
        if (Math.Abs(Width - hostWidth) > 0.5) Width = hostWidth;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 5;
    }
    private double _lastIslandSize = double.NaN;
    private void UpdateIslandSizeIfNeeded()
    {
        double requested = Math.Clamp(_store.Settings.IslandSize, 0, 2);
        double available = SystemParameters.WorkArea.Width;
        double key = requested * 100000 + available;
        if (double.IsNaN(_lastIslandSize) || Math.Abs(_lastIslandSize - key) > .01)
        {
            _lastIslandSize = key;
            ApplyIslandSize();
        }
    }
    private void Refresh()
    {
        UpdateIslandSizeIfNeeded();
        var tasks = _store.GetTasks(DateTime.Today);
        var done = tasks.Count(t => t.Done);
        ProgressText.Text = $"{done}/{tasks.Count}";
        ProgressFill.Background=new SolidColorBrush(ThemeManager.Accent(_store.Settings.Theme));
        ProgressFill.Width = tasks.Count == 0 ? 0 : 112.0 * done / tasks.Count;
        ExpandButton.Content = _expanded ? "⌄" : "⌃";
        Opacity=Math.Clamp(_store.Settings.Opacity/100.0,0.40,1.0);
        OrbsPanel.Children.Clear();
        _orbTimers.Clear();
        if (!_expanded) return;
        var shown=tasks.Where(t=>!t.Done||_store.Settings.ShowDone).Take(5).ToList();
        int[] wires = { 69, 101, 75, 108, 79 };
        foreach (var pair in shown.Select((task, index) => (task,index)))
            OrbsPanel.Children.Add(CreateOrb(pair.task, Math.Max(22,wires[pair.index] + (_store.Settings.BallLength-1)*23)));
    }
    private FrameworkElement CreateOrb(TaskItem task, int ropeLength)
    {
        var orbSize=Math.Clamp(128.0+(_store.Settings.BallSize-1)*16.0,100,158);
        var column = new StackPanel { Width = orbSize+8, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        column.Children.Add(new Rectangle { Width=2, Height=ropeLength, Fill=new SolidColorBrush(Color.FromArgb(165,165,143,251)),HorizontalAlignment=System.Windows.HorizontalAlignment.Center });
        var root = new Grid { Width = orbSize+2, Height = orbSize+7, Cursor=Cursors.Hand };
        // The supplied orb PNGs have a transparent top margin. Extend the
        // connector behind that transparent area so the wire visibly meets
        // the coloured sphere instead of stopping short.
        root.Children.Add(new Rectangle
        {
            Width=2, Height=27, VerticalAlignment=VerticalAlignment.Top,
            HorizontalAlignment=System.Windows.HorizontalAlignment.Center,
            Fill=new SolidColorBrush(Color.FromArgb(165,165,143,251)), IsHitTestVisible=false
        });
        var image = new Image { Width=orbSize, Height=orbSize+5, Stretch=Stretch.Fill, Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/ball_{task.BallAsset}.png")) };
        root.Children.Add(image);
        // Glow only for an actual reminder emitted by ReminderService.
        // Never infer a glow from being inside a clock-time window: that made
        // a task keep flashing after its Start button was pressed.
        if(_store.Settings.Glow && !task.Done && _glowUntil.ContainsKey(task.ID))
        {
            var accent=ThemeManager.Accent(_store.Settings.Theme);
            var halo=new Ellipse
            {
                Stroke=new SolidColorBrush(accent),StrokeThickness=5,
                Margin=new Thickness(3),IsHitTestVisible=false,
                Effect=new System.Windows.Media.Effects.DropShadowEffect
                {Color=accent,BlurRadius=38,ShadowDepth=0,Opacity=.95}
            };
            root.Children.Add(halo);
            halo.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.30,1,TimeSpan.FromMilliseconds(700))
                {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            image.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.57,1,TimeSpan.FromMilliseconds(700))
                {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
        }
        if(_store.Settings.Sway && !task.Done)
        {
            // Mild sway only; avoids constant layout reconstruction on every tick.
            var rotate=new RotateTransform(0,orbSize/2,0);
            root.RenderTransform=rotate;
            var animation=new DoubleAnimation(-1.2,1.2,TimeSpan.FromSeconds(3.5)) { AutoReverse=true, RepeatBehavior=RepeatBehavior.Forever };
            rotate.BeginAnimation(RotateTransform.AngleProperty,animation);
        }
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment=System.Windows.HorizontalAlignment.Center, Margin=new Thickness(12,15,12,0),IsHitTestVisible=false };
        var status = TaskTimerEngine.StateFor(task);
        stack.Children.Add(new TextBlock
        {
            Text = task.Done ? "✓" : status == "running" || status == "overtime" ? "⏸" : "▶",
            FontSize=22, FontWeight=FontWeights.SemiBold, Foreground=Brushes.White, TextAlignment=TextAlignment.Center,
            HorizontalAlignment=System.Windows.HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = task.Title.Length > 6 ? task.Title[..5] + "…" : task.Title,
            FontSize=15, FontWeight=FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(53,40,94)),
            TextAlignment=TextAlignment.Center, TextWrapping=TextWrapping.Wrap, MaxWidth=107, Margin=new Thickness(0,4,0,0)
        });
        var timerLabel=new TextBlock
        {
            Text = task.Done ? "已完成" : TaskTimerEngine.Label(task),
            FontSize=13, Foreground=new SolidColorBrush(Color.FromRgb(71,50,117)),FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center, HorizontalAlignment=System.Windows.HorizontalAlignment.Center, Margin=new Thickness(0,3,0,0),
            Visibility=_store.Settings.ShowTime?Visibility.Visible:Visibility.Collapsed
        };
        _orbTimers[task.ID]=timerLabel;
        stack.Children.Add(timerLabel);
        root.Children.Add(stack);
        if (!task.Done)
        {
            var finish = new Button { Content="✓",Width=26,Height=26,VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,
                Margin=new Thickness(0,0,8,6), ToolTip="完成任务（与开始/暂停计时分开）",Style=(Style)FindResource("PrimaryButton"),Padding=new Thickness(0)};
            finish.Click += (_,e) => { e.Handled=true; _store.Complete(task.ID); };
            root.Children.Add(finish);
        }
        root.MouseLeftButtonUp += (_,e) =>
        {
            if(IsWithinButton(e.OriginalSource as DependencyObject)) return;
            if(!task.Done)
            {
                e.Handled=true;
                _store.StartOrPause(task.ID);
                if(TaskTimerEngine.IsActive(task))
                {
                    _glowUntil.Remove(task.ID);
                    _reminder?.TaskStarted(task.ID);
                }
            }
        };
        root.MouseRightButtonUp += (_,e) => { if (!task.Done) { e.Handled=true; _store.Complete(task.ID); } };
        column.Children.Add(root);
        return column;
    }
    private void OpenDrawer(bool editor)
    {
        if (_drawer != null)
        {
            if (!_drawer.IsVisible) _drawer.Show();
            _drawer.Activate();
            if (editor) _drawer.ShowEditor(false);
            return;
        }
        _drawer = new DrawerWindow(_store);
        _drawer.Closed += (_, _) => { _drawer = null; _drawerHiddenByHotkey = false; };
        _drawer.Show();
        if (editor) _drawer.ShowEditor(false);
    }
    private void Add_Click(object sender, RoutedEventArgs e) => OpenDrawer(true);
    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        e.Handled=true;
        _expanded=!_expanded;
        _store.Settings.Expanded=_expanded;
        _store.Save(); // Changed event rebuilds the island once, from the new state.
    }
}
