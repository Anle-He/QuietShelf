using System.IO;
using Microsoft.Data.Sqlite;

namespace QuietShelf.Data;

public sealed class Database
{
    public Database(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultPath());
        DataDirectory = Path.GetDirectoryName(DatabasePath)!;
        CoversDirectory = Path.Combine(DataDirectory, "covers");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CoversDirectory);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string ConnectionString { get; }
    public string DatabasePath { get; }
    public string DataDirectory { get; }
    public string CoversDirectory { get; }

    public string GetCoverDirectory(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId) || workId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("Invalid work identifier for cover storage.");
        }

        var root = Path.GetFullPath(CoversDirectory) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(Path.Combine(CoversDirectory, workId));
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cover directory escaped the data root.");
        }
        return directory;
    }

    public string GetCoverFilePath(string workId, string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid cover file name.");
        }
        return Path.Combine(GetCoverDirectory(workId), fileName);
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            -- 旧表保留用于无损迁移和回退，不再作为新版界面的数据源。
            CREATE TABLE IF NOT EXISTS media_entries (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                kind TEXT NOT NULL CHECK (kind IN ('book', 'screen')),
                status TEXT NULL CHECK (status IS NULL OR status IN ('planned', 'in_progress', 'completed')),
                completed_on TEXT NULL,
                rating INTEGER NULL CHECK (rating IS NULL OR rating BETWEEN 1 AND 5),
                allure INTEGER NULL CHECK (allure IS NULL OR allure BETWEEN 1 AND 5),
                immersion INTEGER NULL CHECK (immersion IS NULL OR immersion BETWEEN 1 AND 5),
                rationality INTEGER NULL CHECK (rationality IS NULL OR rationality BETWEEN 1 AND 5),
                illumination INTEGER NULL CHECK (illumination IS NULL OR illumination BETWEEN 1 AND 5),
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS works (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                subtitle TEXT NULL,
                kind TEXT NOT NULL CHECK (kind IN ('book', 'screen')),
                status TEXT NULL CHECK (status IS NULL OR status IN ('planned', 'in_progress', 'completed')),
                total_episodes INTEGER NULL CHECK (total_episodes IS NULL OR total_episodes > 0),
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS experiences (
                id TEXT PRIMARY KEY,
                work_id TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
                started_on TEXT NULL,
                completed_on TEXT NULL,
                allure INTEGER NULL CHECK (allure IS NULL OR allure BETWEEN 1 AND 5),
                immersion INTEGER NULL CHECK (immersion IS NULL OR immersion BETWEEN 1 AND 5),
                rationality INTEGER NULL CHECK (rationality IS NULL OR rationality BETWEEN 1 AND 5),
                illumination INTEGER NULL CHECK (illumination IS NULL OR illumination BETWEEN 1 AND 5),
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS progress_entries (
                id TEXT PRIMARY KEY,
                experience_id TEXT NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
                logged_on TEXT NOT NULL,
                metric TEXT NOT NULL CHECK (metric IN ('duration', 'episodes')),
                amount INTEGER NOT NULL CHECK (amount > 0),
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS work_covers (
                id TEXT PRIMARY KEY,
                work_id TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
                file_name TEXT NOT NULL,
                sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
                created_at TEXT NOT NULL,
                UNIQUE (work_id, file_name)
            );

            CREATE INDEX IF NOT EXISTS ix_works_title ON works (title COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_experiences_work_date ON experiences (work_id, completed_on DESC, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_progress_experience_date ON progress_entries (experience_id, logged_on DESC, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_work_covers_order ON work_covers (work_id, sort_order, created_at);
            """;
        await command.ExecuteNonQueryAsync();

        await EnsureLegacyColumnAsync(connection, "allure");
        await EnsureLegacyColumnAsync(connection, "immersion");
        await EnsureLegacyColumnAsync(connection, "rationality");
        await EnsureLegacyColumnAsync(connection, "illumination");
        await EnsureExperienceColumnAsync(connection, "started_on");
        await EnsureWorkColumnAsync(connection, "subtitle");
        await EnsureWorkColumnAsync(connection, "total_episodes");

        var activeIndex = connection.CreateCommand();
        activeIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_experiences_one_active ON experiences (work_id) WHERE started_on IS NOT NULL AND completed_on IS NULL;";
        await activeIndex.ExecuteNonQueryAsync();

        var migrate = connection.CreateCommand();
        migrate.CommandText = """
            INSERT OR IGNORE INTO works (id, title, kind, status, created_at, updated_at)
            SELECT id, title, kind, status, created_at, updated_at FROM media_entries;

            INSERT OR IGNORE INTO experiences
                (id, work_id, started_on, completed_on, allure, immersion, rationality, illumination, notes, created_at, updated_at)
            SELECT id || '-legacy-1', id, NULL, completed_on, allure, immersion, rationality, illumination, notes, created_at, updated_at
            FROM media_entries;
            """;
        await migrate.ExecuteNonQueryAsync();
    }

    private static async Task EnsureLegacyColumnAsync(SqliteConnection connection, string columnName)
    {
        var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(media_entries);";
        var exists = false;
        await using (var reader = await info.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        var allowedColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "allure", "immersion", "rationality", "illumination"
        };
        if (!allowedColumns.Contains(columnName))
        {
            throw new InvalidOperationException("Unsupported database column migration.");
        }

        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE media_entries ADD COLUMN {columnName} INTEGER NULL CHECK ({columnName} IS NULL OR {columnName} BETWEEN 1 AND 5);";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task EnsureExperienceColumnAsync(SqliteConnection connection, string columnName)
    {
        var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(experiences);";
        await using (var reader = await info.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        if (!string.Equals(columnName, "started_on", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported experience column migration.");
        }

        var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE experiences ADD COLUMN started_on TEXT NULL;";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task EnsureWorkColumnAsync(SqliteConnection connection, string columnName)
    {
        var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(works);";
        await using (var reader = await info.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        var definition = columnName switch
        {
            "subtitle" => "subtitle TEXT NULL",
            "total_episodes" => "total_episodes INTEGER NULL CHECK (total_episodes IS NULL OR total_episodes > 0)",
            _ => throw new InvalidOperationException("Unsupported work column migration.")
        };

        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE works ADD COLUMN {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    private static string GetDefaultPath()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("QUIETSHELF_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.Combine(overrideDirectory, "records.db");
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "QuietShelf", "records.db");
    }
}
