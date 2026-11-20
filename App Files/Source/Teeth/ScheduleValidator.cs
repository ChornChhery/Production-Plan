using System.Collections.Generic;
using System.Linq;

namespace Teeth
{
    /// <summary>One problem found while checking a schedule before Save.</summary>
    public class ScheduleError
    {
        public string Profile { get; set; }
        public int RowIndex { get; set; }      // 0-based row in that profile's grid
        public string Message { get; set; }

        public override string ToString()
        {
            return $"{Profile} row {RowIndex + 1}: {Message}";
        }
    }

    /// <summary>
    /// The rules the Schedule window checks on Save. Kept separate from the
    /// form so they can be tested without a screen.
    ///
    /// How "closed" works: a row whose Start AND End are both empty is a
    /// closed (unused) block. It is not an error, it is not counted in Plan,
    /// and Target ignores it. Gaps between open blocks are breaks.
    /// </summary>
    public static class ScheduleValidator
    {
        public const int ShiftLengthMinutes = 12 * 60;

        public static int ShiftStartMinuteOfDay(string profile)
        {
            return profile == "Night Schedule" ? 20 * 60 : 8 * 60;
        }

        public static List<ScheduleError> Validate(string profile, IList<WorkBlockModel> blocks)
        {
            var errors = new List<ScheduleError>();
            int shiftStartMod = ShiftStartMinuteOfDay(profile);

            // Open works that passed the basic checks: (row, startOffset, endOffset).
            // Overlap is judged by TIME order, not row order, so works can be
            // added in any order (e.g. a new work at the bottom that starts earlier).
            var placed = new List<int[]>();

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                bool noStart = string.IsNullOrEmpty(b.StartTime);
                bool noEnd = string.IsNullOrEmpty(b.EndTime);

                if (noStart && noEnd)
                {
                    continue; // closed / unused row - not an error, not counted
                }

                if (noStart || noEnd)
                {
                    errors.Add(Err(profile, i, "only one of Start/End is set. Pick both, or clear both to close this work."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(b.Name))
                {
                    errors.Add(Err(profile, i, "name is required."));
                }

                if (b.StartTime == b.EndTime)
                {
                    errors.Add(Err(profile, i, "Start and End time cannot be the same."));
                    continue;
                }

                int minutes = TargetCalculator.BlockDurationMinutes(b.StartTime, b.EndTime);
                if (minutes <= 0)
                {
                    errors.Add(Err(profile, i, "pick a valid Start/End time."));
                    continue;
                }

                if (b.TargetIn < 0 || b.TargetOut < 0)
                {
                    errors.Add(Err(profile, i, "targets cannot be negative."));
                }

                int startOffset = TargetCalculator.MinutesFromShiftStart(b.StartTime, shiftStartMod);
                int endOffset = startOffset + minutes;

                if (endOffset > ShiftLengthMinutes)
                {
                    string from = $"{shiftStartMod / 60:00}:{shiftStartMod % 60:00}";
                    int endMod = (shiftStartMod + ShiftLengthMinutes) % (24 * 60);
                    string to = $"{endMod / 60:00}:{endMod % 60:00}";
                    errors.Add(Err(profile, i, $"{b.StartTime}-{b.EndTime} is outside the shift ({from}-{to})."));
                    continue;
                }

                placed.Add(new[] { i, startOffset, endOffset });
            }

            placed.Sort((a, b) => a[1] != b[1] ? a[1].CompareTo(b[1]) : a[2].CompareTo(b[2]));
            for (int k = 1; k < placed.Count; k++)
            {
                if (placed[k][1] < placed[k - 1][2])
                {
                    errors.Add(Err(profile, placed[k][0],
                        $"overlaps row {placed[k - 1][0] + 1} (starts before it ends)."));
                }
            }

            errors.Sort((a, b) => a.RowIndex.CompareTo(b.RowIndex));
            return errors;
        }

        /// <summary>
        /// Called after a successful validation, just before saving: open works
        /// sorted by Start time, closed works (no Start/End) after them, closed
        /// works carry no times and no targets, BlockOrder renumbered 0..n-1.
        /// Any number of works is allowed.
        /// </summary>
        public static List<WorkBlockModel> Normalize(string profile, IList<WorkBlockModel> blocks)
        {
            int shiftStartMod = ShiftStartMinuteOfDay(profile);
            var open = new List<WorkBlockModel>();
            var closed = new List<WorkBlockModel>();

            foreach (var b in blocks)
            {
                if (string.IsNullOrEmpty(b.StartTime) || string.IsNullOrEmpty(b.EndTime))
                {
                    b.StartTime = "";
                    b.EndTime = "";
                    b.TargetIn = 0;
                    b.TargetOut = 0;
                    closed.Add(b);
                }
                else
                {
                    open.Add(b);
                }
            }

            // stable sort by start offset
            var sorted = open
                .Select((b, idx) => new { b, idx, off = TargetCalculator.MinutesFromShiftStart(b.StartTime, shiftStartMod) })
                .OrderBy(x => x.off).ThenBy(x => x.idx)
                .Select(x => x.b).ToList();

            var result = new List<WorkBlockModel>(sorted);
            result.AddRange(closed);
            for (int i = 0; i < result.Count; i++) result[i].BlockOrder = i;
            return result;
        }

        private static ScheduleError Err(string profile, int row, string message)
        {
            return new ScheduleError { Profile = profile, RowIndex = row, Message = message };
        }
    }
}
