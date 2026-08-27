using Microsoft.Data.Sqlite;
using QuietShelf.Data;

namespace QuietShelf.Tests;

public sealed class LocalDatabaseMigrationTests
{
    [Fact]
    [Trait("Category", "LocalData")]
    public async Task ConfiguredSourceDatabase_HasCurrentSchemaAndPreservedCounts()
    {
        var sourcePath = Environment.GetEnvironmentVariable("QUIETSHELF_SOURCE_DATABASE");
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var current = await ReadSnapshotAsync(sourcePath);
        Assert.Equal(1, current.SchemaVersion);

        var backupPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}.pre-v1.bak");
        Assert.True(File.Exists(backupPath));
        var backup = await ReadSnapshotAsync(backupPath);
        Assert.Equal(backup.Works, current.Works);
        Assert.Equal(backup.Experiences, current.Experiences);
        Assert.Equal(backup.ProgressEntries, current.ProgressEntries);
        Assert.Equal(backup.Covers, current.Covers);
    }

    [Fact]
    [Trait("Category", "LocalData")]
    public async Task ConfiguredDatabaseCopy_MigratesWithoutChangingRecordCounts()
    {
        var sourcePath = Environment.GetEnvironmentVariable("QUIETSHELF_SOURCE_DATABASE");
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-LocalMigration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var copyPath = Path.Combine(root, "records.db");
        File.Copy(sourcePath, copyPath);
        try
        {
            var before = await ReadSnapshotAsync(copyPath);
            var database = new Database(copyPath);

            await database.InitializeAsync();

            var after = await ReadSnapshotAsync(copyPath);
            Assert.Equal(1, after.SchemaVersion);
            Assert.Equal(before.Works, after.Works);
            Assert.Equal(before.Experiences, after.Experiences);
            Assert.Equal(before.ProgressEntries, after.ProgressEntries);
            Assert.Equal(before.Covers, after.Covers);
            Assert.True(after.MaximumAllure is null or <= 3);
            if (before.SchemaVersion < 1)
            {
                Assert.True(File.Exists(database.MigrationBackupPath));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return new DatabaseSnapshot(
            Convert.ToInt32(await ScalarAsync(connection, "PRAGMA user_version;")),
            await CountAsync(connection, "works"),
            await CountAsync(connection, "experiences"),
            await CountAsync(connection, "progress_entries"),
            await CountAsync(connection, "work_covers"),
            await NullableIntAsync(connection, "SELECT MAX(allure) FROM experiences;"));
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string tableName)
    {
        var exists = connection.CreateCommand();
        exists.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name);";
        exists.Parameters.AddWithValue("$name", tableName);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0)
        {
            return 0;
        }

        return Convert.ToInt64(await ScalarAsync(connection, $"SELECT COUNT(*) FROM {tableName};"));
    }

    private static async Task<object> ScalarAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() ?? 0L;
    }

    private static async Task<int?> NullableIntAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private sealed record DatabaseSnapshot(
        int SchemaVersion,
        long Works,
        long Experiences,
        long ProgressEntries,
        long Covers,
        int? MaximumAllure);
}
