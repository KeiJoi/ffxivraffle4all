using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FFXIVRaffle4All.Models;

namespace FFXIVRaffle4All.Services;

public sealed class XlsxExporter
{
    public string Export(Raffle raffle, string configDirectory, string? filePath = null)
    {
        var exportDir = Path.Combine(configDirectory, "exports");
        Directory.CreateDirectory(exportDir);

        var resolvedPath = ResolveExportPath(raffle, exportDir, filePath);
        if (File.Exists(resolvedPath))
        {
            File.Delete(resolvedPath);
        }

        using (var document = SpreadsheetDocument.Create(resolvedPath, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

            var settingsPart = workbookPart.AddNewPart<WorksheetPart>();
            settingsPart.Worksheet = new Worksheet(new SheetData());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(settingsPart),
                SheetId = sheetId++,
                Name = "Settings",
            });

            var summaryPart = workbookPart.AddNewPart<WorksheetPart>();
            summaryPart.Worksheet = new Worksheet(new SheetData());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(summaryPart),
                SheetId = sheetId++,
                Name = "Summary",
            });

            var participantsPart = workbookPart.AddNewPart<WorksheetPart>();
            participantsPart.Worksheet = new Worksheet(new SheetData());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(participantsPart),
                SheetId = sheetId++,
                Name = "Participants",
            });

            FillSettings(settingsPart.Worksheet.GetFirstChild<SheetData>()!, raffle);
            FillSummary(summaryPart.Worksheet.GetFirstChild<SheetData>()!, raffle);
            FillParticipants(participantsPart.Worksheet.GetFirstChild<SheetData>()!, raffle);

            settingsPart.Worksheet.Save();
            summaryPart.Worksheet.Save();
            participantsPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        return resolvedPath;
    }

    public string GetDefaultFileName(Raffle raffle)
    {
        return $"raffle_{SanitizeFileName(raffle.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    }

    private static void FillSummary(SheetData sheetData, Raffle raffle)
    {
        AppendRow(sheetData,
            CreateTextCell("Raffle Name", true),
            CreateTextCell("Created At", true),
            CreateTextCell("Winner", true),
            CreateTextCell("Starting Pot", true),
            CreateTextCell("Ticket Cost", true),
            CreateTextCell("Prize %", true),
            CreateTextCell("Paid Tickets", true),
            CreateTextCell("Free Tickets", true),
            CreateTextCell("Total Tickets", true),
            CreateTextCell("Running Pot", true),
            CreateTextCell("Prize Pot", true),
            CreateTextCell("House Take", true));

        AppendRow(sheetData,
            CreateTextCell(raffle.Name),
            CreateTextCell(raffle.CreatedAt.ToLocalTime().ToString("u", CultureInfo.InvariantCulture)),
            CreateTextCell(raffle.WinnerName ?? string.Empty),
            CreateNumberCell(raffle.Settings.StartingPot.ToString("0.00", CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.Settings.TicketCost.ToString("0.00", CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.Settings.PrizePercentage.ToString("0.##", CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.TotalPaidTickets.ToString(CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.TotalFreeTickets.ToString(CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.TotalTickets.ToString(CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.RunningPot.ToString("0.00", CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.PrizePot.ToString("0.00", CultureInfo.InvariantCulture)),
            CreateNumberCell(raffle.HouseTake.ToString("0.00", CultureInfo.InvariantCulture)));
    }

    private static void FillSettings(SheetData sheetData, Raffle raffle)
    {
        AppendRow(sheetData,
            CreateTextCell("Setting", true),
            CreateTextCell("Value", true));

        AppendRow(sheetData,
            CreateTextCell("Raffle Name"),
            CreateTextCell(raffle.Name));

        AppendRow(sheetData,
            CreateTextCell("Created At"),
            CreateTextCell(raffle.CreatedAt.ToLocalTime().ToString("u", CultureInfo.InvariantCulture)));

        AppendRow(sheetData,
            CreateTextCell("Winner"),
            CreateTextCell(raffle.WinnerName ?? string.Empty));

        AppendRow(sheetData,
            CreateTextCell("Starting Pot"),
            CreateNumberCell(raffle.Settings.StartingPot.ToString("0.00", CultureInfo.InvariantCulture)));

        AppendRow(sheetData,
            CreateTextCell("Ticket Cost"),
            CreateNumberCell(raffle.Settings.TicketCost.ToString("0.00", CultureInfo.InvariantCulture)));

        AppendRow(sheetData,
            CreateTextCell("Prize %"),
            CreateNumberCell(raffle.Settings.PrizePercentage.ToString("0.##", CultureInfo.InvariantCulture)));

        AppendRow(sheetData,
            CreateTextCell("Paid Tickets per Bonus"),
            CreateNumberCell(raffle.Settings.PaidTicketsForFree.ToString(CultureInfo.InvariantCulture)));

        AppendRow(sheetData,
            CreateTextCell("Free Tickets per Bonus"),
            CreateNumberCell(raffle.Settings.FreeTicketsPerBlock.ToString(CultureInfo.InvariantCulture)));
    }

    private static void FillParticipants(SheetData sheetData, Raffle raffle)
    {
        AppendRow(sheetData,
            CreateTextCell("Name", true),
            CreateTextCell("Paid Tickets", true),
            CreateTextCell("Free Tickets", true),
            CreateTextCell("Total Tickets", true));

        foreach (var participant in raffle.Participants.OrderByDescending(p => p.PaidTickets + p.FreeTickets))
        {
            AppendRow(sheetData,
                CreateTextCell(participant.Name),
                CreateNumberCell(participant.PaidTickets.ToString(CultureInfo.InvariantCulture)),
                CreateNumberCell(participant.FreeTickets.ToString(CultureInfo.InvariantCulture)),
                CreateNumberCell((participant.PaidTickets + participant.FreeTickets).ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void AppendRow(SheetData sheetData, params Cell[] cells)
    {
        var row = new Row();
        foreach (var cell in cells)
        {
            row.Append(cell);
        }
        sheetData.Append(row);
    }

    private static Cell CreateTextCell(string value, bool isHeader = false)
    {
        var text = new Text(value ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve };
        var inlineString = new InlineString(text);
        return new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = inlineString,
        };
    }

    private static Cell CreateNumberCell(string value)
    {
        return new Cell
        {
            DataType = CellValues.Number,
            CellValue = new CellValue(value),
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "raffle" : cleaned;
    }

    private string ResolveExportPath(Raffle raffle, string exportDir, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Path.Combine(exportDir, GetDefaultFileName(raffle));
        }

        var finalPath = filePath.Trim();
        if (!finalPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            finalPath += ".xlsx";
        }

        var targetDir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        return finalPath;
    }
}
