using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using QuietShelf.Models;

namespace QuietShelf.Data;

public sealed partial class LibraryRepository(Database database)
{
    private const string ExperienceSelect = """
        SELECT e.id, e.work_id, e.started_on, e.completed_on, e.allure, e.immersion, e.rationality, e.illumination,
               e.notes, e.created_at, e.updated_at,
               COUNT(DISTINCT p.logged_on),
               COUNT(p.id),
               COALESCE(SUM(CASE WHEN p.metric = 'duration' THEN p.amount ELSE 0 END), 0),
               COALESCE(SUM(CASE WHEN p.metric = 'episodes' THEN p.amount ELSE 0 END), 0),
               w.total_episodes
        FROM experiences e
        JOIN works w ON w.id = e.work_id
        LEFT JOIN progress_entries p ON p.experience_id = e.id
        """;

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
               MAX(e.completed_on) AS latest_activity,
               (SELECT c.file_name FROM work_covers c WHERE c.work_id = w.id
                 ORDER BY c.sort_order, c.created_at LIMIT 1) AS primary_cover_file,
               w.author
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
        command.CommandText = ExperienceSelect + "\n" + """
            WHERE e.work_id = $workId
            GROUP BY e.id
            ORDER BY COALESCE(e.completed_on, e.started_on, substr(e.created_at, 1, 10)) DESC, e.created_at DESC;
            """;
        command.Parameters.AddWithValue("$workId", workId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            experiences.Add(ReadExperience(reader));
        }
        return experiences;
    }

    public async Task<IReadOnlyDictionary<string, MediaExperience>> GetActiveExperiencesAsync()
    {
        var experiences = new Dictionary<string, MediaExperience>(StringComparer.Ordinal);
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = ExperienceSelect + "\n" + """
            WHERE e.started_on IS NOT NULL AND e.completed_on IS NULL
            GROUP BY e.id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var experience = ReadExperience(reader);
            experiences[experience.WorkId] = experience;
        }
        return experiences;
    }

    public async Task AddWorkAsync(MediaWork work)
    {
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO works (id, title, subtitle, author, kind, status, total_episodes, created_at, updated_at)
            VALUES ($id, $title, $subtitle, $author, $kind, $status, $totalEpisodes, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", work.Id);
        command.Parameters.AddWithValue("$title", work.Title);
        command.Parameters.AddWithValue("$subtitle", (object?)work.Subtitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", work.Kind == "book" ? (object?)work.Author ?? DBNull.Value : DBNull.Value);
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
            SET title = $title, subtitle = $subtitle, author = $author, kind = $kind, updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", work.Id);
        command.Parameters.AddWithValue("$title", work.Title);
        command.Parameters.AddWithValue("$subtitle", (object?)work.Subtitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", work.Kind == "book" ? (object?)work.Author ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("$kind", work.Kind);
        command.Parameters.AddWithValue("$updatedAt", work.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<MediaExperience?> GetActiveExperienceAsync(string workId)
    {
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = ExperienceSelect + "\n" + """
            WHERE e.work_id = $workId AND e.started_on IS NOT NULL AND e.completed_on IS NULL
            GROUP BY e.id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workId", workId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadExperience(reader) : null;
    }

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

    public async Task<IReadOnlyList<DashboardTimelineItem>> GetRecentTimelineAsync(int limit = 5)
    {
        var items = new List<DashboardTimelineItem>();
        await using var connection = await OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            WITH activity AS (
                SELECT e.id, e.work_id, e.completed_on AS event_on, 'completion' AS event_type,
                       'completion' AS metric, 0 AS amount, e.notes, e.updated_at AS event_created
                FROM experiences e
                WHERE e.completed_on IS NOT NULL
            )
            SELECT a.id, a.work_id, w.title, w.kind, a.event_on, a.event_type,
                   a.metric, a.amount, a.notes, a.event_created,
                   (SELECT c.file_name FROM work_covers c WHERE c.work_id = w.id
                    ORDER BY c.sort_order, c.created_at LIMIT 1) AS primary_cover_file
            FROM activity a
            JOIN works w ON w.id = a.work_id
            ORDER BY a.event_on DESC, a.event_created DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 8));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string? primaryCoverPath = null;
            if (!reader.IsDBNull(10))
            {
                var candidate = database.GetCoverFilePath(reader.GetString(1), reader.GetString(10));
                if (File.Exists(candidate))
                {
                    primaryCoverPath = candidate;
                }
            }

            items.Add(new DashboardTimelineItem
            {
                Id = reader.GetString(0),
                WorkId = reader.GetString(1),
                Title = reader.GetString(2),
                Kind = reader.GetString(3),
                LoggedOn = DateOnly.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                EventType = reader.GetString(5),
                Metric = reader.GetString(6),
                Amount = reader.GetInt32(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                PrimaryCoverPath = primaryCoverPath,
                IsLatest = items.Count == 0
            });
        }
        return items;
    }

    public async Task<DashboardShowcase> GetDashboardShowcaseAsync()
    {
        var works = new List<DashboardShowcaseItem>();
        var authors = new List<DashboardAuthorRank>();
        await using var connection = await OpenAsync();

        var worksCommand = connection.CreateCommand();
        worksCommand.CommandText = """
            SELECT w.id, w.title, w.kind, w.author,
                   COUNT(e.id) AS completion_count,
                   SUM(CASE WHEN e.allure IS NOT NULL AND e.immersion IS NOT NULL
                                 AND e.rationality IS NOT NULL AND e.illumination IS NOT NULL
                            THEN 1 ELSE 0 END) AS rating_count,
                   AVG(calculate_rank(e.allure, e.immersion, e.rationality, e.illumination)) AS aggregate_rank,
                   MIN(e.completed_on) AS first_completed_on,
                   MAX(e.completed_on) AS latest_completed_on,
                   (SELECT c.file_name FROM work_covers c WHERE c.work_id = w.id
                    ORDER BY c.sort_order, c.created_at LIMIT 1) AS primary_cover_file
            FROM works w
            JOIN experiences e ON e.work_id = w.id AND e.completed_on IS NOT NULL
            GROUP BY w.id
            ORDER BY latest_completed_on DESC, w.title;
            """;
        await using (var reader = await worksCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string? primaryCoverPath = null;
                if (!reader.IsDBNull(9))
                {
                    var candidate = database.GetCoverFilePath(reader.GetString(0), reader.GetString(9));
                    if (File.Exists(candidate))
                    {
                        primaryCoverPath = candidate;
                    }
                }
                works.Add(new DashboardShowcaseItem
                {
                    WorkId = reader.GetString(0),
                    Title = reader.GetString(1),
                    Kind = reader.GetString(2),
                    Author = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CompletionCount = reader.GetInt32(4),
                    RatingCount = reader.GetInt32(5),
                    AggregateRank = reader.IsDBNull(6) ? null : Math.Round(reader.GetDouble(6), 1, MidpointRounding.AwayFromZero),
                    FirstCompletedOn = DateOnly.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                    LatestCompletedOn = DateOnly.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                    PrimaryCoverPath = primaryCoverPath
                });
            }
        }

        var authorsCommand = connection.CreateCommand();
        authorsCommand.CommandText = """
            WITH rated AS (
                SELECT w.id AS work_id, TRIM(w.author) AS author,
                       calculate_rank(e.allure, e.immersion, e.rationality, e.illumination) AS rank
                FROM experiences e
                JOIN works w ON w.id = e.work_id
                WHERE w.kind = 'book' AND e.completed_on IS NOT NULL
                  AND TRIM(COALESCE(w.author, '')) <> ''
                  AND e.allure IS NOT NULL AND e.immersion IS NOT NULL
                  AND e.rationality IS NOT NULL AND e.illumination IS NOT NULL
            ), global_mean AS (
                SELECT AVG(rank) AS mean_rank FROM rated
            ), author_stats AS (
                SELECT author, COUNT(DISTINCT work_id) AS work_count,
                       COUNT(*) AS rating_count, AVG(rank) AS mean_rank
                FROM rated
                GROUP BY author
            )
            SELECT author, work_count, rating_count,
                   ((rating_count * author_stats.mean_rank) + (2.0 * global_mean.mean_rank)) / (rating_count + 2.0) AS weighted_rank
            FROM author_stats CROSS JOIN global_mean
            ORDER BY weighted_rank DESC, rating_count DESC, author
            LIMIT 3;
            """;
        await using (var reader = await authorsCommand.ExecuteReaderAsync())
        {
            var position = 1;
            while (await reader.ReadAsync())
            {
                authors.Add(new DashboardAuthorRank
                {
                    Position = position++,
                    Author = reader.GetString(0),
                    WorkCount = reader.GetInt32(1),
                    RatingCount = reader.GetInt32(2),
                    WeightedRank = Math.Round(reader.GetDouble(3), 1, MidpointRounding.AwayFromZero)
                });
            }
        }

        return new DashboardShowcase { CompletedWorks = works, TopAuthors = authors };
    }

    public async Task AddProgressEntryAsync(ProgressEntry entry, int? totalEpisodes = null)
    {
        await using var connection = await OpenAsync();
        await ValidateProgressEntryAsync(connection, entry, totalEpisodes);
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
        update.CommandText = """
            UPDATE works
            SET total_episodes = COALESCE($totalEpisodes, total_episodes), updated_at = $updatedAt
            WHERE id = (SELECT work_id FROM experiences WHERE id = $experienceId);
            """;
        update.Parameters.AddWithValue("$totalEpisodes", totalEpisodes ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        update.Parameters.AddWithValue("$experienceId", entry.ExperienceId);
        await update.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task UpdateProgressEntryAsync(ProgressEntry entry, int? totalEpisodes = null)
    {
        await using var connection = await OpenAsync();
        await ValidateProgressEntryAsync(connection, entry, totalEpisodes);
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
        if (await updateEntry.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException("The progress entry no longer exists for this experience.");
        }
        var updateWork = connection.CreateCommand();
        updateWork.Transaction = (SqliteTransaction)transaction;
        updateWork.CommandText = """
            UPDATE works
            SET total_episodes=COALESCE($totalEpisodes, total_episodes), updated_at=$updatedAt
            WHERE id = (SELECT work_id FROM experiences WHERE id = $experienceId);
            """;
        updateWork.Parameters.AddWithValue("$totalEpisodes", totalEpisodes ?? (object)DBNull.Value);
        updateWork.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        updateWork.Parameters.AddWithValue("$experienceId", entry.ExperienceId);
        await updateWork.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteProgressEntryAsync(string entryId)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var findWork = connection.CreateCommand();
        findWork.Transaction = (SqliteTransaction)transaction;
        findWork.CommandText = """
            SELECT experience.work_id
            FROM progress_entries AS progress
            JOIN experiences AS experience ON experience.id = progress.experience_id
            WHERE progress.id = $id;
            """;
        findWork.Parameters.AddWithValue("$id", entryId);
        var workId = await findWork.ExecuteScalarAsync() as string;
        if (workId is null)
        {
            return;
        }

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
        ValidateExperienceChronology(experience);
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
        ValidateExperienceChronology(experience);
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
        using var coverLock = await AcquireCoverLockAsync(workId);
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

    private static void ValidateExperienceChronology(MediaExperience experience)
    {
        if (experience.StartedOn is { } startedOn
            && experience.CompletedOn is { } completedOn
            && completedOn < startedOn)
        {
            throw new InvalidOperationException("Completion date cannot be earlier than the start date.");
        }
    }

    private static async Task ValidateProgressEntryAsync(
        SqliteConnection connection,
        ProgressEntry entry,
        int? totalEpisodes)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.started_on, w.kind
            FROM experiences e
            JOIN works w ON w.id = e.work_id
            WHERE e.id = $experienceId;
            """;
        command.Parameters.AddWithValue("$experienceId", entry.ExperienceId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The progress entry does not belong to an existing experience.");
        }

        if (reader.IsDBNull(0))
        {
            throw new InvalidOperationException("Progress requires an experience start date.");
        }

        var startedOn = DateOnly.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        if (entry.LoggedOn < startedOn)
        {
            throw new InvalidOperationException("Progress date cannot be earlier than the experience start date.");
        }

        var workKind = reader.GetString(1);
        if (string.Equals(entry.Metric, "episodes", StringComparison.Ordinal)
            && !string.Equals(workKind, "screen", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Episode progress is only available for screen works.");
        }
        if (totalEpisodes is not null && !string.Equals(workKind, "screen", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Episode totals are only available for screen works.");
        }
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
            Author = reader.IsDBNull(14) ? null : reader.GetString(14),
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

    private static MediaExperience ReadExperience(SqliteDataReader reader) => new()
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
        ProgressDayCount = reader.GetInt32(11),
        ProgressEntryCount = reader.GetInt32(12),
        TotalMinutes = reader.GetInt32(13),
        TotalEpisodes = reader.GetInt32(14),
        AvailableEpisodes = reader.IsDBNull(15) ? null : reader.GetInt32(15)
    };
}
