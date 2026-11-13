using System;
using System.IO;
using ClosedXML.Excel;

namespace Teeth
{
    /// <summary>
    /// One row worth of data for the Excel log: a snapshot of where the
    /// live counts stood right before they were wiped, plus why.
    /// </summary>
    public class ShiftLogEntry
    {
        public DateTime Timestamp { get; set; }
        public DateTime ShiftDate { get; set; }
        public string Line { get; set; }
        public string Shift { get; set; }     // "Day" / "Night"
        public string Reason { get; set; }    // "Shift Rollover" / "Manual Reset" / "App Closed Mid-Shift"
        public int PlanIn { get; set; }
        public int PlanOut { get; set; }
        public int TargetIn { get; set; }
        public int TargetOut { get; set; }
        public int ResultIn { get; set; }
        public int ResultOut { get; set; }
    }

    /// <summary>
    /// Phase 6: appends rows to a single, ever-growing Excel workbook -
    /// "Log Data\Teeth_Log.xlsx" - replacing the daily logging the old
    /// Python script presumably did some other way. There is deliberately
    /// no in-memory cache: every AppendEntry() opens the file (or creates
    /// it), adds one row, and saves immediately, so the workbook on disk is
    /// always the full history and nothing is lost between calls even if
    /// the app crashes right after.
    /// </summary>
    public class ExcelLogger
    {
        private const string SheetName = "Shift Log";

        private static readonly string[] Headers =
        {
            "Timestamp", "Shift Date", "Line", "Shift", "Reason",
            "Plan In", "Plan Out", "Target In", "Target Out",
            "Result In", "Result Out", "Diff In", "Diff Out"
        };

        /// <summary>Full path to Teeth_Log.xlsx, inside the "Log Data"
        /// folder Program.cs already creates on first run.</summary>
        public string LogFilePath { get; }

        public ExcelLogger(string logDirectory)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                throw new ArgumentNullException(nameof(logDirectory));
            }

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            LogFilePath = Path.Combine(logDirectory, "Teeth_Log.xlsx");
        }

        /// <summary>Appends one row to the "Shift Log" sheet, creating the
        /// workbook (and its header row) the first time this is called.</summary>
        public void AppendEntry(ShiftLogEntry entry)
        {
            using (var workbook = OpenOrCreateWorkbook())
            {
                var sheet = workbook.Worksheet(SheetName);
                int nextRow = (sheet.LastRowUsed()?.RowNumber() ?? 1) + 1;

                sheet.Cell(nextRow, 1).Value = entry.Timestamp;
                sheet.Cell(nextRow, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";

                sheet.Cell(nextRow, 2).Value = entry.ShiftDate;
                sheet.Cell(nextRow, 2).Style.DateFormat.Format = "yyyy-mm-dd";

                sheet.Cell(nextRow, 3).Value = entry.Line;
                sheet.Cell(nextRow, 4).Value = entry.Shift;
                sheet.Cell(nextRow, 5).Value = entry.Reason;
                sheet.Cell(nextRow, 6).Value = entry.PlanIn;
                sheet.Cell(nextRow, 7).Value = entry.PlanOut;
                sheet.Cell(nextRow, 8).Value = entry.TargetIn;
                sheet.Cell(nextRow, 9).Value = entry.TargetOut;
                sheet.Cell(nextRow, 10).Value = entry.ResultIn;
                sheet.Cell(nextRow, 11).Value = entry.ResultOut;
                sheet.Cell(nextRow, 12).Value = entry.ResultIn - entry.TargetIn;
                sheet.Cell(nextRow, 13).Value = entry.ResultOut - entry.TargetOut;

                sheet.Columns().AdjustToContents();

                workbook.SaveAs(LogFilePath);
            }
        }

        private XLWorkbook OpenOrCreateWorkbook()
        {
            XLWorkbook workbook = File.Exists(LogFilePath)
                ? new XLWorkbook(LogFilePath)
                : new XLWorkbook();

            if (!workbook.TryGetWorksheet(SheetName, out IXLWorksheet sheet))
            {
                sheet = workbook.Worksheets.Add(SheetName);
                AddHeaderRow(sheet);
            }

            return workbook;
        }

        private static void AddHeaderRow(IXLWorksheet sheet)
        {
            for (int i = 0; i < Headers.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = Headers[i];
            }

            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
        }
    }
}
