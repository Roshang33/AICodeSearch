using OpenAI;
using OpenAI.Embeddings;
using System;
using System.Linq;
using System.Threading.Tasks;

public class EmbeddingService
{
    private readonly OpenAIClient _client;

    public EmbeddingService(string openAiApiKey)
    {
        _client = new OpenAIClient(openAiApiKey);
    }

    // retry logic inside
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var maxRetries = 3;
        var delay = 500;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var resp = await _client.EmbeddingsEndpoint.CreateEmbeddingAsync(text, "text-embedding-3-small"); // or ada-002 if available
                return resp.Data[0].Embedding.ToArray();
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                Console.WriteLine($"Embedding failed, retrying: {ex.Message}");
                await Task.Delay(delay * (i + 1));
            }
        }
        throw new Exception("Failed to get embedding after retries");
    }
}
