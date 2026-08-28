using Microsoft.Data.Sqlite;
using QuietShelf.Data;

namespace QuietShelf.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task CurrentMigration_DoesNotRestoreDeletedLegacyWork()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "records.db");
        try
        {
            await CreateVersionZeroDatabaseAsync(databasePath);
            var database = new Database(databasePath);
            await database.InitializeAsync();

            var repository = new LibraryRepository(database);
            await repository.DeleteWorkAsync("work-1");

            await new Database(databasePath).InitializeAsync();

            Assert.Null(await new LibraryRepository(new Database(databasePath)).GetWorkAsync("work-1"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentMigration_PreservesLegacyInProgressState()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "records.db");
        try
        {
            await CreateLegacyInProgressDatabaseAsync(databasePath);
            var database = new Database(databasePath);

            await database.InitializeAsync();

            var repository = new LibraryRepository(database);
            var work = await repository.GetWorkAsync("work-active");
            var active = await repository.GetActiveExperienceAsync("work-active");
            Assert.Equal("in_progress", work?.Status);
            Assert.Equal(new DateOnly(2026, 8, 3), active?.StartedOn);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentMigration_RepairsLegacyExperienceOnceAndAddsAuthorColumn()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "records.db");
        try
        {
            await CreateVersionOneInProgressDatabaseAsync(databasePath);
            var database = new Database(databasePath);

            await database.InitializeAsync();

            var active = await new LibraryRepository(database).GetActiveExperienceAsync("work-active");
            Assert.Equal(new DateOnly(2026, 8, 3), active?.StartedOn);
            await using (var connection = new SqliteConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                Assert.Equal(3L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM pragma_table_info('works') WHERE name='author';"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM works WHERE id='work-active' AND author IS NULL;"));
                var reset = connection.CreateCommand();
                reset.CommandText = "UPDATE experiences SET started_on=NULL WHERE id='work-active-legacy-1';";
                await reset.ExecuteNonQueryAsync();
            }

            await new Database(databasePath).InitializeAsync();

            Assert.Null(await new LibraryRepository(new Database(databasePath)).GetActiveExperienceAsync("work-active"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentMigration_ClampsAllureAndPreservesRecoveryCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "records.db");
        try
        {
            await CreateVersionZeroDatabaseAsync(databasePath);
            var database = new Database(databasePath);

            await database.InitializeAsync();

            Assert.True(File.Exists(database.MigrationBackupPath));
            await using (var current = new SqliteConnection(database.ConnectionString))
            {
                await current.OpenAsync();
                Assert.Equal(3L, await ScalarInt64Async(current, "PRAGMA user_version;"));
                Assert.Equal(3L, await ScalarInt64Async(current, "SELECT allure FROM experiences WHERE id='experience-1';"));
                Assert.Equal(1L, await ScalarInt64Async(current, "SELECT COUNT(*) FROM progress_entries WHERE experience_id='experience-1';"));

                var invalid = current.CreateCommand();
                invalid.CommandText = "UPDATE experiences SET allure=4 WHERE id='experience-1';";
                await Assert.ThrowsAsync<SqliteException>(() => invalid.ExecuteNonQueryAsync());
            }

            var backupConnectionString = new SqliteConnectionStringBuilder { DataSource = database.MigrationBackupPath }.ToString();
            await using var backup = new SqliteConnection(backupConnectionString);
            await backup.OpenAsync();
            Assert.Equal(5L, await ScalarInt64Async(backup, "SELECT allure FROM experiences WHERE id='experience-1';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentMigration_ReplacesInvalidExistingRecoveryCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "records.db");
        try
        {
            await CreateVersionZeroDatabaseAsync(databasePath);
            var database = new Database(databasePath);
            await File.WriteAllTextAsync(database.MigrationBackupPath, "invalid backup");

            await database.InitializeAsync();

            var backupConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = database.MigrationBackupPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();
            await using var backup = new SqliteConnection(backupConnectionString);
            await backup.OpenAsync();
            Assert.Equal("ok", Convert.ToString(await ScalarAsync(backup, "PRAGMA integrity_check;")));
            Assert.Equal(5L, await ScalarInt64Async(backup, "SELECT allure FROM experiences WHERE id='experience-1';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateVersionZeroDatabaseAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            CREATE TABLE media_entries (
                id TEXT PRIMARY KEY, title TEXT NOT NULL,
                kind TEXT NOT NULL CHECK (kind IN ('book', 'screen')),
                status TEXT NULL, completed_on TEXT NULL,
                rating INTEGER NULL,
                allure INTEGER NULL CHECK (allure IS NULL OR allure BETWEEN 1 AND 5),
                immersion INTEGER NULL, rationality INTEGER NULL, illumination INTEGER NULL,
                notes TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE TABLE works (
                id TEXT PRIMARY KEY, title TEXT NOT NULL, subtitle TEXT NULL,
                kind TEXT NOT NULL CHECK (kind IN ('book', 'screen')),
                status TEXT NULL, total_episodes INTEGER NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE TABLE experiences (
                id TEXT PRIMARY KEY,
                work_id TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
                started_on TEXT NULL, completed_on TEXT NULL,
                allure INTEGER NULL CHECK (allure IS NULL OR allure BETWEEN 1 AND 5),
                immersion INTEGER NULL, rationality INTEGER NULL, illumination INTEGER NULL,
                notes TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE TABLE progress_entries (
                id TEXT PRIMARY KEY,
                experience_id TEXT NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
                logged_on TEXT NOT NULL, metric TEXT NOT NULL, amount INTEGER NOT NULL,
                notes TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            INSERT INTO works VALUES ('work-1', 'migration-test', NULL, 'book', 'completed', NULL, '2026-08-01T00:00:00Z', '2026-08-01T00:00:00Z');
            INSERT INTO experiences VALUES ('experience-1', 'work-1', '2026-08-01', '2026-08-02', 5, 5, 5, 5, NULL, '2026-08-01T00:00:00Z', '2026-08-02T00:00:00Z');
            INSERT INTO progress_entries VALUES ('progress-1', 'experience-1', '2026-08-01', 'duration', 30, NULL, '2026-08-01T00:00:00Z', '2026-08-01T00:00:00Z');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateLegacyInProgressDatabaseAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE media_entries (
                id TEXT PRIMARY KEY, title TEXT NOT NULL,
                kind TEXT NOT NULL CHECK (kind IN ('book', 'screen')),
                status TEXT NULL, completed_on TEXT NULL,
                rating INTEGER NULL,
                allure INTEGER NULL CHECK (allure IS NULL OR allure BETWEEN 1 AND 5),
                immersion INTEGER NULL, rationality INTEGER NULL, illumination INTEGER NULL,
                notes TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            INSERT INTO media_entries VALUES (
                'work-active', 'active-test', 'book', 'in_progress', NULL, NULL,
                NULL, NULL, NULL, NULL, NULL,
                '2026-08-03T09:00:00Z', '2026-08-03T09:00:00Z'
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateVersionOneInProgressDatabaseAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA user_version=1;
            CREATE TABLE works (
                id TEXT PRIMARY KEY, title TEXT NOT NULL, subtitle TEXT NULL,
                kind TEXT NOT NULL CHECK (kind IN ('book', 'screen')),
                status TEXT NULL, total_episodes INTEGER NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE TABLE experiences (
                id TEXT PRIMARY KEY,
                work_id TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
                started_on TEXT NULL, completed_on TEXT NULL,
                allure INTEGER NULL CHECK (allure IS NULL OR allure BETWEEN 1 AND 3),
                immersion INTEGER NULL, rationality INTEGER NULL, illumination INTEGER NULL,
                notes TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            INSERT INTO works VALUES (
                'work-active', 'active-test', NULL, 'book', 'in_progress', NULL,
                '2026-08-03T09:00:00Z', '2026-08-03T09:00:00Z'
            );
            INSERT INTO experiences VALUES (
                'work-active-legacy-1', 'work-active', NULL, NULL,
                NULL, NULL, NULL, NULL, NULL,
                '2026-08-03T09:00:00Z', '2026-08-03T09:00:00Z'
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
