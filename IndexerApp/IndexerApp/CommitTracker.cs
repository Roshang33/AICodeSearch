using Azure;
using Azure.Data.Tables;
using System;
using System.Threading.Tasks;

public class CommitEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "RepoCommit";
    public string RowKey { get; set; } // repoUrl (use sanitized)
    public string CommitSha { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public Azure.ETag ETag { get; set; }
}

public class CommitTracker
{
    private readonly TableClient _table;

    public CommitTracker(string connectionString, string tableName = "GitRepoCommits")
    {
        _table = new TableClient(connectionString, tableName);
        _table.CreateIfNotExists();
    }

    private static string RowKeyForUrl(string repoUrl) => Utilities.SanitizeKey(repoUrl);

    public async Task<string?> GetLastCommitShaAsync(string repoUrl)
    {
        try
        {
            var rowKey = RowKeyForUrl(repoUrl);
            var resp = await _table.GetEntityAsync<CommitEntity>("RepoCommit", rowKey);
            return resp.Value.CommitSha;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SetLastCommitShaAsync(string repoUrl, string sha)
    {
        var ent = new CommitEntity
        {
            RowKey = RowKeyForUrl(repoUrl),
            CommitSha = sha
        };
        await _table.UpsertEntityAsync(ent);
    }
}
