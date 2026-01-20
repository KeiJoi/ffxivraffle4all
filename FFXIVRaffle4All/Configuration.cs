using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FFXIVRaffle4All;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string BackendBaseUrl { get; set; } = string.Empty;
    public string? LastSelectedRaffleId { get; set; }

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
