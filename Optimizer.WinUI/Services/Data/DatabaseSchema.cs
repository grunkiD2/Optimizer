namespace Optimizer.WinUI.Services.Data;

/// <summary>SQL schema definitions for Optimizer SQLite database.</summary>
public static class DatabaseSchema
{
    public const int CurrentVersion = 2;

    public static readonly string[] Tables = new[]
    {
        // ────────────────────────────────────────────────────────────────
        // Core Settings (replaces app-settings.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS Settings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Assistant Action Log (new in Phase 1)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS AssistantActions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ToolId TEXT NOT NULL,
            Arguments TEXT,
            Success INTEGER NOT NULL,
            ErrorMessage TEXT,
            ExecutedAtUtc TEXT NOT NULL,
            ExecutionTimeMs INTEGER,
            DetectedContext TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            CONSTRAINT fk_context FOREIGN KEY(DetectedContext)
                REFERENCES Contexts(Name)
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_actions_tool_context
            ON AssistantActions(ToolId, DetectedContext)
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_actions_executed
            ON AssistantActions(ExecutedAtUtc DESC)
        """,

        // ────────────────────────────────────────────────────────────────
        // Assistant Session Transcripts (new in Phase 1)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS AssistantSessions (
            Id TEXT PRIMARY KEY,
            SessionDate TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            ArchivedAtUtc TEXT
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS SessionEvents (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SessionId TEXT NOT NULL,
            EventType TEXT NOT NULL,
            Content TEXT,
            CreatedAtUtc TEXT DEFAULT CURRENT_TIMESTAMP,

            CONSTRAINT fk_session FOREIGN KEY(SessionId)
                REFERENCES AssistantSessions(Id) ON DELETE CASCADE
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_session_events_date
            ON SessionEvents(CreatedAtUtc DESC)
        """,

        // ────────────────────────────────────────────────────────────────
        // Context Detection (new in Phase 1)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS Contexts (
            Name TEXT PRIMARY KEY,
            Description TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // History (replaces change-history.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS History (
            Id TEXT PRIMARY KEY,
            OptimizationId TEXT NOT NULL,
            OptimizationTitle TEXT NOT NULL,
            Category TEXT,
            TimestampUtc TEXT NOT NULL,
            Action TEXT NOT NULL,
            IsReversible INTEGER NOT NULL,
            IsUndone INTEGER NOT NULL,
            ResultText TEXT,
            DetectedContext TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            CONSTRAINT fk_context_hist FOREIGN KEY(DetectedContext)
                REFERENCES Contexts(Name)
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_history_timestamp
            ON History(TimestampUtc DESC)
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_history_optimization
            ON History(OptimizationId)
        """,

        // ────────────────────────────────────────────────────────────────
        // Profiles (replaces snapshots.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS Profiles (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            Description TEXT,
            ProfileType TEXT NOT NULL,
            IsDefault INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            LastAppliedAtUtc TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS ProfileOptimizations (
            ProfileId TEXT NOT NULL,
            OptimizationId TEXT NOT NULL,
            [Order] INTEGER NOT NULL,

            PRIMARY KEY(ProfileId, OptimizationId),
            CONSTRAINT fk_profile FOREIGN KEY(ProfileId)
                REFERENCES Profiles(Id) ON DELETE CASCADE
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Automation Rules (replaces profile-rules.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS AutomationRules (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            TriggerType TEXT NOT NULL,
            ProfileId TEXT NOT NULL,
            ProfileName TEXT,
            IsEnabled INTEGER NOT NULL,
            StartTimeSpan TEXT,
            EndTimeSpan TEXT,
            ProcessName TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            CONSTRAINT fk_profile_rule FOREIGN KEY(ProfileId)
                REFERENCES Profiles(Id)
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Action Analytics (new in Phase 2)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS ToolContextMetrics (
            ToolId TEXT NOT NULL,
            Context TEXT NOT NULL,
            TotalInvocations INTEGER NOT NULL DEFAULT 0,
            SuccessfulInvocations INTEGER NOT NULL DEFAULT 0,
            AverageDurationMs REAL,
            LastInvokedUtc TEXT,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            PRIMARY KEY(ToolId, Context),
            CONSTRAINT fk_context_metrics FOREIGN KEY(Context)
                REFERENCES Contexts(Name)
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Learned Patterns (new in Phase 2)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS LearnedPatterns (
            Id TEXT PRIMARY KEY,
            Description TEXT NOT NULL,
            ActionSequence TEXT NOT NULL,
            ApplicableContext TEXT,
            ObservedCount INTEGER NOT NULL DEFAULT 0,
            SuccessfulCount INTEGER NOT NULL DEFAULT 0,
            FirstObservedUtc TEXT NOT NULL,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            CONSTRAINT fk_context_pattern FOREIGN KEY(ApplicableContext)
                REFERENCES Contexts(Name)
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Assistant Feedback (new in Phase 2)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS AssistantFeedback (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SessionId TEXT,
            ToolId TEXT NOT NULL,
            UserFeedback TEXT NOT NULL,
            Comment TEXT,
            CreatedAtUtc TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Undo History (replaces undo.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS UndoStack (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            OptimizationId TEXT NOT NULL,
            Title TEXT,
            GroupId TEXT,
            BeforeState TEXT,
            AfterState TEXT,
            AppliedAtUtc TEXT NOT NULL,
            Reversible INTEGER NOT NULL,
            IsUndone INTEGER NOT NULL DEFAULT 0,
            DetectedContext TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_undo_applied
            ON UndoStack(AppliedAtUtc DESC)
        """,

        // ────────────────────────────────────────────────────────────────
        // Trend History (replaces trend-history.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS TrendHistory (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            DriveId TEXT NOT NULL,
            SampleDateUtc TEXT NOT NULL,
            FreeMb REAL,
            TotalMb REAL,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            UNIQUE(DriveId, SampleDateUtc)
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS idx_trend_drive_date
            ON TrendHistory(DriveId, SampleDateUtc DESC)
        """,

        // ────────────────────────────────────────────────────────────────
        // Recommendations Preferences (replaces rec-preferences.json)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS RecommendationPreferences (
            Id TEXT PRIMARY KEY,
            AcceptCount INTEGER NOT NULL DEFAULT 0,
            DismissCount INTEGER NOT NULL DEFAULT 0,
            SnoozedUntilUtc TEXT,
            LastShownUtc TEXT,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Profile ↔ Context Associations (new in Phase 3)
        // Tracks how often a profile is applied in a context and whether it "stuck".
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS ProfileContextAssociations (
            ProfileId TEXT NOT NULL,
            Context TEXT NOT NULL,
            ApplyCount INTEGER NOT NULL DEFAULT 0,
            SuccessCount INTEGER NOT NULL DEFAULT 0,
            LastAppliedUtc TEXT,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,

            PRIMARY KEY(ProfileId, Context),
            CONSTRAINT fk_context_assoc FOREIGN KEY(Context)
                REFERENCES Contexts(Name)
        )
        """,

        // Pending profile applications awaiting a "did it stick?" verdict.
        """
        CREATE TABLE IF NOT EXISTS ProfileApplications (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProfileId TEXT NOT NULL,
            Context TEXT NOT NULL,
            AppliedAtUtc TEXT NOT NULL,
            Resolved INTEGER NOT NULL DEFAULT 0,
            WasSuccess INTEGER
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Suggested Automation Rules (new in Phase 3)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS SuggestedRules (
            Id TEXT PRIMARY KEY,
            ProfileId TEXT NOT NULL,
            ProfileName TEXT,
            TriggerType TEXT NOT NULL,
            TriggerValue TEXT,
            ConfidenceScore REAL NOT NULL DEFAULT 0,
            ReasoningText TEXT,
            Status TEXT NOT NULL DEFAULT 'Pending',
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Scheduled Optimizations (new in Phase 5)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS ScheduledTasks (
            Id TEXT PRIMARY KEY,
            Kind TEXT NOT NULL,           -- 'profile' | 'optimization'
            TargetId TEXT NOT NULL,
            ScheduleType TEXT NOT NULL,    -- 'DailyAt' | 'IntervalMinutes' | 'Once'
            ScheduleValue TEXT NOT NULL,   -- 'HH:mm' | minutes | ISO-8601
            Enabled INTEGER NOT NULL DEFAULT 1,
            LastRunUtc TEXT,
            NextRunUtc TEXT,
            CreatedAtUtc TEXT NOT NULL
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS ScheduleExecutions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TaskId TEXT NOT NULL,
            RanAtUtc TEXT NOT NULL,
            Success INTEGER NOT NULL,
            Message TEXT
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Anomaly Detection with Learning (new in Phase 6)
        // Online mean/variance (Welford) baseline per (context, metric).
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS MetricBaselines (
            Context TEXT NOT NULL,
            Metric TEXT NOT NULL,
            SampleCount INTEGER NOT NULL DEFAULT 0,
            Mean REAL NOT NULL DEFAULT 0,
            M2 REAL NOT NULL DEFAULT 0,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY(Context, Metric)
        )
        """,

        // How many times the user dismissed alerts for a (context, metric);
        // once it crosses a threshold the detector stops surfacing them.
        """
        CREATE TABLE IF NOT EXISTS AnomalySuppressions (
            Context TEXT NOT NULL,
            Metric TEXT NOT NULL,
            DismissCount INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY(Context, Metric)
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS AnomalyAlerts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Context TEXT NOT NULL,
            Metric TEXT NOT NULL,
            Value REAL NOT NULL,
            Expected REAL NOT NULL,
            Sigma REAL NOT NULL,
            CreatedAtUtc TEXT NOT NULL
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Predictive Maintenance Alerts (new in Phase 6)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS MaintenanceAlerts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Signature TEXT NOT NULL UNIQUE,
            Kind TEXT NOT NULL,            -- 'DiskSpace' | 'DiskFailure'
            Target TEXT NOT NULL,
            Message TEXT NOT NULL,
            Severity TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            Acknowledged INTEGER NOT NULL DEFAULT 0
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Optimization Outcomes per Context (new in Phase 7)
        // Drives the confirm-on-first-occurrence auto-apply gate.
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS OptimizationOutcomes (
            OptimizationId TEXT NOT NULL,
            Context TEXT NOT NULL,
            SuccessCount INTEGER NOT NULL DEFAULT 0,
            FailureCount INTEGER NOT NULL DEFAULT 0,
            LastAppliedUtc TEXT,
            PRIMARY KEY(OptimizationId, Context)
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Per-Context State Snapshots (new in Phase 7)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS ContextSnapshots (
            Context TEXT PRIMARY KEY,
            StateJson TEXT NOT NULL,
            CapturedAtUtc TEXT NOT NULL
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Metadata (version tracking)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS Metadata (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Fancontrol federation telemetry — ingested READ-ONLY from the
        // Fancontrol system's state\telemetry\*.jsonl (5 s brain ticks).
        // Ts is the brain's own ISO timestamp; PRIMARY KEY = idempotent re-ingestion.
        // ────────────────────────────────────────────────────────────────
        // ────────────────────────────────────────────────────────────────
        // Per-Process Power Intelligence (docs/POWER-INSIGHTS.md) — attribution
        // snapshots, per-(context,process) Welford baselines, surfaced drift.
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS PowerSnapshots (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Ts TEXT NOT NULL,
            Context TEXT NOT NULL,
            ProcessName TEXT NOT NULL,
            AvgPowerW REAL NOT NULL,
            CpuShare REAL NOT NULL,
            WindowSec REAL NOT NULL
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS IX_PowerSnapshots_Ts ON PowerSnapshots(Ts)
        """,

        """
        CREATE TABLE IF NOT EXISTS PowerBaselines (
            Context TEXT NOT NULL,
            ProcessName TEXT NOT NULL,
            Count REAL NOT NULL,
            MeanW REAL NOT NULL,
            M2 REAL NOT NULL,
            EwmaW REAL NOT NULL,
            LastUpdated TEXT NOT NULL,
            PRIMARY KEY (Context, ProcessName)
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS PowerDriftEvents (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Ts TEXT NOT NULL,
            Context TEXT NOT NULL,
            ProcessName TEXT NOT NULL,
            ObservedW REAL NOT NULL,
            BaselineW REAL NOT NULL,
            ZScore REAL NOT NULL,
            Classification TEXT NOT NULL
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS FancontrolTelemetry (
            Ts TEXT PRIMARY KEY,
            Mode TEXT NOT NULL DEFAULT '',
            Night INTEGER NOT NULL DEFAULT 0,
            Game INTEGER NOT NULL DEFAULT 0,
            Alarm INTEGER NOT NULL DEFAULT 0,
            CpuLoad REAL, CpuTemp REAL, CpuWatts REAL,
            GpuLoad REAL, GpuTemp REAL, GpuWatts REAL, GpuMem REAL,
            Coolant REAL, PumpRpm INTEGER,
            CaseDemand INTEGER, RadDemand INTEGER,
            App TEXT
        )
        """,

        // ────────────────────────────────────────────────────────────────
        // Profile verification loop (Profil 2.0 — Fase 2)
        // ProfileTimeline = profile-active intervals (one row per fgwatch
        //   profile switch; EndTs NULL = still active, set when the NEXT
        //   profile starts). ProfileOutcomes = per-interval rollups
        //   (coolant-p95 from FancontrolTelemetry + fps-1%-low from PresentMon).
        // Both start empty and fill forward — no data migration.
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS ProfileTimeline (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProfileName TEXT NOT NULL,
            StartTs TEXT NOT NULL,
            EndTs TEXT,
            Exe TEXT
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS IX_ProfileTimeline_Profile
            ON ProfileTimeline(ProfileName, StartTs)
        """,

        """
        CREATE TABLE IF NOT EXISTS ProfileOutcomes (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProfileName TEXT NOT NULL,
            RecordedAtUtc TEXT NOT NULL,
            DurationMinutes INTEGER NOT NULL,
            SampleCount INTEGER NOT NULL,
            CoolantP95 REAL,
            GpuFps1Low REAL
        )
        """,

        """
        CREATE INDEX IF NOT EXISTS IX_ProfileOutcomes_Profile
            ON ProfileOutcomes(ProfileName, RecordedAtUtc)
        """
    };

    /// <summary>
    /// Idempotent ALTER statements for upgrading databases created by an earlier schema.
    /// Each runs inside a try/catch that swallows "duplicate column" errors, so adding a
    /// column already present (e.g. on a fresh install) is a no-op.
    /// </summary>
    public static readonly string[] Migrations = new[]
    {
        "ALTER TABLE UndoStack ADD COLUMN GroupId TEXT",
        "ALTER TABLE UndoStack ADD COLUMN IsUndone INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE UndoStack ADD COLUMN DetectedContext TEXT",
    };

    /// <summary>Insert default contexts and metadata. Columns are named explicitly so the
    /// tables' default-valued columns (UpdatedAt / CreatedAt) are populated automatically.</summary>
    public static readonly string[] InitialData = new[]
    {
        $"INSERT OR IGNORE INTO Metadata (Key, Value) VALUES ('SchemaVersion', '{CurrentVersion}')",
        $"INSERT OR IGNORE INTO Metadata (Key, Value) VALUES ('CreatedAt', '{DateTime.UtcNow:O}')",

        "INSERT OR IGNORE INTO Contexts (Name, Description) VALUES ('Gaming', 'Gaming context')",
        "INSERT OR IGNORE INTO Contexts (Name, Description) VALUES ('Work', 'Work/Productivity context')",
        "INSERT OR IGNORE INTO Contexts (Name, Description) VALUES ('Plex', 'Plex server/Media hosting context')",
        "INSERT OR IGNORE INTO Contexts (Name, Description) VALUES ('Unknown', 'Unknown or general context')"
    };
}
