using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FFXIVRaffle4All.Models;

namespace FFXIVRaffle4All.Services;

public sealed class XlsxImporter
{
    public Raffle Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Import path is empty.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("XLSX file not found.", path);
        }

        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook missing.");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

        var settingsSheet = GetSheetByName(workbookPart, "Settings");
        var summarySheet = GetSheetByName(workbookPart, "Summary");
        var participantsSheet = GetSheetByName(workbookPart, "Participants");

        var raffle = new Raffle
        {
            Settings = new RaffleSettings(),
            Participants = new List<RaffleParticipant>(),
        };

        var settingsApplied = false;
        if (settingsSheet != null)
        {
            var settingsData = ReadSheetRows(settingsSheet, sharedStrings);
            if (settingsData.Count >= 2)
            {
                var headerMap = BuildHeaderMap(settingsData[0], sharedStrings);
                var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 1; i < settingsData.Count; i++)
                {
                    var values = BuildRowMap(settingsData[i], headerMap, sharedStrings);
                    var settingName = GetValue(values, "Setting");
                    if (string.IsNullOrWhiteSpace(settingName))
                    {
                        continue;
                    }
                    settings[settingName] = GetValue(values, "Value");
                }

                ApplySettings(settings, raffle);
                settingsApplied = true;
            }
        }

        if (!settingsApplied && summarySheet != null)
        {
            var summaryData = ReadSheetRows(summarySheet, sharedStrings);
            if (summaryData.Count >= 2)
            {
                var headerMap = BuildHeaderMap(summaryData[0], sharedStrings);
                var values = BuildRowMap(summaryData[1], headerMap, sharedStrings);

                raffle.Name = GetValue(values, "Raffle Name");
                raffle.WinnerName = NullIfEmpty(GetValue(values, "Winner"));

                if (DateTime.TryParse(GetValue(values, "Created At"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var created))
                {
                    raffle.CreatedAt = created.ToUniversalTime();
                }

                raffle.Settings.StartingPot = ParseFloat(GetValue(values, "Starting Pot"));
                raffle.Settings.TicketCost = ParseFloat(GetValue(values, "Ticket Cost"));
                raffle.Settings.PrizePercentage = ParseFloat(GetValue(values, "Prize %"));
            }
        }

        if (participantsSheet != null)
        {
            var participantsData = ReadSheetRows(participantsSheet, sharedStrings);
            if (participantsData.Count >= 2)
            {
                var headerMap = BuildHeaderMap(participantsData[0], sharedStrings);
                for (var i = 1; i < participantsData.Count; i++)
                {
                    var values = BuildRowMap(participantsData[i], headerMap, sharedStrings);
                    var name = GetValue(values, "Name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    raffle.Participants.Add(new RaffleParticipant
                    {
                        Name = name,
                        PaidTickets = ParseInt(GetValue(values, "Paid Tickets")),
                        FreeTickets = ParseInt(GetValue(values, "Free Tickets")),
                    });
                }
            }
        }

        return raffle;
    }

    private static void ApplySettings(Dictionary<string, string> settings, Raffle raffle)
    {
        raffle.Name = GetSetting(settings, "Raffle Name");
        raffle.WinnerName = NullIfEmpty(GetSetting(settings, "Winner"));

        var createdAt = GetSetting(settings, "Created At");
        if (DateTime.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var created))
        {
            raffle.CreatedAt = created.ToUniversalTime();
        }

        raffle.Settings.StartingPot = ParseFloat(GetSetting(settings, "Starting Pot"));
        raffle.Settings.TicketCost = ParseFloat(GetSetting(settings, "Ticket Cost"));
        raffle.Settings.PrizePercentage = ParseFloat(GetSetting(settings, "Prize %"));
        raffle.Settings.PaidTicketsForFree = ParseInt(GetSetting(settings, "Paid Tickets per Bonus"));
        raffle.Settings.FreeTicketsPerBlock = ParseInt(GetSetting(settings, "Free Tickets per Bonus"));
    }

    private static WorksheetPart? GetSheetByName(WorkbookPart workbookPart, string name)
    {
        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>()
            .FirstOrDefault(s => string.Equals(s.Name?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
        {
            return null;
        }

        return (WorksheetPart?)workbookPart.GetPartById(sheet.Id!);
    }

    private static List<Row> ReadSheetRows(WorksheetPart worksheetPart, SharedStringTable? sharedStrings)
    {
        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData == null)
        {
            return new List<Row>();
        }

        return sheetData.Elements<Row>().ToList();
    }

    private static Dictionary<int, string> BuildHeaderMap(Row row, SharedStringTable? sharedStrings)
    {
        var map = new Dictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var index = GetColumnIndex(cell.CellReference?.Value);
            var value = GetCellValue(cell, sharedStrings);
            if (index.HasValue && !string.IsNullOrWhiteSpace(value))
            {
                map[index.Value] = value.Trim();
            }
        }
        return map;
    }

    private static Dictionary<string, string> BuildRowMap(Row row, Dictionary<int, string> headerMap, SharedStringTable? sharedStrings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in row.Elements<Cell>())
        {
            var index = GetColumnIndex(cell.CellReference?.Value);
            if (!index.HasValue || !headerMap.TryGetValue(index.Value, out var header))
            {
                continue;
            }
            map[header] = GetCellValue(cell, sharedStrings);
        }
        return map;
    }

    private static int? GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return null;
        }

        var column = new string(cellReference.Where(char.IsLetter).ToArray());
        if (string.IsNullOrEmpty(column))
        {
            return null;
        }

        var index = 0;
        foreach (var ch in column.ToUpperInvariant())
        {
            index *= 26;
            index += ch - 'A' + 1;
        }

        return index;
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        var value = cell.CellValue?.InnerText ?? string.Empty;
        if (cell.DataType == null)
        {
            return value;
        }

        if (cell.DataType.Value == CellValues.SharedString && int.TryParse(value, out var index))
        {
            return sharedStrings?.ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        }

        if (cell.DataType.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        return value;
    }

    private static string GetValue(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string GetSetting(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static float ParseFloat(string value)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0f;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
