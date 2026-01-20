using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXIVRaffle4All.Models;

namespace FFXIVRaffle4All.Services;

public sealed class BackendClient
{
    private readonly PluginConfiguration configuration;
    private readonly HttpClient httpClient = new();
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public BackendClient(PluginConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public async Task<BackendLinksResult> GenerateLinksAsync(Raffle raffle, CancellationToken cancellationToken)
    {
        var baseUri = GetBaseUri();
        if (baseUri == null)
        {
            return BackendLinksResult.Failed("Backend base URL is not set.");
        }

        var request = BuildCreateRequest(raffle);
        var endpoint = new Uri(baseUri, "/api/raffles");
        using var response = await httpClient.PostAsJsonAsync(endpoint, request, jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return BackendLinksResult.Failed($"Backend error: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var payload = await response.Content.ReadFromJsonAsync<RaffleCreateResponse>(jsonOptions, cancellationToken);
        if (payload == null || string.IsNullOrWhiteSpace(payload.HostUrl) || string.IsNullOrWhiteSpace(payload.ViewerUrl))
        {
            return BackendLinksResult.Failed("Backend response missing host/viewer URLs.");
        }

        return new BackendLinksResult(true, payload.HostUrl, payload.ViewerUrl, payload.RaffleId, payload.WinnerName, null);
    }

    public async Task<BackendRaffleResult> FetchRaffleAsync(string raffleId, string? token, CancellationToken cancellationToken)
    {
        var baseUri = GetBaseUri();
        if (baseUri == null)
        {
            return BackendRaffleResult.Failed("Backend base URL is not set.");
        }

        var endpoint = new Uri(baseUri, $"/api/raffles/{raffleId}?token={Uri.EscapeDataString(token ?? string.Empty)}");
        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return BackendRaffleResult.Failed($"Backend error: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var payload = await response.Content.ReadFromJsonAsync<RaffleStateResponse>(jsonOptions, cancellationToken);
        if (payload == null)
        {
            return BackendRaffleResult.Failed("Backend response missing raffle state.");
        }

        return new BackendRaffleResult(true, payload, null);
    }

    private Uri? GetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(configuration.BackendBaseUrl))
        {
            return null;
        }

        var raw = configuration.BackendBaseUrl.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var baseUri))
        {
            return baseUri;
        }

        if (Uri.TryCreate($"https://{raw}", UriKind.Absolute, out baseUri))
        {
            return baseUri;
        }

        return null;
    }

    private static RaffleCreateRequest BuildCreateRequest(Raffle raffle)
    {
        var tickets = new List<string>();
        foreach (var participant in raffle.Participants)
        {
            for (var i = 0; i < participant.PaidTickets + participant.FreeTickets; i++)
            {
                tickets.Add(participant.Name);
            }
        }

        return new RaffleCreateRequest
        {
            RaffleId = raffle.ExternalId ?? raffle.Id,
            Name = raffle.Name,
            CreatedAt = raffle.CreatedAt,
            Settings = raffle.Settings,
            Participants = raffle.Participants,
            Tickets = tickets,
        };
    }
}

public sealed record BackendLinksResult(bool Success, string? HostUrl, string? ViewerUrl, string? RaffleId, string? WinnerName, string? Error)
{
    public static BackendLinksResult Failed(string error) => new(false, null, null, null, null, error);
}

public sealed record BackendRaffleResult(bool Success, RaffleStateResponse? Payload, string? Error)
{
    public static BackendRaffleResult Failed(string error) => new(false, null, error);
}

public sealed class RaffleCreateRequest
{
    public string? RaffleId { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public RaffleSettings? Settings { get; set; }
    public List<RaffleParticipant>? Participants { get; set; }
    public List<string>? Tickets { get; set; }
}

public sealed class RaffleCreateResponse
{
    public string? RaffleId { get; set; }
    public string? HostUrl { get; set; }
    public string? ViewerUrl { get; set; }
    public string? WinnerName { get; set; }
}

public sealed class RaffleStateResponse
{
    public string? RaffleId { get; set; }
    public string? Name { get; set; }
    public string? WinnerName { get; set; }
}
