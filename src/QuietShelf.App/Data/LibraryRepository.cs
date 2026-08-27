using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using QuietShelf.Models;

namespace QuietShelf.Data;

public sealed class LibraryRepository(Database database)
{
    private const string WorkSelect = """
        SELECT w.id, w.title, w.subtitle, w.kind,
               CASE
                   WHEN SUM(CASE WHEN e.started_on IS NOT NULL AND e.completed_on IS NULL THEN 1 ELSE 0 END) > 0 THEN 'in_progress'
                   WHEN SUM(CASE WHEN e.completed_on IS NOT NULL THEN 1 ELSE 0 END) > 0 THEN 'completed'
                   ELSE 'planned'
               END AS status,
               w.total_episodes, w.created_at, w.updated_at,
               SUM(CASE WHEN e.completed_on IS NOT NULL THEN 1 ELSE 0 END) AS experience_count,
               SUM(CASE WHEN e.started_on IS NOT NULL AND e.completed_on IS NULL THEN 1 ELSE 0 END) AS active_count,
               SUM(CASE WHEN e.completed_on IS NOT NULL AND e.allure IS NOT NULL AND e.immersion IS NOT NULL
                             AND e.rationality IS NOT NULL AND e.illumination IS NOT NULL
                        THEN 1 ELSE 0 END) AS rated_count,
               ROUND(AVG(CASE WHEN e.completed_on IS NOT NULL
                              THEN calculate_rank(e.allure, e.immersion, e.rationality, e.illumination) END), 1) AS aggregate_rank,
               MAX(COALESCE(
                   (SELECT MAX(p.logged_on) FROM progress_entries p WHERE p.experience_id = e.id),
                   e.completed_on, e.started_on, substr(e.created_at, 1, 10))) AS latest_activity,
               (SELECT c.file_name FROM work_covers c WHERE c.work_id = w.id
                ORDER BY c.sort_order, c.created_at LIMIT 1) AS primary_cover_file
        FROM works w
        LEFT JOIN experiences e ON e.work_id = w.id
        """;

    public async Task<IReadOnlyList<MediaWork>> GetWorksAsync()
    {
        var works = new List<MediaWork>();
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = WorkSelect + "\n" + """
            GROUP BY w.id
            ORDER BY COALESCE(latest_activity, substr(w.created_at, 1, 10)) DESC, w.created_at DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            works.Add(ReadWork(reader));
        }
        return works;
    }

    public async Task<MediaWork?> GetWorkAsync(string id)
    {
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = WorkSelect + "\n" + """
            WHERE w.id = $id
            GROUP BY w.id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadWork(reader) : null;
    }

    public async Task<IReadOnlyList<MediaExperience>> GetExperiencesAsync(string workId)
    {
        var experiences = new List<MediaExperience>();
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.work_id, e.started_on, e.completed_on, e.allure, e.immersion, e.rationality, e.illumination,
                   e.notes, e.created_at, e.updated_at,
                   (SELECT COUNT(DISTINCT p.logged_on) FROM progress_entries p WHERE p.experience_id = e.id),
                   COALESCE((SELECT SUM(p.amount) FROM progress_entries p WHERE p.experience_id = e.id AND p.metric = 'duration'), 0),
                   COALESCE((SELECT SUM(p.amount) FROM progress_entries p WHERE p.experience_id = e.id AND p.metric = 'episodes'), 0),
                   w.total_episodes
            FROM experiences e
            JOIN works w ON w.id = e.work_id
            WHERE e.work_id = $workId
            ORDER BY COALESCE(e.completed_on, e.started_on, substr(e.created_at, 1, 10)) DESC, e.created_at DESC;
            """;
        command.Parameters.AddWithValue("$workId", workId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            experiences.Add(new MediaExperience
            {
                Id = reader.GetString(0),
                WorkId = reader.GetString(1),
                StartedOn = reader.IsDBNull(2) ? null : DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                CompletedOn = reader.IsDBNull(3) ? null : DateOnly.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                Allure = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Immersion = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Rationality = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Illumination = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                ProgressEntryCount = reader.GetInt32(11),
                TotalMinutes = reader.GetInt32(12),
                TotalEpisodes = reader.GetInt32(13),
                AvailableEpisodes = reader.IsDBNull(14) ? null : reader.GetInt32(14)
            });
        }
        return experiences;
    }

    public async Task AddWorkAsync(MediaWork work)
    {
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO works (id, title, subtitle, kind, status, total_episodes, created_at, updated_at)
            VALUES ($id, $title, $subtitle, $kind, $status, $totalEpisodes, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", work.Id);
        command.Parameters.AddWithValue("$title", work.Title);
        command.Parameters.AddWithValue("$subtitle", (object?)work.Subtitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", work.Kind);
        command.Parameters.AddWithValue("$status", (object?)work.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("$totalEpisodes", work.TotalEpisodes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", work.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", work.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateWorkMetadataAsync(MediaWork work)
    {
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE works
            SET title = $title, subtitle = $subtitle, kind = $kind, updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", work.Id);
        command.Parameters.AddWithValue("$title", work.Title);
        command.Parameters.AddWithValue("$subtitle", (object?)work.Subtitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", work.Kind);
        command.Parameters.AddWithValue("$updatedAt", work.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<WorkCover>> GetCoversAsync(string workId)
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
        return covers;
    }

    public async Task AddCoversAsync(string workId, IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
        {
            return;
        }

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
        var existing = await GetCoversAsync(workId);
        if (existing.Count + sourcePaths.Count > 20)
        {
            throw new InvalidOperationException("每部作品最多保存 20 张封面。");
        }

        var coverDirectory = database.GetCoverDirectory(workId);
        Directory.CreateDirectory(coverDirectory);
        var staged = new List<WorkCover>();
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
                var fileName = coverId + source.Extension.ToLowerInvariant();
                var destination = database.GetCoverFilePath(workId, fileName);
                await using (var input = source.OpenRead())
                await using (var output = File.Create(destination))
                {
                    await input.CopyToAsync(output);
                }
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
            foreach (var cover in staged)
            {
                if (File.Exists(cover.FilePath))
                {
                    File.Delete(cover.FilePath);
                }
            }
            throw;
        }
    }

    public Task SetPrimaryCoverAsync(string workId, string coverId) => ReorderCoverAsync(workId, coverId, 0, absolute: true);

    public Task MoveCoverAsync(string workId, string coverId, int offset) => ReorderCoverAsync(workId, coverId, offset, absolute: false);

    public async Task DeleteCoverAsync(string workId, string coverId)
    {
        var covers = await GetCoversAsync(workId);
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
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Move(temporaryPath, cover.FilePath, overwrite: true);
            }
            throw;
        }
    }

    public async Task<MediaExperience?> GetActiveExperienceAsync(string workId) =>
        (await GetExperiencesAsync(workId)).FirstOrDefault(experience =>
            experience.StartedOn is not null && experience.CompletedOn is null);

    public async Task<IReadOnlyList<ProgressEntry>> GetProgressEntriesAsync(string experienceId)
    {
        var entries = new List<ProgressEntry>();
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, experience_id, logged_on, metric, amount, notes, created_at, updated_at
            FROM progress_entries
            WHERE experience_id = $experienceId
            ORDER BY logged_on DESC, created_at DESC;
            """;
        command.Parameters.AddWithValue("$experienceId", experienceId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new ProgressEntry
            {
                Id = reader.GetString(0),
                ExperienceId = reader.GetString(1),
                LoggedOn = DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                Metric = reader.GetString(3),
                Amount = reader.GetInt32(4),
                Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
            });
        }
        return entries;
    }

    public async Task AddProgressEntryAsync(ProgressEntry entry, string workId, int? totalEpisodes = null)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;
        insert.CommandText = """
            INSERT INTO progress_entries (id, experience_id, logged_on, metric, amount, notes, created_at, updated_at)
            VALUES ($id, $experienceId, $loggedOn, $metric, $amount, $notes, $createdAt, $updatedAt);
            """;
        insert.Parameters.AddWithValue("$id", entry.Id);
        insert.Parameters.AddWithValue("$experienceId", entry.ExperienceId);
        insert.Parameters.AddWithValue("$loggedOn", entry.LoggedOn.ToString("yyyy-MM-dd"));
        insert.Parameters.AddWithValue("$metric", entry.Metric);
        insert.Parameters.AddWithValue("$amount", entry.Amount);
        insert.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        insert.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        await insert.ExecuteNonQueryAsync();

        var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = "UPDATE works SET total_episodes = COALESCE($totalEpisodes, total_episodes), updated_at = $updatedAt WHERE id = $workId;";
        update.Parameters.AddWithValue("$totalEpisodes", totalEpisodes ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        update.Parameters.AddWithValue("$workId", workId);
        await update.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task UpdateProgressEntryAsync(ProgressEntry entry, string workId, int? totalEpisodes = null)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var updateEntry = connection.CreateCommand();
        updateEntry.Transaction = (SqliteTransaction)transaction;
        updateEntry.CommandText = """
            UPDATE progress_entries SET logged_on=$loggedOn, metric=$metric, amount=$amount,
                notes=$notes, updated_at=$updatedAt
            WHERE id=$id AND experience_id=$experienceId;
            """;
        updateEntry.Parameters.AddWithValue("$id", entry.Id);
        updateEntry.Parameters.AddWithValue("$experienceId", entry.ExperienceId);
        updateEntry.Parameters.AddWithValue("$loggedOn", entry.LoggedOn.ToString("yyyy-MM-dd"));
        updateEntry.Parameters.AddWithValue("$metric", entry.Metric);
        updateEntry.Parameters.AddWithValue("$amount", entry.Amount);
        updateEntry.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        updateEntry.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        await updateEntry.ExecuteNonQueryAsync();
        var updateWork = connection.CreateCommand();
        updateWork.Transaction = (SqliteTransaction)transaction;
        updateWork.CommandText = "UPDATE works SET total_episodes=COALESCE($totalEpisodes, total_episodes), updated_at=$updatedAt WHERE id=$workId;";
        updateWork.Parameters.AddWithValue("$totalEpisodes", totalEpisodes ?? (object)DBNull.Value);
        updateWork.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        updateWork.Parameters.AddWithValue("$workId", workId);
        await updateWork.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteProgressEntryAsync(string entryId, string workId)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var delete = connection.CreateCommand();
        delete.Transaction = (SqliteTransaction)transaction;
        delete.CommandText = "DELETE FROM progress_entries WHERE id=$id;";
        delete.Parameters.AddWithValue("$id", entryId);
        await delete.ExecuteNonQueryAsync();
        await TouchWorkAsync(connection, (SqliteTransaction)transaction, workId, DateTimeOffset.Now);
        await transaction.CommitAsync();
    }

    public async Task AddExperienceAsync(MediaExperience experience)
    {
        RatingScale.Validate(experience);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;
        insert.CommandText = """
            INSERT INTO experiences
                (id, work_id, started_on, completed_on, allure, immersion, rationality, illumination, notes, created_at, updated_at)
            VALUES
                ($id, $workId, $startedOn, $completedOn, $allure, $immersion, $rationality, $illumination, $notes, $createdAt, $updatedAt);
            """;
        insert.Parameters.AddWithValue("$id", experience.Id);
        insert.Parameters.AddWithValue("$workId", experience.WorkId);
        insert.Parameters.AddWithValue("$startedOn", experience.StartedOn?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$completedOn", experience.CompletedOn?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$allure", experience.Allure ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$immersion", experience.Immersion ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$rationality", experience.Rationality ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$illumination", experience.Illumination ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("$notes", (object?)experience.Notes ?? DBNull.Value);
        insert.Parameters.AddWithValue("$createdAt", experience.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("$updatedAt", experience.UpdatedAt.ToString("O"));
        await insert.ExecuteNonQueryAsync();

        await RefreshWorkStatusAsync(
            connection,
            (SqliteTransaction)transaction,
            experience.WorkId,
            experience.UpdatedAt);
        await transaction.CommitAsync();
    }

    public async Task UpdateExperienceAsync(MediaExperience experience)
    {
        RatingScale.Validate(experience);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE experiences SET started_on=$startedOn, completed_on=$completedOn,
                allure=$allure, immersion=$immersion, rationality=$rationality, illumination=$illumination,
                notes=$notes, updated_at=$updatedAt WHERE id=$id AND work_id=$workId;
            """;
        command.Parameters.AddWithValue("$id", experience.Id);
        command.Parameters.AddWithValue("$workId", experience.WorkId);
        command.Parameters.AddWithValue("$startedOn", experience.StartedOn?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedOn", experience.CompletedOn?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$allure", experience.Allure ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$immersion", experience.Immersion ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$rationality", experience.Rationality ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$illumination", experience.Illumination ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)experience.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", experience.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();

        await RefreshWorkStatusAsync(
            connection,
            (SqliteTransaction)transaction,
            experience.WorkId,
            experience.UpdatedAt);
        await transaction.CommitAsync();
    }

    public async Task DeleteExperienceAsync(string experienceId, string workId)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var delete = connection.CreateCommand();
        delete.Transaction = (SqliteTransaction)transaction;
        delete.CommandText = "DELETE FROM experiences WHERE id=$id AND work_id=$workId;";
        delete.Parameters.AddWithValue("$id", experienceId);
        delete.Parameters.AddWithValue("$workId", workId);
        await delete.ExecuteNonQueryAsync();

        await RefreshWorkStatusAsync(
            connection,
            (SqliteTransaction)transaction,
            workId,
            DateTimeOffset.Now);
        await transaction.CommitAsync();
    }

    public async Task DeleteWorkAsync(string workId)
    {
        var coverDirectory = database.GetCoverDirectory(workId);
        var temporaryDirectory = coverDirectory + ".deleting-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(coverDirectory))
        {
            Directory.Move(coverDirectory, temporaryDirectory);
        }
        try
        {
            await using var connection = await OpenAsync();
            var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM works WHERE id=$id;";
            delete.Parameters.AddWithValue("$id", workId);
            await delete.ExecuteNonQueryAsync();
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Move(temporaryDirectory, coverDirectory);
            }
            throw;
        }
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async Task ReorderCoverAsync(string workId, string coverId, int position, bool absolute)
    {
        var covers = await GetCoversAsync(workId);
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

    private static async Task TouchWorkAsync(SqliteConnection connection, SqliteTransaction transaction, string workId, DateTimeOffset updatedAt)
    {
        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE works SET updated_at=$updatedAt WHERE id=$workId;";
        update.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("O"));
        update.Parameters.AddWithValue("$workId", workId);
        await update.ExecuteNonQueryAsync();
    }

    private static async Task RefreshWorkStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workId,
        DateTimeOffset updatedAt)
    {
        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE works SET
                status = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM experiences
                        WHERE work_id=$workId AND started_on IS NOT NULL AND completed_on IS NULL
                    ) THEN 'in_progress'
                    WHEN EXISTS (
                        SELECT 1 FROM experiences
                        WHERE work_id=$workId AND completed_on IS NOT NULL
                    ) THEN 'completed'
                    ELSE 'planned'
                END,
                updated_at=$updatedAt
            WHERE id=$workId;
            """;
        update.Parameters.AddWithValue("$workId", workId);
        update.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("O"));
        await update.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        connection.CreateFunction<int?, int?, int?, int?, double?>(
            "calculate_rank",
            RatingScale.Calculate);
        var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
        return connection;
    }

    private MediaWork ReadWork(SqliteDataReader reader)
    {
        double? aggregate = reader.IsDBNull(11) ? null : reader.GetDouble(11);
        string? primaryCoverPath = null;
        if (!reader.IsDBNull(13))
        {
            var candidate = database.GetCoverFilePath(reader.GetString(0), reader.GetString(13));
            if (File.Exists(candidate))
            {
                primaryCoverPath = candidate;
            }
        }
        return new MediaWork
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Subtitle = reader.IsDBNull(2) ? null : reader.GetString(2),
            Kind = reader.GetString(3),
            Status = reader.IsDBNull(4) ? null : reader.GetString(4),
            TotalEpisodes = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            PrimaryCoverPath = primaryCoverPath,
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            ExperienceCount = reader.GetInt32(8),
            HasActiveExperience = reader.GetInt32(9) > 0,
            RatedExperienceCount = reader.GetInt32(10),
            AggregateRank = aggregate is null ? null : Math.Round(aggregate.Value, 1, MidpointRounding.AwayFromZero),
            LatestActivityOn = reader.IsDBNull(12) ? null : DateOnly.Parse(reader.GetString(12), CultureInfo.InvariantCulture)
        };
    }
}
