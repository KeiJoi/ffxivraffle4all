using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVRaffle4All.Models;
using FFXIVRaffle4All.Services;

namespace FFXIVRaffle4All.Windows;

public sealed class RaffleWindow : Window
{
    private readonly RaffleManager raffleManager;
    private readonly BackendClient backendClient;
    private readonly PluginConfiguration configuration;
    private readonly ITargetManager targetManager;
    private readonly XlsxExporter xlsxExporter = new();
    private readonly string configDirectory;

    private string newRaffleName = string.Empty;
    private string renameRaffleName = string.Empty;
    private string nameInput = string.Empty;
    private int paidTicketsInput = 1;
    private int freeTicketsInput = 1;
    private string backendUrlInput;
    private string statusMessage = string.Empty;
    private bool isBusy;

    public RaffleWindow(
        RaffleManager raffleManager,
        BackendClient backendClient,
        PluginConfiguration configuration,
        ITargetManager targetManager,
        string configDirectory)
        : base("FFXIV Raffle 4 All", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.raffleManager = raffleManager;
        this.backendClient = backendClient;
        this.configuration = configuration;
        this.targetManager = targetManager;
        this.configDirectory = configDirectory;
        backendUrlInput = configuration.BackendBaseUrl;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 520),
        };
    }

    public override void Draw()
    {
        var raffle = raffleManager.CurrentRaffle;
        if (raffle == null)
        {
            ImGui.TextWrapped("Create a raffle to get started.");
            DrawRaffleCreationOnly();
            return;
        }

        var available = ImGui.GetContentRegionAvail();
        var leftWidth = MathF.Max(320f, available.X * 0.45f);

        ImGui.BeginChild("LeftPanel", new Vector2(leftWidth, 0), true);
        DrawLeftPanel(raffle);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("RightPanel", new Vector2(0, 0), true);
        DrawRightPanel(raffle);
        ImGui.EndChild();
    }

    private void DrawRaffleCreationOnly()
    {
        ImGui.InputText("New raffle name", ref newRaffleName, 64);
        if (ImGui.Button("Create raffle"))
        {
            raffleManager.CreateRaffle(newRaffleName);
            newRaffleName = string.Empty;
        }
    }

    private void DrawLeftPanel(Raffle raffle)
    {
        DrawRaffleSelection(raffle);
        ImGui.Separator();

        DrawSettings(raffle);
        ImGui.Separator();

        DrawTicketEntry(raffle);
        ImGui.Separator();

        DrawBackendSection(raffle);
        ImGui.Separator();

        DrawExportSection(raffle);

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(statusMessage);
        }
    }

    private void DrawRaffleSelection(Raffle raffle)
    {
        ImGui.Text("Raffle");
        var preview = $"{raffle.Name} ({raffle.CreatedAt.ToLocalTime():g})";
        if (ImGui.BeginCombo("##raffleSelect", preview))
        {
            foreach (var entry in raffleManager.Raffles)
            {
                var label = $"{entry.Name} ({entry.CreatedAt.ToLocalTime():g})";
                var isSelected = entry.Id == raffle.Id;
                if (ImGui.Selectable(label, isSelected))
                {
                    raffleManager.SelectById(entry.Id);
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.InputText("New raffle name", ref newRaffleName, 64);
        if (ImGui.Button("Create new raffle"))
        {
            raffleManager.CreateRaffle(newRaffleName);
            newRaffleName = string.Empty;
        }

        ImGui.InputText("Rename current", ref renameRaffleName, 64);
        if (ImGui.Button("Apply name"))
        {
            raffleManager.RenameCurrent(renameRaffleName);
        }

        if (ImGui.Button("Reset current raffle"))
        {
            raffleManager.ResetCurrent();
        }
    }

    private void DrawSettings(Raffle raffle)
    {
        ImGui.Text("Settings");

        var startingPot = raffle.Settings.StartingPot;
        if (ImGui.InputFloat("Starting pot", ref startingPot, 1f, 10f, "%.2f"))
        {
            raffle.Settings.StartingPot = MathF.Max(0f, startingPot);
            raffleManager.Save();
        }

        var ticketCost = raffle.Settings.TicketCost;
        if (ImGui.InputFloat("Ticket cost", ref ticketCost, 0.1f, 1f, "%.2f"))
        {
            raffle.Settings.TicketCost = MathF.Max(0f, ticketCost);
            raffleManager.Save();
        }

        var prizePercentage = raffle.Settings.PrizePercentage;
        if (ImGui.InputFloat("Prize %", ref prizePercentage, 1f, 5f, "%.2f"))
        {
            raffle.Settings.PrizePercentage = Math.Clamp(prizePercentage, 0f, 100f);
            raffleManager.Save();
        }

        ImGui.Spacing();
        ImGui.Text("Free ticket rule");
        var paidForFree = raffle.Settings.PaidTicketsForFree;
        var freePerBlock = raffle.Settings.FreeTicketsPerBlock;
        if (ImGui.InputInt("Paid tickets per bonus", ref paidForFree))
        {
            raffle.Settings.PaidTicketsForFree = Math.Max(0, paidForFree);
            raffleManager.Save();
        }

        if (ImGui.InputInt("Free tickets per bonus", ref freePerBlock))
        {
            raffle.Settings.FreeTicketsPerBlock = Math.Max(0, freePerBlock);
            raffleManager.Save();
        }

        ImGui.Spacing();
        ImGui.Text($"Paid tickets: {raffle.TotalPaidTickets}");
        ImGui.Text($"Free tickets: {raffle.TotalFreeTickets}");
        ImGui.Text($"Total tickets: {raffle.TotalTickets}");
        ImGui.Text($"Running pot: {raffle.RunningPot:0.00}");
        ImGui.Text($"Prize pot: {raffle.PrizePot:0.00}");
    }

    private void DrawTicketEntry(Raffle raffle)
    {
        ImGui.Text("Add tickets");
        ImGui.InputText("Name", ref nameInput, 64);
        ImGui.SameLine();
        if (ImGui.Button("Use target"))
        {
            nameInput = GetTargetName();
        }

        if (paidTicketsInput < 1)
        {
            paidTicketsInput = 1;
        }

        ImGui.InputInt("Paid tickets", ref paidTicketsInput);
        paidTicketsInput = Math.Max(1, paidTicketsInput);
        var freePreview = RaffleManager.CalculateFreeTickets(raffle.Settings, Math.Max(0, paidTicketsInput));
        if (freePreview > 0)
        {
            ImGui.Text($"Adds {freePreview} free tickets based on rule.");
        }

        if (ImGui.Button("Add paid tickets"))
        {
            raffleManager.AddPaidTickets(nameInput, paidTicketsInput);
        }

        ImGui.InputInt("Free tickets", ref freeTicketsInput);
        freeTicketsInput = Math.Max(0, freeTicketsInput);
        if (ImGui.Button("Add free tickets"))
        {
            raffleManager.AddFreeTickets(nameInput, Math.Max(0, freeTicketsInput));
        }
    }

    private void DrawBackendSection(Raffle raffle)
    {
        ImGui.Text("Backend");
        ImGui.InputText("Backend URL", ref backendUrlInput, 256);
        if (ImGui.Button("Save server address"))
        {
            configuration.BackendBaseUrl = backendUrlInput.Trim();
            configuration.Save();
            statusMessage = "Server address saved.";
        }

        if (ImGui.Button("Generate host link"))
        {
            _ = GenerateLinksAsync(raffle);
        }

        ImGui.SameLine();
        if (ImGui.Button("Generate viewer link"))
        {
            _ = GenerateLinksAsync(raffle);
        }

        DrawLinkRow("Host link", raffle.HostUrl);
        DrawLinkRow("Viewer link", raffle.ViewerUrl);

        if (ImGui.Button("Fetch winner from backend"))
        {
            _ = FetchWinnerAsync(raffle);
        }

        var winnerName = raffle.WinnerName ?? string.Empty;
        if (ImGui.InputText("Winner", ref winnerName, 64))
        {
            raffle.WinnerName = winnerName;
            raffleManager.Save();
        }
    }

    private void DrawExportSection(Raffle raffle)
    {
        ImGui.Text("Export");
        if (ImGui.Button("Export raffle to XLSX"))
        {
            var path = xlsxExporter.Export(raffle, configDirectory);
            statusMessage = $"Exported XLSX to {path}";
        }
    }

    private void DrawLinkRow(string label, string? value)
    {
        var display = value ?? string.Empty;
        ImGui.InputText(label, ref display, 512, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Copy##{label}"))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ImGui.SetClipboardText(value);
                statusMessage = $"Copied {label} to clipboard.";
            }
        }
    }

    private async Task GenerateLinksAsync(Raffle raffle)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        statusMessage = "Generating links...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await backendClient.GenerateLinksAsync(raffle, cts.Token);
            if (!result.Success)
            {
                statusMessage = result.Error ?? "Link generation failed.";
                return;
            }

            raffle.HostUrl = result.HostUrl;
            raffle.ViewerUrl = result.ViewerUrl;
            raffle.ExternalId = result.RaffleId ?? raffle.ExternalId;
            if (!string.IsNullOrWhiteSpace(result.WinnerName))
            {
                raffle.WinnerName = result.WinnerName;
            }

            raffleManager.Save();
            statusMessage = "Host and viewer links generated.";
        }
        catch (Exception ex)
        {
            statusMessage = $"Link generation error: {ex.Message}";
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task FetchWinnerAsync(Raffle raffle)
    {
        if (isBusy)
        {
            return;
        }

        string? externalId = null;
        string? token = null;
        if (string.IsNullOrWhiteSpace(raffle.ExternalId))
        {
            if (!TryExtractRaffleId(raffle, out externalId, out token))
            {
                statusMessage = "No backend raffle ID available. Generate links first.";
                return;
            }
        }

        isBusy = true;
        statusMessage = "Fetching winner...";
        try
        {
            var raffleId = raffle.ExternalId ?? externalId ?? string.Empty;
            var tokenToUse = token ?? string.Empty;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await backendClient.FetchRaffleAsync(raffleId, tokenToUse, cts.Token);
            if (!result.Success || result.Payload == null)
            {
                statusMessage = result.Error ?? "Failed to fetch raffle state.";
                return;
            }

            raffle.WinnerName = result.Payload.WinnerName ?? raffle.WinnerName;
            raffleManager.Save();
            statusMessage = "Winner updated.";
        }
        catch (Exception ex)
        {
            statusMessage = $"Fetch error: {ex.Message}";
        }
        finally
        {
            isBusy = false;
        }
    }

    private bool TryExtractRaffleId(Raffle raffle, out string? raffleId, out string? token)
    {
        raffleId = null;
        token = null;
        if (TryParseLink(raffle.HostUrl, out raffleId, out token))
        {
            return true;
        }

        if (TryParseLink(raffle.ViewerUrl, out raffleId, out token))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseLink(string? url, out string? raffleId, out string? token)
    {
        raffleId = null;
        token = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        raffleId = segments[1];
        token = segments[2];
        return true;
    }

    private string GetTargetName()
    {
        var target = targetManager.Target;
        if (target == null)
        {
            statusMessage = "No target selected.";
            return string.Empty;
        }

        var rawName = target.Name.TextValue;
        return RaffleManager.NormalizeName(rawName);
    }

    private void DrawRightPanel(Raffle raffle)
    {
        ImGui.Text("Participants");
        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        var size = new Vector2(0, ImGui.GetContentRegionAvail().Y);
        if (ImGui.BeginTable("ParticipantsTable", 4, tableFlags, size))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Paid", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Free", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Adjust", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGui.TableHeadersRow();

            var participants = raffle.Participants.ToArray();
            foreach (var participant in participants)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(participant.Name);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(participant.PaidTickets.ToString(CultureInfo.InvariantCulture));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(participant.FreeTickets.ToString(CultureInfo.InvariantCulture));

                ImGui.TableNextColumn();
                ImGui.PushID(participant.Name);
                if (ImGui.SmallButton("+Paid"))
                {
                    raffleManager.AdjustTickets(participant.Name, 1, 0);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("-Paid"))
                {
                    raffleManager.AdjustTickets(participant.Name, -1, 0);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("+Free"))
                {
                    raffleManager.AdjustTickets(participant.Name, 0, 1);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("-Free"))
                {
                    raffleManager.AdjustTickets(participant.Name, 0, -1);
                }
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (raffle.Participants.Count == 0)
        {
            ImGui.TextWrapped("No tickets yet. Add a name on the left to begin.");
        }
    }
}
