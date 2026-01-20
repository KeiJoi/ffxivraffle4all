using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FFXIVRaffle4All.Models;

namespace FFXIVRaffle4All.Services;

public sealed class RaffleRepository
{
    private readonly string filePath;
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true,
    };

    public RaffleRepository(string configDirectory)
    {
        filePath = Path.Combine(configDirectory, "raffles.json");
        Directory.CreateDirectory(configDirectory);
    }

    public List<Raffle> Load()
    {
        if (!File.Exists(filePath))
        {
            return new List<Raffle>();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<Raffle>>(json, serializerOptions) ?? new List<Raffle>();
        }
        catch
        {
            return new List<Raffle>();
        }
    }

    public void Save(IReadOnlyCollection<Raffle> raffles)
    {
        var json = JsonSerializer.Serialize(raffles, serializerOptions);
        File.WriteAllText(filePath, json);
    }
}
