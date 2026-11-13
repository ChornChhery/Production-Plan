using System.Collections.Generic;

namespace Teeth
{
    /// <summary>
    /// Mirrors the single-row "ShiftConfig" table: which line is active,
    /// which shift profile (Day/Night) is active, and the current global
    /// plan totals shown in the PLAN IN / PLAN OUT boxes.
    /// Replaces the top-level keys in the old schedule.json.
    /// </summary>
    public class ShiftConfigModel
    {
        public string ActiveLine { get; set; } = "Line A";
        public string ActiveProfile { get; set; } = "Day Schedule";
        public int GlobalPlanIn { get; set; } = 0;
        public int GlobalPlanOut { get; set; } = 0;
    }

    /// <summary>
    /// Mirrors one row of the "WorkBlocks" table. Replaces one entry in the
    /// old schedule.json "presets" array. One row = one Work/Break block
    /// belonging to either the "Day Schedule" or "Night Schedule" profile.
    /// </summary>
    public class WorkBlockModel
    {
        public int Id { get; set; }
        public string Profile { get; set; }      // "Day Schedule" or "Night Schedule"
        public int BlockOrder { get; set; }       // 0-10, row position in the timeline
        public string Name { get; set; }          // "Work 1", "Break 2", etc.
        public string StartTime { get; set; }      // "HH:mm"
        public string EndTime { get; set; }        // "HH:mm"
        public bool IsBreak { get; set; }
        public int TargetIn { get; set; }
        public int TargetOut { get; set; }
    }

    /// <summary>
    /// Mirrors the single-row "LiveCounts" table. Replaces counts.json.
    /// </summary>
    public class LiveCountsModel
    {
        public int CountIn { get; set; } = 0;
        public int CountOut { get; set; } = 0;
        public double FrozenSeconds { get; set; } = 0.0;

        /// <summary>Which shift these counts belong to, as the shift's start
        /// time ("yyyy-MM-dd HH:mm", e.g. "2026-11-04 20:00"). null = unknown
        /// (old database) when loaded, and "leave the stored value alone"
        /// when saved - so code that builds a fresh LiveCountsModel (e.g.
        /// Sensor Correction) can never erase it.</summary>
        public string ShiftKey { get; set; } = null;

        /// <summary>The IN/OUT counts that were last written to the Excel log
        /// as "App Closed Mid-Shift" (null = nothing logged for the current
        /// counts). Read-only from the model's point of view: changed only via
        /// DatabaseManager.SetLastLogged so a normal save never touches it.</summary>
        public int? LastLoggedIn { get; set; } = null;
        public int? LastLoggedOut { get; set; } = null;
    }
}
