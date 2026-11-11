using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Core.Database.Repositories
{
    public class MetadataRepository
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock;

        public MetadataRepository(string connectionString, SemaphoreSlim dbLock)
        {
            _connectionString = connectionString;
            _dbLock = dbLock;
        }

        public async Task<List<MetadataResult>> GetMetadataPoolForQueryAsync(string query)
        {
            var list = new List<MetadataResult>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ImdbId, Title, PosterUrl, BackdropUrl, Overview, [Cast], Director, TrailerUrl, ReleaseDate, VoteAverage, MediaType FROM MetadataPool WHERE SearchQuery = @q";
                cmd.Parameters.AddWithValue("@q", query.ToLowerInvariant());
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new MetadataResult {
                        ImdbId = reader.GetString(0), Title = reader.GetString(1), PosterUrl = reader.GetString(2),
                        BackdropUrl = reader.GetString(3), Overview = reader.GetString(4), Cast = reader.GetString(5),
                        Director = reader.IsDBNull(6) ? "" : reader.GetString(6), TrailerUrl = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        ReleaseDate = reader.IsDBNull(8) ? "" : reader.GetString(8), VoteAverage = reader.GetDouble(9),
                        MediaType = reader.IsDBNull(10) ? "" : reader.GetString(10)
                    });
                }
            }
            return list;
        }

        public async Task SaveMetadataPoolResultsAsync(string query, List<MetadataResult> results)
        {
            if (results == null || results.Count == 0) return;
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    foreach (var item in results)
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = "INSERT INTO MetadataPool (SearchQuery, ImdbId, Title, PosterUrl, BackdropUrl, Overview, [Cast], Director, TrailerUrl, ReleaseDate, VoteAverage, CreatedAt, MediaType) VALUES (@q, @imdb, @t, @p, @b, @o, @c, @d, @tr, @rd, @v, @ca, @mt)";
                        cmd.Parameters.AddWithValue("@q", query.ToLowerInvariant()); cmd.Parameters.AddWithValue("@imdb", item.ImdbId ?? "");
                        cmd.Parameters.AddWithValue("@t", item.Title ?? ""); cmd.Parameters.AddWithValue("@p", item.PosterUrl ?? "");
                        cmd.Parameters.AddWithValue("@b", item.BackdropUrl ?? ""); cmd.Parameters.AddWithValue("@o", item.Overview ?? "");
                        cmd.Parameters.AddWithValue("@c", item.Cast ?? ""); cmd.Parameters.AddWithValue("@d", item.Director ?? "");
                        cmd.Parameters.AddWithValue("@tr", item.TrailerUrl ?? ""); cmd.Parameters.AddWithValue("@rd", item.ReleaseDate ?? "");
                        cmd.Parameters.AddWithValue("@v", item.VoteAverage); cmd.Parameters.AddWithValue("@ca", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@mt", item.MediaType ?? "");
                        await cmd.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
            }
            finally { _dbLock.Release(); }
        }
    }
}
