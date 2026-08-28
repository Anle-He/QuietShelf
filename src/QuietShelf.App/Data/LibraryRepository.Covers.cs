using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using QuietShelf.Models;

namespace QuietShelf.Data;

public sealed partial class LibraryRepository
{
    // The application is single-instance. Share gates across repository instances
    // so each cover directory stays consistent through file and database changes.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CoverOperationGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WorkCover>> GetCoversAsync(string workId)
    {
        using var coverLock = await AcquireCoverLockAsync(workId);
        return await ReadAndReconcileCoversAsync(workId);
    }

    // Caller must hold the cover lock, including while reading the database snapshot.
    private async Task<IReadOnlyList<WorkCover>> ReadAndReconcileCoversAsync(string workId)
    {
        var covers = new List<WorkCover>();
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, work_id, file_name, sort_order, created_at
            FROM work_covers
            WHERE work_id = $workId
            ORDER BY sort_order, created_at;
            """;
        command.Parameters.AddWithValue("$workId", workId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fileName = reader.GetString(2);
            covers.Add(new WorkCover
            {
                Id = reader.GetString(0),
                WorkId = reader.GetString(1),
                FileName = fileName,
                FilePath = database.GetCoverFilePath(workId, fileName),
                SortOrder = reader.GetInt32(3),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)
            });
        }
        ReconcileDeletedCoverFiles(workId, covers);
        return covers;
    }

    public async Task AddCoversAsync(string workId, IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
        {
            return;
        }

        using var coverLock = await AcquireCoverLockAsync(workId);
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
        var existing = await ReadAndReconcileCoversAsync(workId);
        if (existing.Count + sourcePaths.Count > 20)
        {
            throw new InvalidOperationException("每部作品最多保存 20 张封面。");
        }

        var coverDirectory = database.GetCoverDirectory(workId);
        Directory.CreateDirectory(coverDirectory);
        var staged = new List<WorkCover>();
        var createdFiles = new List<string>();
        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                var source = new FileInfo(sourcePath);
                if (!source.Exists || !allowedExtensions.Contains(source.Extension))
                {
                    throw new InvalidOperationException("封面仅支持 JPG、PNG 或 BMP 图片。");
                }
                if (source.Length > 25 * 1024 * 1024)
                {
                    throw new InvalidOperationException($"图片“{source.Name}”超过 25 MB。");
                }

                var coverId = Guid.NewGuid().ToString("N");
                var fileName = coverId + ".jpg";
                var destination = database.GetCoverFilePath(workId, fileName);
                var temporaryDestination = destination + ".adding";
                createdFiles.Add(temporaryDestination);
                await CoverImageProcessor.SaveOptimizedJpegAsync(source.FullName, temporaryDestination);
                File.Move(temporaryDestination, destination);
                createdFiles.Add(destination);
                staged.Add(new WorkCover
                {
                    Id = coverId,
                    WorkId = workId,
                    FileName = fileName,
                    FilePath = destination,
                    SortOrder = existing.Count + staged.Count
                });
            }

            await using var connection = await OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var cover in staged)
            {
                var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = """
                    INSERT INTO work_covers (id, work_id, file_name, sort_order, created_at)
                    VALUES ($id, $workId, $fileName, $sortOrder, $createdAt);
                    """;
                insert.Parameters.AddWithValue("$id", cover.Id);
                insert.Parameters.AddWithValue("$workId", cover.WorkId);
                insert.Parameters.AddWithValue("$fileName", cover.FileName);
                insert.Parameters.AddWithValue("$sortOrder", cover.SortOrder);
                insert.Parameters.AddWithValue("$createdAt", cover.CreatedAt.ToString("O"));
                await insert.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            foreach (var path in createdFiles)
            {
                TryDeleteFile(path);
            }
            throw;
        }
    }

    public Task SetPrimaryCoverAsync(string workId, string coverId) => ReorderCoverAsync(workId, coverId, 0, absolute: true);

    public Task MoveCoverAsync(string workId, string coverId, int offset) => ReorderCoverAsync(workId, coverId, offset, absolute: false);

    public async Task DeleteCoverAsync(string workId, string coverId)
    {
        using var coverLock = await AcquireCoverLockAsync(workId);
        var covers = await ReadAndReconcileCoversAsync(workId);
        var cover = covers.FirstOrDefault(item => item.Id == coverId);
        if (cover is null)
        {
            return;
        }

        var temporaryPath = cover.FilePath + ".deleting";
        if (File.Exists(cover.FilePath))
        {
            File.Move(cover.FilePath, temporaryPath, overwrite: true);
        }
        var committed = false;
        try
        {
            await using var connection = await OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            var delete = connection.CreateCommand();
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM work_covers WHERE id = $id AND work_id = $workId;";
            delete.Parameters.AddWithValue("$id", coverId);
            delete.Parameters.AddWithValue("$workId", workId);
            await delete.ExecuteNonQueryAsync();
            await WriteCoverOrderAsync(connection, (SqliteTransaction)transaction, workId,
                covers.Where(item => item.Id != coverId).Select(item => item.Id).ToList());
            await transaction.CommitAsync();
            committed = true;
        }
        catch
        {
            if (!committed && File.Exists(temporaryPath))
            {
                File.Move(temporaryPath, cover.FilePath, overwrite: true);
            }
            throw;
        }

        TryDeleteFile(temporaryPath);
    }

    private async Task ReorderCoverAsync(string workId, string coverId, int position, bool absolute)
    {
        using var coverLock = await AcquireCoverLockAsync(workId);
        var covers = await ReadAndReconcileCoversAsync(workId);
        var ids = covers.Select(cover => cover.Id).ToList();
        var currentIndex = ids.IndexOf(coverId);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = absolute ? position : currentIndex + position;
        targetIndex = Math.Clamp(targetIndex, 0, ids.Count - 1);
        if (targetIndex == currentIndex)
        {
            return;
        }

        ids.RemoveAt(currentIndex);
        ids.Insert(targetIndex, coverId);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await WriteCoverOrderAsync(connection, (SqliteTransaction)transaction, workId, ids);
        await transaction.CommitAsync();
    }

    private static async Task WriteCoverOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workId,
        IReadOnlyList<string> orderedCoverIds)
    {
        for (var index = 0; index < orderedCoverIds.Count; index++)
        {
            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE work_covers SET sort_order = $sortOrder WHERE id = $id AND work_id = $workId;";
            update.Parameters.AddWithValue("$sortOrder", index);
            update.Parameters.AddWithValue("$id", orderedCoverIds[index]);
            update.Parameters.AddWithValue("$workId", workId);
            await update.ExecuteNonQueryAsync();
        }
    }

    private async Task<IDisposable> AcquireCoverLockAsync(string workId)
    {
        var gate = CoverOperationGates.GetOrAdd(database.GetCoverDirectory(workId), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new CoverOperationLock(gate);
    }

    private sealed class CoverOperationLock(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The database is authoritative after commit; a later cleanup can remove the staged file.
        }
        catch (UnauthorizedAccessException)
        {
            // The database is authoritative after commit; a later cleanup can remove the staged file.
        }
    }

    private void ReconcileDeletedCoverFiles(string workId, IReadOnlyList<WorkCover> covers)
    {
        var directory = database.GetCoverDirectory(workId);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.deleting", SearchOption.TopDirectoryOnly))
        {
            var originalPath = path[..^".deleting".Length];
            if (covers.Any(cover => string.Equals(cover.FilePath, originalPath, StringComparison.OrdinalIgnoreCase)))
            {
                if (!File.Exists(originalPath))
                {
                    File.Move(path, originalPath);
                }
            }
            else
            {
                TryDeleteFile(path);
            }
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.adding", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(path);
        }

        var referencedPaths = covers.Select(cover => cover.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => new[] { ".jpg", ".jpeg", ".png", ".bmp" }
                         .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            if (!referencedPaths.Contains(path))
            {
                TryDeleteFile(path);
            }
        }
    }
}
