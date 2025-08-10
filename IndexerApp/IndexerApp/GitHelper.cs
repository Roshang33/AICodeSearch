using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class GitHelper
{
    private readonly string _localPath;
    private readonly string _repoUrl;

    public GitHelper(string localPath, string repoUrl)
    {
        _localPath = localPath;
        _repoUrl = repoUrl;
    }

    public async Task EnsureRepoFetchedAsync()
    {
        if (!Directory.Exists(_localPath) || !Repository.IsValid(_localPath))
        {
            if (Directory.Exists(_localPath)) Directory.Delete(_localPath, true);
            Console.WriteLine("Cloning repository...");
            Repository.Clone(_repoUrl, _localPath);
        }
        else
        {
            Console.WriteLine("Fetching updates...");
            using var repo = new Repository(_localPath);
            foreach (var remote in repo.Network.Remotes)
            {
                var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                Commands.Fetch(repo, remote.Name, refSpecs, null, null);
            }
            // optional: fast-forward merge/pull local branch
            var signature = new Signature("indexer", "indexer@example.com", DateTimeOffset.Now);
            Commands.Pull(repo, signature, new PullOptions());
        }
    }

    public string GetHeadCommitSha()
    {
        using var repo = new Repository(_localPath);
        return repo.Head.Tip?.Sha ?? string.Empty;
    }

    public IEnumerable<(string Path, ChangeKind Status)> GetDiffSince(string lastCommitSha)
    {
        using var repo = new Repository(_localPath);
        var head = repo.Head.Tip;
        if (string.IsNullOrEmpty(lastCommitSha))
        {
            // first time: return all tracked files as Added
            return repo.Index.Select(e => (e.Path, ChangeKind.Added)).ToList();
        }

        var oldCommit = repo.Lookup<Commit>(lastCommitSha);
        if (oldCommit == null)
        {
            return repo.Index.Select(e => (e.Path, ChangeKind.Added)).ToList();
        }

        var changes = repo.Diff.Compare<TreeChanges>(oldCommit.Tree, head.Tree);
        return changes.Select(c => (c.Path, c.Status));
    }

    public enum ChangeKind { Added, Modified, Deleted }
}
