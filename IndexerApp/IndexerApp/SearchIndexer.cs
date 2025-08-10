using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class SearchIndexer
{
    private readonly SearchClient _client;
    private readonly SearchIndexClient _indexClient;
    private readonly string _indexName;

    public SearchIndexer(string endpoint, string apiKey, string indexName)
    {
        var cred = new AzureKeyCredential(apiKey);
        _indexClient = new SearchIndexClient(new Uri(endpoint), cred);
        _client = new SearchClient(new Uri(endpoint), indexName, cred);
        _indexName = indexName;
    }

    public async Task EnsureIndexExistsAsync(int vectorDimensions = 1536)
    {
        var exists = (await _indexClient.GetIndexNamesAsync().ToListAsync()).Contains(_indexName);
        if (exists) return;

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchField("repoUrl", SearchFieldDataType.String) { IsFilterable = true, IsSearchable = false },
            new SearchField("filePath", SearchFieldDataType.String) { IsFilterable = true, IsSearchable = true },
            new SearchField("fileName", SearchFieldDataType.String) { IsSearchable = true },
            new SearchField("content", SearchFieldDataType.String) { IsSearchable = true },
            new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = vectorDimensions,
                VectorSearchConfiguration = "default-vector-config"
            }
        };

        var index = new SearchIndex(_indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Algorithms =
                {
                    new HnswVectorSearchAlgorithmConfiguration("default-vector-config")
                    {
                        Parameters = new HnswParameters { M = 4, EfConstruction = 400, Metric = VectorSearchAlgorithmMetric.Cosine }
                    }
                }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index);
    }

    public async Task UploadBatchAsync(IEnumerable<IndexActionWrapper> actions)
    {
        // convert wrapper actions into IndexDocumentsBatch
        var uploads = new List<IndexDocumentsAction<Dictionary<string, object>>>();
        var deletesByFilter = new List<Dictionary<string, object>>();

        foreach (var a in actions)
        {
            if (a.Type == IndexActionType.Upload)
            {
                uploads.Add(IndexDocumentsAction.Upload(a.Document));
            }
            else if (a.Type == IndexActionType.Delete)
            {
                uploads.Add(IndexDocumentsAction.Delete(a.Document));
            }
            else if (a.Type == IndexActionType.DeleteByFilter)
            {
                deletesByFilter.Add(a.Document);
            }
        }

        if (uploads.Count > 0)
        {
            var batch = IndexDocumentsBatch.Create(uploads);
            await _client.IndexDocumentsAsync(batch);
        }

        // handle deletes by filter by running search for matching docs then deleting by id
        foreach (var filter in deletesByFilter)
        {
            var repoUrl = filter["repoUrl"].ToString();
            var filePath = filter["filePath"].ToString();
            var options = new SearchOptions { Filter = $"repoUrl eq '{repoUrl}' and filePath eq '{filePath}'", Size = 1000 };
            var results = await _client.SearchAsync<Dictionary<string, object>>("", options);
            var ids = new List<Dictionary<string, object>>();
            await foreach (var r in results.Value.GetResultsAsync())
            {
                if (r.Document.TryGetValue("id", out var idObj))
                {
                    ids.Add(new Dictionary<string, object> { ["id"] = idObj.ToString() });
                }
            }
            if (ids.Count > 0)
            {
                var deleteBatch = IndexDocumentsBatch.Delete(ids.Select(d => (object)d));
                // note: need typed batch in correct form; doing delete by uploading document with only key is simpler
                var actionsList = ids.Select(d => IndexDocumentsAction.Delete(d)).ToList();
                var b = IndexDocumentsBatch.Create(actionsList);
                await _client.IndexDocumentsAsync(b);
            }
        }
    }
}
