namespace Optimizer.WinUI.Services.Data;

/// <summary>SQL schema definitions for Optimizer SQLite database.</summary>
public static class DatabaseSchema
{
    public const int CurrentVersion = 1;

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
            BeforeState TEXT,
            AfterState TEXT,
            AppliedAtUtc TEXT NOT NULL,
            Reversible INTEGER NOT NULL,
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
        // Metadata (version tracking)
        // ────────────────────────────────────────────────────────────────
        """
        CREATE TABLE IF NOT EXISTS Metadata (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """
    };

    /// <summary>Insert default contexts and metadata.</summary>
    public static readonly string[] InitialData = new[]
    {
        $"INSERT OR IGNORE INTO Metadata VALUES ('SchemaVersion', '{CurrentVersion}')",
        $"INSERT OR IGNORE INTO Metadata VALUES ('CreatedAt', '{DateTime.UtcNow:O}')",

        "INSERT OR IGNORE INTO Contexts VALUES ('Gaming', 'Gaming context')",
        "INSERT OR IGNORE INTO Contexts VALUES ('Work', 'Work/Productivity context')",
        "INSERT OR IGNORE INTO Contexts VALUES ('Plex', 'Plex server/Media hosting context')",
        "INSERT OR IGNORE INTO Contexts VALUES ('Unknown', 'Unknown or general context')"
    };
}
