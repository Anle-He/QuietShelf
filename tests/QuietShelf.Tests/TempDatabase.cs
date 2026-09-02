using Microsoft.Data.Sqlite;
using QuietShelf.Data;

namespace QuietShelf.Tests;

internal sealed class TempDatabase : IAsyncDisposable
{
    private TempDatabase(string root, Database database)
    {
        Root = root;
        Database = database;
        Repository = new LibraryRepository(database);
    }

    public string Root { get; }
    public Database Database { get; }
    public LibraryRepository Repository { get; }

    public static async Task<TempDatabase> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = new Database(Path.Combine(root, "records.db"));
        await database.InitializeAsync();
        return new TempDatabase(root, database);
    }

    public async Task SeedHistoricalProgressAsync(string experienceId, DateOnly loggedOn)
    {
        await using var connection = new SqliteConnection(Database.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO progress_entries (id, experience_id, logged_on, metric, amount, notes, created_at, updated_at)
            VALUES ($id, $experienceId, $loggedOn, 'duration', 1, NULL, $createdAt, $updatedAt);
            """;
        var now = DateTimeOffset.UtcNow.ToString("O");
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$experienceId", experienceId);
        command.Parameters.AddWithValue("$loggedOn", loggedOn.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CountHistoricalProgressAsync(string experienceId)
    {
        await using var connection = new SqliteConnection(Database.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM progress_entries WHERE experience_id = $experienceId;";
        command.Parameters.AddWithValue("$experienceId", experienceId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        var resolvedRoot = Path.GetFullPath(Root);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!resolvedRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolvedRoot).StartsWith("QuietShelf-Tests-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
        }
        Directory.Delete(resolvedRoot, recursive: true);
        return ValueTask.CompletedTask;
    }
}
