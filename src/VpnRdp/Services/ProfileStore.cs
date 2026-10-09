using System.Text.Json;
using System.Text.Json.Serialization;
using VpnRdp.Models;

namespace VpnRdp.Services;

/// <summary>Loads/saves profiles.json. Never contains secrets — those live in Credential Manager.</summary>
public sealed class ProfileStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public ProfileStore(string? path = null) => _path = path ?? AppPaths.ProfilesFile;

    public List<Profile> Load()
    {
        if (!File.Exists(_path))
            return new List<Profile>();
        try
        {
            return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(_path), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read {_path}; backing it up and starting fresh", ex);
            File.Copy(_path, _path + ".bad", overwrite: true);
            return new List<Profile>();
        }
    }

    public void Save(IEnumerable<Profile> profiles)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profiles.ToList(), JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
