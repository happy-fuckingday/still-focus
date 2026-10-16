using System.Text.Json;

namespace StillFocus.Core;

public sealed class DayEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "重要日期";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool Enabled { get; set; } = true;
    public string Label(DateOnly today)
    {
        int days = Date.DayNumber - today.DayNumber;
        return $"{Name} · " + (days > 0 ? $"D-{days}" : days == 0 ? "今天" : $"已过 {-days} 天");
    }
    public override string ToString() => $"{(Enabled ? "" : "[停用] ")}{Name}\n{Date:yyyy-MM-dd}";
}

public sealed class Settings
{
    public int Minutes { get; set; } = 25;
    public int Count { get; set; }
    public bool ShowDays { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool Shake { get; set; } = true;
    public bool Toast { get; set; }
    public bool AutoHide { get; set; } = true;
    public bool Startup { get; set; }
    public double Opacity { get; set; } = .94;
    // Physical screen coordinates, never paths or account identifiers.
    public int? X { get; set; }
    public int? Y { get; set; }
    public string Dock { get; set; } = "";
    public Guid? Selected { get; set; }
    public List<DayEvent> Events { get; set; } = [];
    public DayEvent? Current => Events.FirstOrDefault(e => e.Enabled && e.Id == Selected) ?? Events.FirstOrDefault(e => e.Enabled);

    public void Normalize()
    {
        Minutes = Math.Clamp(Minutes, 1, 99);
        Count = Math.Clamp(Count, 0, 99);
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .4, 1) : .94;
        if (Dock is not ("Left" or "Right" or "Top" or "Bottom")) Dock = "";
        Events ??= [];
        Events = Events.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name)).ToList();
        foreach (var e in Events) e.Name = e.Name.Trim()[..Math.Min(e.Name.Trim().Length, 100)];
        Selected = Current?.Id;
    }
}

public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    public string FilePath => Path.Combine(directory, "settings.json");
    public bool Recovered { get; private set; }
    public Settings Load()
    {
        foreach (string path in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? throw new JsonException();
                settings.Normalize();
                Recovered = path.EndsWith(".bak");
                return settings;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            { Recovered = true; }
        }
        return new Settings();
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(directory);
        string temp = FilePath + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        // After recovery preserve the known-good backup instead of rotating a damaged primary over it.
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, Recovered ? null : FilePath + ".bak");
        else File.Move(temp, FilePath);
        Recovered = false;
    }
}
