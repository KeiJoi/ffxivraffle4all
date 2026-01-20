using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXIVRaffle4All.Models;

public sealed class Raffle
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RaffleSettings Settings { get; set; } = new();
    public List<RaffleParticipant> Participants { get; set; } = new();
    public string? ExternalId { get; set; }
    public string? HostUrl { get; set; }
    public string? ViewerUrl { get; set; }
    public string? WinnerName { get; set; }

    public int TotalPaidTickets => Participants.Sum(p => p.PaidTickets);
    public int TotalFreeTickets => Participants.Sum(p => p.FreeTickets);
    public int TotalTickets => TotalPaidTickets + TotalFreeTickets;
    public float RunningPot => Settings.StartingPot + (Settings.TicketCost * TotalPaidTickets);
    public float PrizePot => RunningPot * (Settings.PrizePercentage / 100f);
}

public sealed class RaffleSettings
{
    public float StartingPot { get; set; } = 0f;
    public float TicketCost { get; set; } = 0f;
    public float PrizePercentage { get; set; } = 100f;
    public int PaidTicketsForFree { get; set; } = 0;
    public int FreeTicketsPerBlock { get; set; } = 0;
}

public sealed class RaffleParticipant
{
    public string Name { get; set; } = string.Empty;
    public int PaidTickets { get; set; } = 0;
    public int FreeTickets { get; set; } = 0;
}
