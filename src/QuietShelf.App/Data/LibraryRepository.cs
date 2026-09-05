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
               COUNT(p.id)
        FROM experiences e
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
            var primaryCoverPath = ReadCoverPath(reader, 1, 10);

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
                var primaryCoverPath = ReadCoverPath(reader, 0, 9);
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

    private string? ReadCoverPath(SqliteDataReader reader, int workIdColumn, int fileNameColumn)
    {
        if (reader.IsDBNull(fileNameColumn)) return null;
        var path = database.GetCoverFilePath(reader.GetString(workIdColumn), reader.GetString(fileNameColumn));
        return File.Exists(path) ? path : null;
    }
    private MediaWork ReadWork(SqliteDataReader reader)
    {
        double? aggregate = reader.IsDBNull(11) ? null : reader.GetDouble(11);
        var primaryCoverPath = ReadCoverPath(reader, 0, 13);
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
        ProgressEntryCount = reader.GetInt32(11)
    };
}
