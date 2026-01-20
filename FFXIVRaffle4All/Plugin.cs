using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVRaffle4All.Services;
using FFXIVRaffle4All.Windows;

namespace FFXIVRaffle4All;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/raffle";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("FFXIVRaffle4All");
    private readonly PluginConfiguration configuration;
    private readonly RaffleRepository raffleRepository;
    private readonly RaffleManager raffleManager;
    private readonly BackendClient backendClient;
    private readonly RaffleWindow raffleWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        ITargetManager targetManager)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        configuration.Initialize(pluginInterface);

        raffleRepository = new RaffleRepository(pluginInterface.ConfigDirectory.FullName);
        raffleManager = new RaffleManager(raffleRepository.Load(), raffleRepository, configuration);
        backendClient = new BackendClient(configuration);
        raffleWindow = new RaffleWindow(raffleManager, backendClient, configuration, targetManager, pluginInterface.ConfigDirectory.FullName);

        windowSystem.AddWindow(raffleWindow);
        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi += OpenUI;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the FFXIV Raffle 4 All window.",
        });
    }

    public string Name => "FFXIV Raffle 4 All";

    public void Dispose()
    {
        windowSystem.RemoveAllWindows();
        commandManager.RemoveHandler(CommandName);
        pluginInterface.UiBuilder.Draw -= DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenUI;
        raffleManager.Save();
        configuration.Save();
    }

    private void OnCommand(string command, string arguments)
    {
        raffleWindow.IsOpen = true;
    }

    private void DrawUI()
    {
        windowSystem.Draw();
    }

    private void OpenUI()
    {
        raffleWindow.IsOpen = true;
    }
}
