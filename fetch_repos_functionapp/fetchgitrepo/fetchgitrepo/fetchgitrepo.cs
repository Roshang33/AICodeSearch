using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace fetchgitrepo
{
    public static class fetchgitrepo
    {
        [FunctionName("fetchrepolist")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Starting GitHub repo fetch.");

            string githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            string githubOrg = Environment.GetEnvironmentVariable("GITHUB_ORG");
            string azureTableConn = Environment.GetEnvironmentVariable("AZURE_TABLE_CONN");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string tableName = data?.tablename;

            if (string.IsNullOrEmpty(githubToken) || string.IsNullOrEmpty(githubOrg) || string.IsNullOrEmpty(azureTableConn) || string.IsNullOrEmpty(tableName))
            {
                return new BadRequestObjectResult("Missing required parameters or environment variables.");
            }

            try
            {
                var repos = await GetGitHubRepos(githubToken, githubOrg);
                await WriteToTable(azureTableConn, tableName, repos);
                return new OkObjectResult("Success");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error occurred.");
                return new ObjectResult($"Internal Server Error: {ex.Message}") { StatusCode = 500 };
            }
        }

        private static async Task<List<dynamic>> GetGitHubRepos(string token, string orgOrUser)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AzureFunctionApp");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var repos = new List<dynamic>();
            int page = 1;

            while (true)
            {
                string url = $"https://api.github.com/orgs/{orgOrUser}/repos?per_page=100&page={page}";
                var response = await client.GetAsync(url);
                string content = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    url = $"https://api.github.com/users/{orgOrUser}/repos?per_page=100&page={page}";
                    response = await client.GetAsync(url);
                    content = await response.Content.ReadAsStringAsync();
                }

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"GitHub API error: {response.StatusCode} - {content}");

                var pageRepos = JsonConvert.DeserializeObject<List<dynamic>>(content);
                if (pageRepos == null || pageRepos.Count == 0) break;

                repos.AddRange(pageRepos);
                page++;
            }

            return repos;
        }

        private static async Task WriteToTable(string connString, string tableName, List<dynamic> repos)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(tableName);
            await table.CreateIfNotExistsAsync();

            foreach (var repo in repos)
            {
                var entity = new DynamicTableEntity("GitHub", repo.id.ToString())
                {
                    Properties =
                    {
                        { "name", new EntityProperty((string)repo.name) },
                        { "full_name", new EntityProperty((string)repo.full_name) },
                        { "html_url", new EntityProperty((string)repo.html_url) }
                    }
                };

                var insertOp = TableOperation.InsertOrReplace(entity);
                await table.ExecuteAsync(insertOp);
            }
        }
    }
}
