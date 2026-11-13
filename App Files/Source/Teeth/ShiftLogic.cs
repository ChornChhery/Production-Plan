using System;
using System.Collections.Generic;

namespace Teeth
{
    /// <summary>
    /// Determines which shift is "active right now" and when that shift began.
    /// Direct translation of get_shift() / get_shift_start() from the Python
    /// script - Day runs 08:00-20:00, Night runs 20:00-08:00 (next day).
    /// </summary>
    public static class ShiftCalculator
    {
        public static string GetShift(DateTime now)
        {
            return (now.Hour >= 8 && now.Hour < 20) ? "Day" : "Night";
        }

        /// <summary>
        /// "Frozen time": Target pauses for this many seconds. Done by reading
        /// the Target timeline as if the clock were that many seconds earlier
        /// (never earlier than the shift start). 0 or negative = no effect.
        /// </summary>
        public static DateTime ApplyFrozenTime(DateTime now, DateTime shiftStart, double frozenSeconds)
        {
            if (frozenSeconds <= 0.0)
            {
                return now;
            }

            DateTime effective = now.AddSeconds(-frozenSeconds);
            return effective < shiftStart ? shiftStart : effective;
        }

        public static DateTime GetShiftStart(DateTime now)
        {
            DateTime today = now.Date;

            if (now.Hour >= 8 && now.Hour < 20)
            {
                return today.AddHours(8);
            }

            // Night shift: if it's after midnight but before 08:00, the shift
            // actually started at 20:00 *yesterday*. Otherwise (>= 20:00) it
            // started at 20:00 today.
            DateTime anchorDate = (now.Hour < 8) ? today.AddDays(-1) : today;
            return anchorDate.AddHours(20);
        }
    }

    /// <summary>Shift identity helpers: a shift is identified by its start time.</summary>
    public static class ShiftKeys
    {
        private const string Format = "yyyy-MM-dd HH:mm";

        public static string FromStart(DateTime shiftStart)
        {
            return shiftStart.ToString(Format, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ForTime(DateTime now)
        {
            return FromStart(ShiftCalculator.GetShiftStart(now));
        }

        public static bool TryParse(string key, out DateTime shiftStart)
        {
            return DateTime.TryParseExact(key, Format, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out shiftStart);
        }
    }

    /// <summary>Result of a target/plan calculation for one instant in time.</summary>
    public readonly struct TargetResult
    {
        public int TargetIn { get; }
        public int TargetOut { get; }
        public int PlanIn { get; }
        public int PlanOut { get; }

        public TargetResult(int targetIn, int targetOut, int planIn, int planOut)
        {
            TargetIn = targetIn;
            TargetOut = targetOut;
            PlanIn = planIn;
            PlanOut = planOut;
        }

        public static readonly TargetResult Empty = new TargetResult(0, 0, 0, 0);
    }

    /// <summary>
    /// Phase 4: translation of update_gui()'s timeline math from the Python
    /// script. Places each WorkBlock at its own absolute Start/End time
    /// (measured as an offset from the shift's start, matching the
    /// reference), then accumulates a proportional target: full credit for
    /// a block that has already ended, a proportional fraction for
    /// whichever block is currently in progress, and zero for blocks that
    /// haven't started yet.
    ///
    /// IMPORTANT: a gap between one block's End and the next block's Start
    /// (e.g. Work 1 ends 10:00, Work 2 starts 10:10) is a real, deliberate
    /// break - there is no separate "break" row or flag anywhere in the
    /// data model. During 10:00-10:10 above, "now" falls inside no block at
    /// all, so target accumulation simply holds at whatever Work 1 already
    /// finished at, and only resumes once Work 2's own declared Start time
    /// arrives. This method used to chain blocks purely by summed duration
    /// (ignoring each block's own Start time), which silently ate any gap
    /// and started the next block immediately - that bug is fixed here by
    /// positioning every block from its own Start/End instead of a
    /// running cursor.
    /// </summary>
    public static class TargetCalculator
    {
        public static TargetResult Calculate(DateTime now, DateTime shiftStart, IReadOnlyList<WorkBlockModel> timeline)
        {
            if (timeline == null || timeline.Count == 0)
            {
                return TargetResult.Empty;
            }

            double inAccumulated = 0.0;
            double outAccumulated = 0.0;
            int planIn = 0;
            int planOut = 0;

            int shiftStartMinuteOfDay = shiftStart.Hour * 60 + shiftStart.Minute;

            foreach (var block in timeline)
            {
                int durationMinutes = BlockDurationMinutes(block.StartTime, block.EndTime);

                if (durationMinutes <= 0)
                {
                    continue;
                }

                DateTime blockStart = shiftStart.AddMinutes(MinutesFromShiftStart(block.StartTime, shiftStartMinuteOfDay));
                DateTime blockEnd = blockStart.AddMinutes(durationMinutes);

                double elapsedSeconds;
                if (now >= blockEnd)
                {
                    elapsedSeconds = durationMinutes * 60.0;
                }
                else if (now >= blockStart && now < blockEnd)
                {
                    elapsedSeconds = (now - blockStart).TotalSeconds;
                }
                else
                {
                    elapsedSeconds = 0.0;
                }

                double blockSeconds = durationMinutes * 60.0;
                inAccumulated += (block.TargetIn * elapsedSeconds) / blockSeconds;
                outAccumulated += (block.TargetOut * elapsedSeconds) / blockSeconds;

                // Plan = the shift's full total (every block at 100%), i.e.
                // what Target would read at the very end of the shift.
                planIn += block.TargetIn;
                planOut += block.TargetOut;
            }

            return new TargetResult(
                targetIn: (int)Math.Round(inAccumulated, MidpointRounding.ToEven),
                targetOut: (int)Math.Round(outAccumulated, MidpointRounding.ToEven),
                planIn: planIn,
                planOut: planOut);
        }

        /// <summary>
        /// How many minutes after the shift's own start-of-day time
        /// (e.g. 08:00 for Day, 20:00 for Night) the given "HH:mm" clock
        /// time falls, wrapping past midnight when needed. This is what
        /// lets a block's absolute position in the shift be computed
        /// directly from its own Start/End rather than from a running
        /// cursor - the fix that makes gaps behave as breaks.
        /// </summary>
        public static int MinutesFromShiftStart(string hhmm, int shiftStartMinuteOfDay)
        {
            if (!TryParseHhMm(hhmm, out int minuteOfDay))
            {
                return 0;
            }

            int diff = minuteOfDay - shiftStartMinuteOfDay;
            if (diff < 0)
            {
                diff += 24 * 60;
            }
            return diff;
        }

        /// <summary>
        /// Minutes between two "HH:mm" times, assuming the block rolls over
        /// to the next day whenever End &lt;= Start (same rule the Settings
        /// window, Phase 5, will use for its live Tact-time preview - mirrors
        /// update_tact_for_row() in the Python script).
        /// </summary>
        public static int BlockDurationMinutes(string startTime, string endTime)
        {
            if (!TryParseHhMm(startTime, out int startMinutes) ||
                !TryParseHhMm(endTime, out int endMinutes))
            {
                return 0;
            }

            if (endMinutes <= startMinutes)
            {
                endMinutes += 24 * 60;
            }

            return endMinutes - startMinutes;
        }

        private static bool TryParseHhMm(string value, out int totalMinutes)
        {
            totalMinutes = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(new[] { ':' });
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out int hours)) return false;
            if (!int.TryParse(parts[1], out int minutes)) return false;

            totalMinutes = hours * 60 + minutes;
            return true;
        }
    }
}
