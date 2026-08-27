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
