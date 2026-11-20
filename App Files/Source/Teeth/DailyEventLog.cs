using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClosedXML.Excel;

namespace Teeth
{
    /// <summary>One IN / OUT / refused-scan event for the daily Excel log.</summary>
    public class DailyEvent
    {
        public DateTime Time;
        public string Line;
        public string Shift;     // "Day" / "Night"
        public string Event;     // "IN", "OUT", "OUT (code not readable)", "REFUSED - DUPLICATE", "REFUSED - UNKNOWN CODE"
        public int Number;       // running Result number after this event; 0 = not counted
        public string Detail;    // source of IN, scanned serial for OUT
    }

    /// <summary>
    /// One Excel file per calendar day: "Log Data\Teeth_Daily_yyyy-MM-dd.xlsx".
    /// Sheet "Events" = one row per IN / OUT / refused scan. Sheet "Summary" =
    /// counts per shift (formulas). Events are queued in memory and written by
    /// Flush() (every few seconds from a background thread, and on close), so
    /// the screen never waits for Excel and a file that is open in Excel only
    /// delays the write - the rows stay queued and are retried.
    /// </summary>
    public class DailyEventLog
    {
        private const string EventsSheet = "Events";
        private const string SummarySheet = "Summary";
        private static readonly string[] Headers = { "Time", "Date", "Line", "Shift", "Event", "Number", "Detail" };

        private readonly string _dir;
        private readonly List<DailyEvent> _queue = new List<DailyEvent>();
        private readonly object _queueLock = new object();
        private readonly object _fileLock = new object();
        private int _flushing;

        public DailyEventLog(string logDirectory)
        {
            _dir = logDirectory;
            try { if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir); } catch { }
        }

        public string PathForDay(DateTime day)
        {
            return Path.Combine(_dir, "Teeth_Daily_" + day.ToString("yyyy-MM-dd") + ".xlsx");
        }

        public void Add(DailyEvent e)
        {
            lock (_queueLock) { _queue.Add(e); }
        }

        /// <summary>Writes the queue in a background thread (no-op if already running).</summary>
        public void FlushAsync()
        {
            lock (_queueLock) { if (_queue.Count == 0) return; }
            if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Flush(); }
                finally { Interlocked.Exchange(ref _flushing, 0); }
            });
        }

        /// <summary>Writes everything queued now. Failed days stay queued for the next try.</summary>
        public void Flush()
        {
            lock (_fileLock)
            {
                List<DailyEvent> batch;
                lock (_queueLock)
                {
                    batch = new List<DailyEvent>(_queue);
                    _queue.Clear();
                }
                if (batch.Count == 0) return;

                var failed = new List<DailyEvent>();
                foreach (var day in batch.GroupBy(x => x.Time.Date))
                {
                    var rows = day.OrderBy(x => x.Time).ToList();
                    try { WriteDay(day.Key, rows); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Daily log failed: " + ex.Message);
                        failed.AddRange(rows);
                    }
                }
                if (failed.Count > 0)
                {
                    lock (_queueLock) { _queue.InsertRange(0, failed); }
                }
            }
        }

        private void WriteDay(DateTime day, List<DailyEvent> rows)
        {
            string path = PathForDay(day);
            using (var wb = File.Exists(path) ? new XLWorkbook(path) : new XLWorkbook())
            {
                IXLWorksheet ws;
                if (!wb.TryGetWorksheet(EventsSheet, out ws))
                {
                    ws = wb.Worksheets.Add(EventsSheet);
                    for (int i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
                    ws.Row(1).Style.Font.Bold = true;
                    ws.SheetView.FreezeRows(1);
                }

                int r = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
                foreach (var e in rows)
                {
                    ws.Cell(r, 1).Value = e.Time;
                    ws.Cell(r, 1).Style.DateFormat.Format = "hh:mm:ss";
                    ws.Cell(r, 2).Value = e.Time.Date;
                    ws.Cell(r, 2).Style.DateFormat.Format = "yyyy-mm-dd";
                    ws.Cell(r, 3).Value = e.Line ?? "";
                    ws.Cell(r, 4).Value = e.Shift ?? "";
                    ws.Cell(r, 5).Value = e.Event ?? "";
                    if (e.Number > 0) ws.Cell(r, 6).Value = e.Number;
                    ws.Cell(r, 7).Value = e.Detail ?? "";
                    r++;
                }
                ws.Columns().AdjustToContents();

                IXLWorksheet sum;
                if (wb.TryGetWorksheet(SummarySheet, out sum)) sum.Delete();
                sum = wb.Worksheets.Add(SummarySheet, 1);
                string[] hdr = { "Shift", "IN counted", "OUT counted", "OUT code not readable", "Refused scans" };
                for (int i = 0; i < hdr.Length; i++) sum.Cell(1, i + 1).Value = hdr[i];
                sum.Row(1).Style.Font.Bold = true;
                string[] shifts = { "Day", "Night" };
                for (int i = 0; i < shifts.Length; i++)
                {
                    int row = i + 2;
                    string s = "$A" + row;
                    sum.Cell(row, 1).Value = shifts[i];
                    sum.Cell(row, 2).FormulaA1 = "COUNTIFS(Events!$D:$D," + s + ",Events!$E:$E,\"IN\")";
                    sum.Cell(row, 3).FormulaA1 = "COUNTIFS(Events!$D:$D," + s + ",Events!$E:$E,\"OUT\")";
                    sum.Cell(row, 4).FormulaA1 = "COUNTIFS(Events!$D:$D," + s + ",Events!$E:$E,\"OUT (code not readable)\")";
                    sum.Cell(row, 5).FormulaA1 = "COUNTIFS(Events!$D:$D," + s + ",Events!$E:$E,\"REFUSED*\")";
                }
                sum.Cell(4, 1).Value = "Total";
                for (int c = 2; c <= 5; c++)
                    sum.Cell(4, c).FormulaA1 = "SUM(" + sum.Cell(2, c).Address + ":" + sum.Cell(3, c).Address + ")";
                sum.Row(4).Style.Font.Bold = true;
                sum.Cell(6, 1).Value = "Day = 08:00-20:00, Night = 20:00-08:00. A night shift that passes midnight is split over two daily files by the clock date.";
                sum.Columns(1, 5).AdjustToContents();

                wb.SaveAs(path);
            }
        }
    }
}
