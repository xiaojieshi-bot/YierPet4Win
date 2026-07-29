using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace YierPet;

public sealed class SpriteSheet
{
    public const int Columns = 8;
    public const int CellWidth = 192;
    public const int CellHeight = 208;

    private readonly Dictionary<PetState, BitmapSource[]> _frames = new();

    public static SpriteSheet? Load()
    {
        var path = LocateSheet();
        if (path == null) return null;
        try
        {
            using var image = BitmapUtil.LoadImage(path);
            return new SpriteSheet(image);
        }
        catch
        {
            return null;
        }
    }

    private SpriteSheet(Image<Rgba32> image)
    {
        var scaleX = (double)image.Width / (Columns * CellWidth);
        var scaleY = (double)image.Height / (9 * CellHeight);

        foreach (var state in PetStateExtensions.All)
        {
            var list = new List<BitmapSource>();
            var durations = state.DurationsMs();
            for (var col = 0; col < durations.Length; col++)
            {
                var x = (int)(col * CellWidth * scaleX);
                var y = (int)(state.Row() * CellHeight * scaleY);
                var w = (int)(CellWidth * scaleX);
                var h = (int)(CellHeight * scaleY);
                using var frame = image.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));
                list.Add(BitmapUtil.ToBitmapSource(frame));
            }
            _frames[state] = list.ToArray();
        }
    }

    public BitmapSource[] FramesFor(PetState state) =>
        _frames.TryGetValue(state, out var f) ? f : [];

    private static string? LocateSheet()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "spritesheet.webp"),
            Path.Combine(baseDir, "spritesheet.webp"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}

public enum PetStyle
{
    Classic,
    Yier,
    Bubu,
    Duo,
}

public static class PetStyleExtensions
{
    public static string DisplayName(this PetStyle style) => style switch
    {
        PetStyle.Classic => "经典一二",
        PetStyle.Yier => "活力一二",
        PetStyle.Bubu => "活力布布",
        PetStyle.Duo => "一二布布",
        _ => style.ToString(),
    };

    public static bool IsSticker(this PetStyle style) => style != PetStyle.Classic;

    public static string Raw(this PetStyle style) => style switch
    {
        PetStyle.Classic => "classic",
        PetStyle.Yier => "yier",
        PetStyle.Bubu => "bubu",
        PetStyle.Duo => "duo",
        _ => "classic",
    };

    public static PetStyle? FromRaw(string? raw) => raw switch
    {
        "classic" => PetStyle.Classic,
        "yier" => PetStyle.Yier,
        "bubu" => PetStyle.Bubu,
        "duo" => PetStyle.Duo,
        _ => null,
    };

    public static PetStyle[] All { get; } =
        [PetStyle.Classic, PetStyle.Yier, PetStyle.Bubu, PetStyle.Duo];
}

public sealed class Sticker
{
    public required string Id { get; init; }
    public required double Fps { get; init; }
    public required int FrameCount { get; init; }
    public required string[] Tags { get; init; }
    public required string Directory { get; init; }

    public BitmapSource[] LoadFrames()
    {
        var frames = new List<BitmapSource>();
        for (var i = 0; i < FrameCount; i++)
        {
            var path = Path.Combine(Directory, $"frame_{i:D3}.png");
            if (!File.Exists(path)) continue;
            try
            {
                using var img = BitmapUtil.LoadImage(path);
                frames.Add(BitmapUtil.ToBitmapSource(img));
            }
            catch
            {
                // skip bad frame
            }
        }
        return frames.ToArray();
    }
}

public sealed class PackLibrary
{
    private readonly Sticker[] _stickers;

    private sealed class Meta
    {
        public string pack { get; set; } = "";
        public List<MetaEntry> stickers { get; set; } = [];
    }

    private sealed class MetaEntry
    {
        public string id { get; set; } = "";
        public double fps { get; set; }
        public int frames { get; set; }
        public string[] tags { get; set; } = [];
    }

    public PackLibrary(PetStyle style)
    {
        if (!style.IsSticker()) throw new ArgumentException("Not a sticker style");
        var packDir = LocatePackDir(style);
        if (packDir == null)
        {
            _stickers = [];
            return;
        }

        var metaPath = Path.Combine(packDir, "meta.json");
        if (!File.Exists(metaPath))
        {
            _stickers = [];
            return;
        }

        var json = File.ReadAllText(metaPath);
        var meta = JsonSerializer.Deserialize<Meta>(json);
        if (meta?.stickers == null || meta.stickers.Count == 0)
        {
            _stickers = [];
            return;
        }

        _stickers = meta.stickers.Select(e => new Sticker
        {
            Id = e.id,
            Fps = e.fps,
            FrameCount = e.frames,
            Tags = e.tags,
            Directory = Path.Combine(packDir, e.id),
        }).ToArray();
    }

    public bool IsValid => _stickers.Length > 0;

    public Sticker? RandomStickerAnyOf(IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            var pool = _stickers.Where(s => s.Tags.Contains(tag)).ToArray();
            if (pool.Length > 0)
                return pool[Random.Shared.Next(pool.Length)];
        }
        return _stickers.Length > 0
            ? _stickers[Random.Shared.Next(_stickers.Length)]
            : null;
    }

    private static string? LocatePackDir(PetStyle style)
    {
        var baseDir = AppContext.BaseDirectory;
        var sub = style.Raw();
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "Packs", sub),
            Path.Combine(baseDir, "Packs", sub),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full)) return full;
        }
        return null;
    }
}

public static class UserSettings
{
    private static readonly string PathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YierPetWin", "settings.json");

    private sealed class Store : Dictionary<string, string> { }

    private static Store LoadStore()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var json = File.ReadAllText(PathFile);
                return JsonSerializer.Deserialize<Store>(json) ?? new Store();
            }
        }
        catch { }
        return new Store();
    }

    private static void SaveStore(Store store)
    {
        var dir = Path.GetDirectoryName(PathFile)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(PathFile, JsonSerializer.Serialize(store));
    }

    public static PetStyle SavedStyle
    {
        get
        {
            var s = LoadStore();
            return PetStyleExtensions.FromRaw(
                s.GetValueOrDefault("pet.style")) ?? PetStyle.Classic;
        }
        set
        {
            var s = LoadStore();
            s["pet.style"] = value.Raw();
            SaveStore(s);
        }
    }

    public static bool ReminderEnabled(ReminderKind kind)
    {
        var key = kind.DefaultsKey();
        var s = LoadStore();
        if (!s.ContainsKey(key)) return true;
        return s[key] == "1";
    }

    public static void SetReminderEnabled(ReminderKind kind, bool enabled)
    {
        var s = LoadStore();
        s[kind.DefaultsKey()] = enabled ? "1" : "0";
        SaveStore(s);
    }
}
