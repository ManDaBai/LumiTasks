using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LumiTasks.VisualPrototype;

// Match the existing Go JSON keys so no manual task re-entry is needed.
public sealed class TaskItem
{
    [JsonPropertyName("id")] public string ID { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("reminder")] public string Reminder { get; set; } = "";
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("done")] public bool Done { get; set; }
    [JsonPropertyName("done_at")] public string DoneAt { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "学习";
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("repeat")] public string Repeat { get; set; } = "once";
    [JsonPropertyName("source_id")] public string SourceID { get; set; } = "";
    [JsonPropertyName("color")] public string Color { get; set; } = "auto";
    [JsonPropertyName("remind_mode")] public string RemindMode { get; set; } = "both";
    [JsonPropertyName("pinned")] public bool Pinned { get; set; }
    [JsonPropertyName("timer")] public TimerData Timer { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Other { get; set; }
    [JsonIgnore] public string BallAsset
    {
        get
        {
            if (Done) return "green";
            var names = new[] { "purple", "blue", "gold", "teal", "peach" };
            if (names.Contains(Color, StringComparer.OrdinalIgnoreCase)) return Color.ToLowerInvariant();
            unchecked { uint hash = 2166136261; foreach(var c in ID) hash = (hash ^ c) * 16777619; return names[(int)(hash % (uint)names.Length)]; }
        }
    }
}
public sealed class TimerData
{
    [JsonPropertyName("state")] public string State { get; set; } = "idle";
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("elapsed_seconds")] public long ElapsedSeconds { get; set; }
    [JsonPropertyName("overtime_notified")] public bool OvertimeNotified { get; set; }
    [JsonPropertyName("actual_seconds")] public long ActualSeconds { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Other { get; set; }
}
public sealed class SettingsData
{
    [JsonPropertyName("expanded")] public bool Expanded { get; set; } = true;
    [JsonPropertyName("island_size")] public int IslandSize { get; set; } = 1;
    [JsonPropertyName("ball_size")] public int BallSize { get; set; } = 1;
    [JsonPropertyName("ball_length")] public int BallLength { get; set; } = 1;
    [JsonPropertyName("show_time")] public bool ShowTime { get; set; } = true;
    [JsonPropertyName("show_done")] public bool ShowDone { get; set; } = true;
    [JsonPropertyName("sway")] public bool Sway { get; set; } = true;
    [JsonPropertyName("click_complete")] public bool ClickComplete { get; set; } = false;
    [JsonPropertyName("auto_carry")] public bool AutoCarry { get; set; } = false;
    [JsonPropertyName("reminder_lead")] public int ReminderLead { get; set; } = 0;
    [JsonPropertyName("default_category")] public string DefaultCategory { get; set; } = "学习";
    [JsonPropertyName("startup")] public bool Startup { get; set; } = false;
    [JsonPropertyName("remind")] public bool Remind { get; set; } = true;
    [JsonPropertyName("theme")] public string Theme { get; set; } = "purple";
    [JsonPropertyName("opacity")] public int Opacity { get; set; } = 100;
    [JsonPropertyName("glow")] public bool Glow { get; set; } = true;
    [JsonPropertyName("timer_notify")] public bool TimerNotify { get; set; } = true;
    [JsonPropertyName("sound_style")] public string SoundStyle { get; set; } = "soft";
    // Keep category names in the existing settings object for JSON backward compatibility.
    [JsonPropertyName("custom_categories")] public List<string> CustomCategories { get; set; } = new();
    // Remember removed choices so old task records cannot re-add them to the menus.
    [JsonPropertyName("deleted_categories")] public List<string> DeletedCategories { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Other { get; set; }
}
public sealed class TaskDatabase
{
    [JsonPropertyName("version")] public int Version { get; set; } = 3;
    [JsonPropertyName("days")] public Dictionary<string,List<TaskItem>> Days { get; set; } = new();
    [JsonPropertyName("settings")] public SettingsData Settings { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Other { get; set; }
}
public static class TaskTimerEngine
{
    public static bool IsActive(TaskItem task) => task.Timer.State is "running" or "overtime";
    public static string StateFor(TaskItem task)
    {
        if (task.Done) return "completed";
        long elapsed = Elapsed(task);
        if (IsActive(task) && task.Duration > 0 && elapsed >= task.Duration * 60L) return "overtime";
        return string.IsNullOrWhiteSpace(task.Timer.State) ? "idle" : task.Timer.State;
    }
    // Repair inconsistent timer records on load. A task marked Done must never
    // retain a live timer; an undone task must not keep a stale completed state.
    public static bool Normalize(TaskItem task)
    {
        task.Timer ??= new TimerData();
        task.Timer.State ??= "idle";
        task.Timer.StartedAt ??= "";
        bool changed = false;
        if (task.Done)
        {
            // Done is authoritative: never add wall-clock time from a stale
            // started_at value to an already completed task on next launch.
            long elapsed = Math.Max(0, Math.Max(task.Timer.ElapsedSeconds, task.Timer.ActualSeconds));
            if (task.Timer.ElapsedSeconds != elapsed) { task.Timer.ElapsedSeconds = elapsed; changed = true; }
            if (task.Timer.ActualSeconds != elapsed) { task.Timer.ActualSeconds = elapsed; changed = true; }
            if (task.Timer.StartedAt.Length != 0) { task.Timer.StartedAt = ""; changed = true; }
            if (task.Timer.State != "completed") { task.Timer.State = "completed"; changed = true; }
        }
        else if (task.Timer.State == "completed")
        {
            task.Timer.StartedAt = "";
            task.Timer.ActualSeconds = 0;
            task.Timer.State = task.Timer.ElapsedSeconds > 0 ? "paused" : "idle";
            changed = true;
        }
        return changed;
    }
    private static DateTimeOffset? ParseGoTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Go RFC3339Nano may have nine fractional digits. .NET normalizes to seven.
        value = Regex.Replace(value, @"(\.\d{7})\d+(?=Z|[+-]\d{2}:\d{2}$)", "$1");
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }
    public static long Elapsed(TaskItem task)
    {
        var seconds = Math.Max(0,task.Timer.ElapsedSeconds);
        if (IsActive(task) && ParseGoTime(task.Timer.StartedAt) is { } start)
            seconds += Math.Max(0, (long)(DateTimeOffset.UtcNow-start).TotalSeconds);
        return seconds;
    }
    public static void Start(TaskItem task)
    {
        if (task.Done || IsActive(task) || task.Timer.State == "completed") return;
        task.Timer.StartedAt=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        task.Timer.State=task.Duration > 0 && Elapsed(task)>=task.Duration*60L ? "overtime" : "running";
    }
    public static void Pause(TaskItem task)
    {
        if (task.Done || !IsActive(task)) return;
        task.Timer.ElapsedSeconds=Elapsed(task);
        task.Timer.StartedAt="";
        task.Timer.State=task.Duration > 0 && task.Timer.ElapsedSeconds >= task.Duration*60L ? "paused_overtime" : "paused";
    }
    public static void Complete(TaskItem task)
    {
        // Completion is deliberately idempotent. A duplicate UI event must not
        // silently undo a task that has already been completed.
        if (task.Done) return;
        var elapsed=Elapsed(task);
        task.Timer.ElapsedSeconds=elapsed;
        task.Timer.ActualSeconds=elapsed;
        task.Timer.StartedAt="";
        task.Timer.State="completed";
        task.Done=true;
        task.DoneAt=DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
    }
    public static void UndoComplete(TaskItem task)
    {
        if (!task.Done) return;
        task.Done=false;task.DoneAt="";
        task.Timer.ActualSeconds=0;task.Timer.StartedAt="";
        task.Timer.State=task.Timer.ElapsedSeconds==0 ? "idle" : task.Duration>0 && task.Timer.ElapsedSeconds>=task.Duration*60L ? "paused_overtime" : "paused";
    }
    public static string Label(TaskItem task)
    {
        var elapsed=Elapsed(task);
        string fmt(long sec) => sec>=3600 ? $"{sec/3600}:{sec%3600/60:00}:{sec%60:00}" : $"{sec/60:00}:{sec%60:00}";
        if (task.Done) return "已完成";
        var state=StateFor(task);
        if (state is "running" or "overtime") return task.Duration>0 && elapsed>=task.Duration*60L ? "超时 +"+fmt(elapsed-task.Duration*60L) : task.Duration>0 ? fmt(Math.Max(0,task.Duration*60L-elapsed)) : fmt(elapsed);
        if (state is "paused" or "paused_overtime") return task.Duration>0 && elapsed>=task.Duration*60L ? "暂停 +"+fmt(elapsed-task.Duration*60L) : "暂停 "+fmt(Math.Max(0,task.Duration*60L-elapsed));
        return task.Duration>0 ? $"{task.Duration}分钟" : "点击开始";
    }
}
public sealed class TaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() {WriteIndented=true,PropertyNameCaseInsensitive=true};
    private readonly string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumiTasksWPFPreview");
    private string DataFile => Path.Combine(_dir,"tasks.preview.json");
    private TaskDatabase _db = new();
    public event Action? Changed;
    public SettingsData Settings => _db.Settings;
    public string PreviewDataDirectory => _dir;
    private static readonly string[] FixedCategories = { "学习", "写作", "文献", "数据处理", "田野记录", "生活", "其他" };
    private static bool IsFixedCategory(string? name) =>
        !string.IsNullOrWhiteSpace(name) && FixedCategories.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    public bool CanDeleteCategory(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !IsFixedCategory(name) &&
        Categories.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    public bool DeleteCustomCategory(string? name)
    {
        if (!CanDeleteCategory(name)) return false;
        var target = name!.Trim();
        Settings.CustomCategories ??= new();
        Settings.DeletedCategories ??= new();
        Settings.CustomCategories.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
        if (!Settings.DeletedCategories.Contains(target, StringComparer.OrdinalIgnoreCase))
            Settings.DeletedCategories.Add(target);
        if (string.Equals(Settings.DefaultCategory, target, StringComparison.OrdinalIgnoreCase))
            Settings.DefaultCategory = "学习";
        // Intentionally do not touch any task's Category, completed state or timer.
        Save();
        return true;
    }

    private static bool RemoveLegacyFullscreenOption(SettingsData settings)
    {
        // Old JSON may store this obsolete key in extension data; remove it once.
        if (settings.Other is null) return false;
        var obsolete = settings.Other.Keys.Where(x =>
            string.Equals(x, "fullscreen_hide", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var key in obsolete) settings.Other.Remove(key);
        return obsolete.Length > 0;
    }

    public IReadOnlyList<string> Categories
    {
        get
        {
            var result = new List<string>(FixedCategories);
            var known = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
            var deleted = new HashSet<string>(Settings.DeletedCategories ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var name in (Settings.CustomCategories ?? new List<string>())
                     .Concat(GetAllTasks().Select(x => x.Category))
                     .Append(Settings.DefaultCategory))
            {
                var category = (name ?? "").Trim();
                if (category.Length > 0 && !deleted.Contains(category) && known.Add(category)) result.Add(category);
            }
            return result;
        }
    }
    public bool AddCustomCategory(string? name, out string normalized)
    {
        normalized = (name ?? "").Trim();
        if (normalized.Length < 1 || normalized.Length > 20 ||
            normalized == "自定义…" || normalized == "保留原有分类（已删除）" ||
            normalized.Any(char.IsControl)) return false;
        // C# does not allow an out parameter to be captured by a lambda (CS1628).
        // Copy it to an ordinary local before searching; preserve the same category semantics.
        var candidate = normalized;
        var found = Categories.FirstOrDefault(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
        if (found != null) { normalized = found; return true; }
        Settings.CustomCategories ??= new List<string>();
        Settings.DeletedCategories ??= new List<string>();
        // Explicitly adding a previously deleted name restores it.
        Settings.DeletedCategories.RemoveAll(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
        Settings.CustomCategories.Add(normalized);
        Save();
        return true;
    }
    public IEnumerable<TaskItem> GetAllTasks() => _db.Days.Values.SelectMany(x => x);
    public IEnumerable<(DateTime date, IReadOnlyList<TaskItem> tasks)> GetRecentDays(DateTime today,int count)
    {
        for (int offset=count-1;offset>=0;offset--)
        {
            var d=today.Date.AddDays(-offset);
            yield return (d,GetTasks(d));
        }
    }
    public string ExportBackup(string directory)
    {
        Directory.CreateDirectory(directory);
        var filename=Path.Combine(directory,$"LumiTasks_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(filename, JsonSerializer.Serialize(_db,JsonOptions));
        return filename;
    }
    public void ExportBackupTo(string filename)
    {
        var folder=Path.GetDirectoryName(filename)??throw new InvalidDataException("备份路径无效");
        Directory.CreateDirectory(folder);
        File.WriteAllText(filename,JsonSerializer.Serialize(_db,JsonOptions));
    }
    public void ImportBackup(string filename)
    {
        var info=new FileInfo(filename);
        if(info.Length>12*1024*1024) throw new InvalidDataException("备份文件过大（限制 12MB）。");
        var loaded=JsonSerializer.Deserialize<TaskDatabase>(File.ReadAllText(filename),JsonOptions);
        if(loaded?.Days is null) throw new InvalidDataException("不是有效的 LumiTasks 备份文件。");
        var ownBackup=Path.Combine(_dir,"backups");
        ExportBackup(ownBackup);
        _db=loaded;_db.Settings ??=new();_db.Settings.CustomCategories ??=new();
        _db.Settings.DeletedCategories ??=new();_db.Days ??=new();
        RemoveLegacyFullscreenOption(_db.Settings);
        foreach (var task in _db.Days.Values.SelectMany(x => x)) TaskTimerEngine.Normalize(task);
        Save();
    }
    public IReadOnlyList<TaskItem> GetTasks(DateTime day)
    {
        var key=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        return _db.Days.TryGetValue(key,out var tasks)?tasks: Array.Empty<TaskItem>();
    }
    public void LoadPreviewCopy()
    {
        Directory.CreateDirectory(_dir);
        var original=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumiTasks","tasks.json");
        if (!File.Exists(DataFile) && File.Exists(original))
        {
            try { File.Copy(original,DataFile); }
            catch (IOException) { /* still safe to use an empty preview, never change original */ }
            catch (UnauthorizedAccessException) { /* read-only legacy data must not break preview startup */ }
        }
        if (File.Exists(DataFile))
        {
            try
            {
                var value=JsonSerializer.Deserialize<TaskDatabase>(File.ReadAllText(DataFile),JsonOptions);
                if (value?.Days != null) _db=value;
            }
            catch { _db=new(); } // never modify original file
        }
        _db.Settings ??= new();
        _db.Settings.CustomCategories ??= new();
        _db.Settings.DeletedCategories ??= new();
        bool legacySettingsCleared = RemoveLegacyFullscreenOption(_db.Settings);
        _db.Days ??= new();
        bool normalizedTimers = false;
        foreach (var task in _db.Days.Values.SelectMany(x => x))
            normalizedTimers = TaskTimerEngine.Normalize(task) || normalizedTimers;
        if (normalizedTimers || legacySettingsCleared) Save();
        if (!File.Exists(DataFile) && !File.Exists(original))
        {
            // Demo tasks in the *separate preview* database only.
            var today=DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            _db.Days[today] = new()
            {
                new TaskItem { ID=Guid.NewGuid().ToString("N"),Title="阅读文献",Category="文献",Duration=30,Color="purple" },
                new TaskItem { ID=Guid.NewGuid().ToString("N"),Title="论文写作",Category="写作",Duration=60,Color="blue" },
                new TaskItem { ID=Guid.NewGuid().ToString("N"),Title="整理资料",Category="资料",Duration=30,Color="peach" }
            };
            Save();
        }
        EnsureDate(DateTime.Today);
    }
    public void Save()
    {
        Directory.CreateDirectory(_dir);
        var temp=DataFile+".tmp";
        var backup=DataFile+".bak";
        _db.Version=Math.Max(3,_db.Version);
        File.WriteAllText(temp,JsonSerializer.Serialize(_db,JsonOptions));
        if (File.Exists(DataFile)) File.Copy(DataFile,backup,true);
        File.Move(temp,DataFile,true);
        Changed?.Invoke();
    }
    public void Add(DateTime day,TaskItem task)
    {
        var key=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if (!_db.Days.TryGetValue(key,out var list)) _db.Days[key]=list=new();
        list.Add(task);Save();
    }
    public bool Update(DateTime day, TaskItem item)
    {
        var list=GetTasks(day);
        var index=list.ToList().FindIndex(x=>x.ID==item.ID);
        if(index<0) return false;
        var key=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        _db.Days[key][index]=item;
        Save();return true;
    }
    public void Remove(DateTime day,string id)
    {
        var key=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if (_db.Days.TryGetValue(key,out var list) && list.RemoveAll(x=>x.ID==id)>0) Save();
    }
    public void StartOrPause(string id)
    {
        var t=GetTasks(DateTime.Today).FirstOrDefault(x=>x.ID==id);
        if (t is null) return;
        bool normalized=TaskTimerEngine.Normalize(t);
        if (t.Done) { if(normalized) Save(); return; }
        // Each task owns its own timer. Starting or pausing one task must not
        // mutate timers belonging to other tasks or other dates.
        if (TaskTimerEngine.IsActive(t)) TaskTimerEngine.Pause(t);
        else TaskTimerEngine.Start(t);
        Save();
    }
    public void Complete(string id) => Complete(DateTime.Today, id);
    public void Complete(DateTime day, string id)
    {
        var t=GetTasks(day).FirstOrDefault(x=>x.ID==id);
        if (t is null) return;
        bool normalized=TaskTimerEngine.Normalize(t);
        if (t.Done) { if(normalized) Save(); return; }
        TaskTimerEngine.Complete(t);
        Save();
    }
    public void UndoComplete(DateTime day, string id)
    {
        var t=GetTasks(day).FirstOrDefault(x=>x.ID==id);
        if (t is null) return;
        bool normalized=TaskTimerEngine.Normalize(t);
        if (!t.Done) { if(normalized) Save(); return; }
        TaskTimerEngine.UndoComplete(t);
        Save();
    }
    public void ToggleComplete(DateTime day, string id)
    {
        var t=GetTasks(day).FirstOrDefault(x=>x.ID==id);
        if (t is null) return;
        TaskTimerEngine.Normalize(t);
        if (t.Done) TaskTimerEngine.UndoComplete(t);
        else TaskTimerEngine.Complete(t);
        Save();
    }
    public void UpdateTimers()
    {
        bool changed=false;
        foreach(var t in GetTasks(DateTime.Today))
        {
            if (t.Done && TaskTimerEngine.Normalize(t)) { changed = true; continue; }
            if(t.Duration>0 && !t.Done && TaskTimerEngine.IsActive(t) && !t.Timer.OvertimeNotified && TaskTimerEngine.Elapsed(t)>=t.Duration*60L)
            {
                t.Timer.State="overtime";t.Timer.OvertimeNotified=true;changed=true;
            }
        }
        if(changed) Save();
    }
    public void EnsureDate(DateTime target)
    {
        var key=target.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(!_db.Days.ContainsKey(key)) _db.Days[key]=new();
        // Repeat only once per original source and day. The existing app remains authoritative.
        var have=_db.Days[key].Select(x=>x.ID).ToHashSet();
        bool changed=false;
        foreach(var kv in _db.Days.ToArray())
        {
            if(!DateTime.TryParseExact(kv.Key,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var originalDate) || originalDate.Date>=target.Date) continue;
            foreach(var source in kv.Value)
            {
                if(source.SourceID!="" || source.Repeat is not ("daily" or "weekdays" or "weekly")) continue;
                bool due=source.Repeat=="daily" || source.Repeat=="weekdays" && target.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || source.Repeat=="weekly" && target.DayOfWeek==originalDate.DayOfWeek;
                string newID=$"rec:{source.ID}:{key}";
                if (!due || !have.Add(newID)) continue;
                // Use serialization for a deep copy without accidentally sharing a timer object.
                var copy=JsonSerializer.Deserialize<TaskItem>(JsonSerializer.Serialize(source,JsonOptions),JsonOptions)!;
                copy.ID=newID;copy.SourceID=source.ID;copy.Done=false;copy.DoneAt="";copy.Timer=new();
                _db.Days[key].Add(copy);changed=true;
            }
        }
        if (Settings.AutoCarry && target.Date==DateTime.Today && _db.Days.TryGetValue(target.AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),out var previous))
        {
            foreach(var task in previous.Where(x=>!x.Done))
            {
                var clonedID=$"carry:{task.ID}:{key}";
                if(have.Contains(clonedID)) continue;
                if(TaskTimerEngine.IsActive(task)) {TaskTimerEngine.Pause(task);changed=true;}
                var clone=JsonSerializer.Deserialize<TaskItem>(JsonSerializer.Serialize(task,JsonOptions),JsonOptions)!;
                clone.ID=clonedID;clone.SourceID=task.ID;clone.Repeat="once";
                _db.Days[key].Add(clone);have.Add(clonedID);changed=true;
            }
        }
        if (changed) Save();
    }
    public (int done,int total,long elapsed) GetStatistics(DateTime day)
    {
        var tasks=GetTasks(day);
        return (tasks.Count(x=>x.Done),tasks.Count,tasks.Sum(TaskTimerEngine.Elapsed));
    }
}
