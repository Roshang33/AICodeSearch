using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Config (from env)
        var repoUrl = args.Length > 0 ? args[0] : throw new ArgumentException("Provide repo URL as first arg");
        var localRoot = Path.Combine(Path.GetTempPath(), "git-indexer");
        Directory.CreateDirectory(localRoot);

        var searchEndpoint = Environment.GetEnvironmentVariable("SEARCH_ENDPOINT")!;
        var searchApiKey = Environment.GetEnvironmentVariable("SEARCH_API_KEY")!;
        var storageConn = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")!;
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
        var indexName = Environment.GetEnvironmentVariable("INDEX_NAME") ?? "code-index";
        int maxParallel = int.TryParse(Environment.GetEnvironmentVariable("MAX_PARALLELISM"), out var m) ? m : 6;
        int batchSize = int.TryParse(Environment.GetEnvironmentVariable("BATCH_SIZE"), out var b) ? b : 64;
        int chunkSize = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_SIZE"), out var c) ? c : 3000;

        Console.WriteLine($"Indexing repo {repoUrl} -> index {indexName}");
        var repoPath = Path.Combine(localRoot, Utilities.SanitizeFilename(repoUrl));

        var git = new GitHelper(repoPath, repoUrl);
        await git.EnsureRepoFetchedAsync();

        var tracker = new CommitTracker(storageConn, tableName: "GitRepoCommits");
        var lastSha = await tracker.GetLastCommitShaAsync(repoUrl);
        var headSha = git.GetHeadCommitSha();

        if (lastSha == headSha)
        {
            Console.WriteLine("No new commits since last index. Exiting.");
            return 0;
        }

        // compute changed files and deleted files
        var diff = git.GetDiffSince(lastSha);
        var addedOrModified = diff.Where(d => d.Status == LibGit2Sharp.ChangeKind.Added || d.Status == LibGit2Sharp.ChangeKind.Modified)
                                  .Select(d => Path.Combine(repoPath, d.Path)).Where(File.Exists).ToList();
        var deleted = diff.Where(d => d.Status == LibGit2Sharp.ChangeKind.Deleted).Select(d => d.Path).ToList();

        Console.WriteLine($"Changed files: {addedOrModified.Count}, Deleted files: {deleted.Count}");

        var embedSvc = new EmbeddingService(openAiKey);
        var indexer = new SearchIndexer(searchEndpoint, searchApiKey, indexName);
        await indexer.EnsureIndexExistsAsync(vectorDimensions: 1536); // adjust dims to model

        // Producer/consumer pattern: process files in parallel, batch upload
        var uploadQueue = new ConcurrentQueue<IndexActionWrapper>();
        var cts = new CancellationTokenSource();

        // consumer task: flush queue to Azure Search in batches
        var consumer = Task.Run(async () =>
        {
            var buffer = new List<IndexActionWrapper>(batchSize);
            while (!cts.IsCancellationRequested || !uploadQueue.IsEmpty)
            {
                while (buffer.Count < batchSize && uploadQueue.TryDequeue(out var item))
                {
                    buffer.Add(item);
                }

                if (buffer.Count > 0)
                {
                    await indexer.UploadBatchAsync(buffer);
                    buffer.Clear();
                }
                else
                {
                    await Task.Delay(300);
                }
            }

            // flush remaining
            while (uploadQueue.TryDequeue(out var item))
            {
                buffer.Add(item);
                if (buffer.Count >= batchSize)
                {
                    await indexer.UploadBatchAsync(buffer);
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0) await indexer.UploadBatchAsync(buffer);
        }, cts.Token);

        // process files in parallel with throttling
        var throttler = new SemaphoreSlim(maxParallel);
        var producers = addedOrModified.Select(async path =>
        {
            await throttler.WaitAsync();
            try
            {
                var text = await File.ReadAllTextAsync(path);
                if (string.IsNullOrWhiteSpace(text)) return;

                // chunk large files into smaller passages
                var chunks = Utilities.ChunkText(text, chunkSize);
                int chunkIndex = 0;
                foreach (var chunk in chunks)
                {
                    // generate embedding with retry policy inside service
                    var vec = await embedSvc.GetEmbeddingAsync(chunk);

                    var id = Utilities.IdForPath(repoUrl, path, chunkIndex);
                    var doc = new Dictionary<string, object>
                    {
                        ["id"] = id,
                        ["repoUrl"] = repoUrl,
                        ["filePath"] = Path.GetRelativePath(repoPath, path),
                        ["fileName"] = Path.GetFileName(path),
                        ["content"] = chunk,
                        ["contentVector"] = vec
                    };

                    uploadQueue.Enqueue(new IndexActionWrapper(IndexActionType.Upload, doc));
                    chunkIndex++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {path}: {ex.Message}");
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(producers);

        // enqueue deletes for removed files
        foreach (var removedPath in deleted)
        {
            // we used IdForPath with chunk index; easiest option: delete by prefix (not supported) OR track IDs in index.
            // Simpler: delete any documents that match repoUrl + filePath
            uploadQueue.Enqueue(new IndexActionWrapper(IndexActionType.DeleteByFilter, new Dictionary<string, object>
            {
                ["repoUrl"] = repoUrl,
                ["filePath"] = removedPath
            }));
        }

        // done producing
        // signal consumer to finish after small delay to allow last items to queue
        await Task.Delay(500);
        cts.Cancel();
        await consumer;

        // update last commit sha
        await tracker.SetLastCommitShaAsync(repoUrl, headSha);

        Console.WriteLine("Indexing finished.");
        return 0;
    }
}
