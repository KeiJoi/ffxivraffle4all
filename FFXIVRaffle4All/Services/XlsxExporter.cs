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
    public string Export(Raffle raffle, string configDirectory)
    {
        var exportDir = Path.Combine(configDirectory, "exports");
        Directory.CreateDirectory(exportDir);

        var fileName = $"raffle_{SanitizeFileName(raffle.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(exportDir, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using (var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

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

            FillSummary(summaryPart.Worksheet.GetFirstChild<SheetData>()!, raffle);
            FillParticipants(participantsPart.Worksheet.GetFirstChild<SheetData>()!, raffle);

            summaryPart.Worksheet.Save();
            participantsPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        return filePath;
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
            CreateTextCell("Prize Pot", true));

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
            CreateNumberCell(raffle.PrizePot.ToString("0.00", CultureInfo.InvariantCulture)));
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
}
