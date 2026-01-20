using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVRaffle4All.Models;

namespace FFXIVRaffle4All.Services;

public sealed class RaffleManager
{
    private readonly List<Raffle> raffles;
    private readonly RaffleRepository repository;
    private readonly PluginConfiguration configuration;

    public RaffleManager(List<Raffle> raffles, RaffleRepository repository, PluginConfiguration configuration)
    {
        this.raffles = raffles;
        this.repository = repository;
        this.configuration = configuration;
        CurrentRaffle = raffles.FirstOrDefault(r => r.Id == configuration.LastSelectedRaffleId) ?? raffles.FirstOrDefault();
    }

    public IReadOnlyList<Raffle> Raffles => raffles;
    public Raffle? CurrentRaffle { get; private set; }

    public Raffle CreateRaffle(string? name)
    {
        var raffle = new Raffle
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? DefaultRaffleName() : name.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        raffles.Insert(0, raffle);
        SetCurrent(raffle);
        Save();
        return raffle;
    }

    public void ImportRaffle(Raffle raffle)
    {
        raffle.Id = Guid.NewGuid().ToString("N");
        raffle.CreatedAt = raffle.CreatedAt == default ? DateTime.UtcNow : raffle.CreatedAt;
        if (string.IsNullOrWhiteSpace(raffle.Name))
        {
            raffle.Name = DefaultRaffleName();
        }
        raffle.Participants ??= new List<RaffleParticipant>();
        raffle.Settings ??= new RaffleSettings();
        raffle.HostUrl = null;
        raffle.ViewerUrl = null;
        raffle.ExternalId = null;
        raffles.Insert(0, raffle);
        SetCurrent(raffle);
        Save();
    }

    public void SetCurrent(Raffle raffle)
    {
        CurrentRaffle = raffle;
        configuration.LastSelectedRaffleId = raffle.Id;
        configuration.Save();
    }

    public void SelectById(string id)
    {
        var raffle = raffles.FirstOrDefault(r => r.Id == id);
        if (raffle == null)
        {
            return;
        }

        SetCurrent(raffle);
    }

    public void RenameCurrent(string name)
    {
        if (CurrentRaffle == null)
        {
            return;
        }

        CurrentRaffle.Name = string.IsNullOrWhiteSpace(name) ? CurrentRaffle.Name : name.Trim();
        Save();
    }

    public void ResetCurrent()
    {
        if (CurrentRaffle == null)
        {
            return;
        }

        CurrentRaffle.Participants.Clear();
        CurrentRaffle.WinnerName = null;
        CurrentRaffle.HostUrl = null;
        CurrentRaffle.ViewerUrl = null;
        Save();
    }

    public void AddPaidTickets(string rawName, int paidTickets)
    {
        if (CurrentRaffle == null || paidTickets <= 0)
        {
            return;
        }

        var name = NormalizeName(rawName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var participant = FindOrCreateParticipant(name);
        participant.PaidTickets += paidTickets;
        var freeTickets = CalculateFreeTickets(CurrentRaffle.Settings, paidTickets);
        participant.FreeTickets += freeTickets;
        CleanupParticipant(participant);
        Save();
    }

    public void AddFreeTickets(string rawName, int freeTickets)
    {
        if (CurrentRaffle == null || freeTickets <= 0)
        {
            return;
        }

        var name = NormalizeName(rawName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var participant = FindOrCreateParticipant(name);
        participant.FreeTickets += freeTickets;
        CleanupParticipant(participant);
        Save();
    }

    public void AdjustTickets(string rawName, int paidDelta, int freeDelta)
    {
        if (CurrentRaffle == null)
        {
            return;
        }

        var name = NormalizeName(rawName);
        var participant = FindParticipant(name);
        if (participant == null)
        {
            return;
        }

        participant.PaidTickets = Math.Max(0, participant.PaidTickets + paidDelta);
        participant.FreeTickets = Math.Max(0, participant.FreeTickets + freeDelta);
        CleanupParticipant(participant);
        Save();
    }

    public void Save()
    {
        repository.Save(raffles);
    }

    public static string NormalizeName(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 0)
        {
            trimmed = trimmed.Substring(0, atIndex);
        }

        return trimmed;
    }

    public static int CalculateFreeTickets(RaffleSettings settings, int paidTickets)
    {
        if (settings.PaidTicketsForFree <= 0 || settings.FreeTicketsPerBlock <= 0)
        {
            return 0;
        }

        var blocks = paidTickets / settings.PaidTicketsForFree;
        return blocks * settings.FreeTicketsPerBlock;
    }

    private RaffleParticipant FindOrCreateParticipant(string name)
    {
        var participant = FindParticipant(name);
        if (participant != null)
        {
            return participant;
        }

        participant = new RaffleParticipant { Name = name };
        CurrentRaffle!.Participants.Add(participant);
        return participant;
    }

    private RaffleParticipant? FindParticipant(string name)
    {
        return CurrentRaffle?.Participants.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void CleanupParticipant(RaffleParticipant participant)
    {
        if (participant.PaidTickets <= 0 && participant.FreeTickets <= 0)
        {
            CurrentRaffle!.Participants.Remove(participant);
        }
    }

    private static string DefaultRaffleName()
    {
        return $"Raffle {DateTime.Now:yyyy-MM-dd HHmm}";
    }
}
