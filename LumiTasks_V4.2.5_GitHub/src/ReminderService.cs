using SystemIcons = System.Drawing.SystemIcons;
using System.Globalization;
using System.IO;
using System.Text.Json;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace LumiTasks.VisualPrototype;

public sealed class ReminderService : IDisposable
{
    private readonly TaskStore _store;
    private readonly NotifyIcon? _icon;
    private readonly ContextMenuStrip? _trayMenu;
    private readonly HashSet<string> _sent=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,DateTimeOffset> _snoozes=new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(TaskItem task,string message,bool allowWhileRunning)> _queue=new();
    private readonly string _ledger;
    private readonly string _snoozeFile;
    private ReminderPopupWindow? _popup;
    private DateTimeOffset _retryPopupAfter=DateTimeOffset.MinValue;
    private bool _disposed;
    public event Action<string>? GlowRequested;
    public event Action<string>? Acknowledged;
    public event Action? ExitRequested;
    public event Action? ShowRequested;
    public bool HasTrayRecovery => _icon is { Visible: true };

    public ReminderService(TaskStore store)
    {
        _store=store;
        _ledger=Path.Combine(_store.PreviewDataDirectory,"reminders.sent.json");
        _snoozeFile=Path.Combine(_store.PreviewDataDirectory,"reminders.snoozed.json");
        try
        {
            if(File.Exists(_ledger))
                foreach(var value in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_ledger))??new())
                    if(!string.IsNullOrWhiteSpace(value)) _sent.Add(value);
        }
        catch { /* optional ledger does not affect task data */ }
        try
        {
            if(File.Exists(_snoozeFile))
                foreach(var kv in JsonSerializer.Deserialize<Dictionary<string,DateTimeOffset>>(File.ReadAllText(_snoozeFile))??new())
                    _snoozes[kv.Key]=kv.Value;
        }
        catch { /* corrupted optional snooze file cannot prevent startup */ }
        try
        {
            _trayMenu=new ContextMenuStrip();
            _trayMenu.Items.Add(new ToolStripMenuItem("显示灵动岛",null,(_,_)=>ShowRequested?.Invoke()));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(new ToolStripMenuItem("退出 LumiTasks",null,(_,_)=>ExitRequested?.Invoke()));
            _icon=new NotifyIcon {Icon=SystemIcons.Information,Text="LumiTasks",Visible=true,ContextMenuStrip=_trayMenu};
            _icon.DoubleClick+=(_,_)=>ShowRequested?.Invoke();
        }
        catch { _icon=null;_trayMenu?.Dispose(); /* independent desktop popup still works */ }
    }
    public void Tick(DateTime now)
    {
        if(_disposed)return;
        var settings=_store.Settings;
        foreach(var task in _store.GetTasks(now.Date))
        {
            if(task.Done)continue;
            var prefix=now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+":"+task.ID;
            if(!TaskTimerEngine.IsActive(task) && !string.IsNullOrWhiteSpace(task.Reminder)
                && TimeSpan.TryParseExact(task.Reminder,"hh\\:mm",CultureInfo.InvariantCulture,out var time))
            {
                var leadMinutes=Math.Max(0,settings.ReminderLead);
                var due=now.Date.Add(time).AddMinutes(-leadMinutes);
                // V4.2 recorded only date + task ID, so changing the reminder time
                // later on the same day could never trigger another notification.
                // Use the *actual* schedule as part of the receipt key instead.
                // No task JSON fields or existing reminder records are deleted.
                var scheduledKey=$"{prefix}:scheduled:{time.Hours:00}{time.Minutes:00}:lead{leadMinutes}";
                // Ignore overdue reminders when launching much later than scheduled.
                if(now>=due && now<due.AddMinutes(2) && !_sent.Contains(scheduledKey))
                {
                    // Commit the delivery ledger only when at least one configured channel
                    // accepted this event. A silent failure may be retried from the queue.
                    if(Signal(task,"您设置的任务时间已到，请及时处理。",task.RemindMode))
                    {
                        _sent.Add(scheduledKey);
                        SaveLedger(now);
                    }
                }
            }
            if(task.Duration>0 && TaskTimerEngine.IsActive(task) && TaskTimerEngine.Elapsed(task)>=task.Duration*60L)
            {
                if(!task.Timer.OvertimeNotified)
                {
                    task.Timer.OvertimeNotified=true;
                    if(!_sent.Contains(prefix+":overtime") &&
                       Signal(task,"预计专注时间已到。",task.RemindMode, true))
                    {
                        _sent.Add(prefix+":overtime");
                        SaveLedger(now);
                    }
                    _store.Save();
                }
            }
        }
        bool snoozesChanged=false;
        foreach(var entry in _snoozes.Where(kv=>kv.Value<=DateTimeOffset.Now).ToArray())
        {
            _snoozes.Remove(entry.Key);
            snoozesChanged=true;
            var task=_store.GetAllTasks().FirstOrDefault(t=>t.ID==entry.Key);
            if(task!=null && !task.Done)
                Signal(task,"稍后提醒时间已到，请及时处理。",task.RemindMode,true);
        }
        // Write once after due snoozes are consumed; no repeat on the next second.
        if(snoozesChanged)SaveSnoozes();
        // If an attempted WPF popup could not be displayed, retry without
        // losing the queued reminder. Never replace it with another UI design.
        ShowNext();
    }

    // Starting work acknowledges the visual prompt for that task, without
    // prematurely closing an already-visible popup (the user closes that manually).
    public void TaskStarted(string taskID)
    {
        if(_disposed)return;
        if(_snoozes.Remove(taskID))SaveSnoozes();
        if(_queue.Count>0)
        {
            var retained=_queue.Where(x=>x.task.ID!=taskID || x.allowWhileRunning).ToArray();
            _queue.Clear();
            foreach(var item in retained)_queue.Enqueue(item);
        }
        Acknowledged?.Invoke(taskID);
    }

    private bool Signal(TaskItem task,string message,string mode,bool allowWhileRunning=false)
    {
        var config=_store.Settings;
        bool glow=config.Glow && (mode is "glow" or "both");
        bool popup=config.TimerNotify && (mode is "notification" or "both");
        if(glow || popup) LogPopupStatus($"SIGNAL mode={mode}, glow={glow}, popup={popup}, started={TaskTimerEngine.IsActive(task)}");
        if(glow)GlowRequested?.Invoke(task.ID);
        if(popup)
        {
            _queue.Enqueue((task,message,allowWhileRunning));
            ShowNext();
        }
        return glow || popup;
    }
    private void ShowNext()
    {
        if(_disposed || _popup!=null || _queue.Count==0 || DateTimeOffset.Now<_retryPopupAfter)return;
        while(_queue.Count>0)
        {
            var next=_queue.Peek();
            // An ordinary scheduled reminder is no longer relevant once work
            // has begun; an overtime reminder is still valid while working.
            if(next.task.Done || (!next.allowWhileRunning && TaskTimerEngine.IsActive(next.task)))
            { _queue.Dequeue();continue; }
            try
            {
                // Do not dequeue until WPF confirms Show() succeeded.
                var popup=new ReminderPopupWindow(next.task.Title,next.message);
                popup.ChoiceMade+=minutes=>
                {
                    Acknowledged?.Invoke(next.task.ID);
                    if(minutes is >0)
                    {
                        _snoozes[next.task.ID]=DateTimeOffset.Now.AddMinutes(minutes.Value);
                        SaveSnoozes();
                    }
                };
                popup.Closed+=(_,_)=>
                {
                    if(ReferenceEquals(_popup,popup))_popup=null;
                    ShowNext();
                };
                _popup=popup;
                popup.Show();
                _queue.Dequeue();
                _retryPopupAfter=DateTimeOffset.MinValue;
                LogPopupStatus("POPUP_SHOWN");
            }
            catch(Exception ex)
            {
                _popup=null;
                _retryPopupAfter=DateTimeOffset.Now.AddSeconds(10);
                LogPopupStatus("POPUP_FAILED: " + ex);
            }
            return;
        }
    }

    private static void LogPopupStatus(string message)
    {
        try
        {
            var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LumiTasksUpdater","logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir,"reminder-popup-runtime.log"),
                $"[{DateTimeOffset.Now:O}] {message}\n");
        }
        catch { /* logging must not prevent a task from running */ }
    }
    private void SaveLedger(DateTime today)
    {
        try
        {
            Directory.CreateDirectory(_store.PreviewDataDirectory);
            var cutoff=today.Date.AddDays(-7).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            var recent=_sent.Where(x=>string.CompareOrdinal(x,cutoff)>=0).ToArray();
            File.WriteAllText(_ledger,JsonSerializer.Serialize(recent));
        }
        catch { /* a failed ledger write must not crash timers */ }
    }
    private void SaveSnoozes()
    {
        try
        {
            Directory.CreateDirectory(_store.PreviewDataDirectory);
            File.WriteAllText(_snoozeFile,JsonSerializer.Serialize(_snoozes));
        }
        catch { /* optional reminder state */ }
    }
    public void Dispose()
    {
        _disposed=true;
        _queue.Clear();
        if(_popup!=null)
        {
            var closing=_popup;_popup=null;
            closing.Close();
        }
        SaveSnoozes();
        if(_icon!=null){_icon.Visible=false;_icon.Dispose();}
        _trayMenu?.Dispose();
    }
}
