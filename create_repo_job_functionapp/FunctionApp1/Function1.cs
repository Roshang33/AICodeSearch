using System;
using System.Collections.Generic;
using System.Threading.Tasks;
//using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Batch;
using Microsoft.Azure.Batch.Auth;
using Microsoft.Azure.WebJobs;

public class QueueToBatchFunction
{
    private const string QueueName = "repoqueue";
    private const int MaxMessages = 10;

    private static readonly string _queueConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
    private static readonly string _batchAccountUrl = Environment.GetEnvironmentVariable("BatchAccountUrl");
    private static readonly string _batchAccountName = Environment.GetEnvironmentVariable("BatchAccountName");
    private static readonly string _batchAccountKey = Environment.GetEnvironmentVariable("BatchAccountKey");
    private static readonly string _batchPoolId = Environment.GetEnvironmentVariable("BatchPoolId");

    [FunctionName("BatchJobTimerTrigger")]
    public static async Task Run(
          [TimerTrigger("*/1 * * * *")] TimerInfo myTimer,
          ILogger log)
    {
        log.LogInformation($"Timer function executed at: {DateTime.Now}");

        var queueClient = new QueueClient(_queueConnectionString, QueueName);
        await queueClient.CreateIfNotExistsAsync();

        if (!await queueClient.ExistsAsync())
        {
            log.LogError("Queue does not exist.");
            return;
        }

        var messages = new List<string>();
        QueueMessage[] retrievedMessages = await queueClient.ReceiveMessagesAsync(maxMessages: 10);

        foreach (var message in retrievedMessages)
        {
            messages.Add(message.MessageText);
        }

        if (messages.Count == 0)
        {
            log.LogInformation("No messages to process.");
            return;
        }

        // Start a Batch job
        string jobId = "job-" + Guid.NewGuid().ToString();

        BatchSharedKeyCredentials credentials = new BatchSharedKeyCredentials(
            _batchAccountUrl,
            _batchAccountName,
            _batchAccountKey);

        using var batchClient =  BatchClient.Open(credentials);

        CloudJob job = batchClient.JobOperations.CreateJob();
        job.Id = jobId;
        job.PoolInformation = new PoolInformation { PoolId = _batchPoolId };
        await job.CommitAsync();

        // Add a task per message
        List<CloudTask> tasks = new();

        for (int i = 0; i < messages.Count; i++)
        {
            string taskId = $"task-{i + 1}";
            string taskCommandLine = $"cmd /c echo {messages[i]}"; // Replace with real command

            tasks.Add(new CloudTask(taskId, taskCommandLine));
        }

        await batchClient.JobOperations.AddTaskAsync(jobId, tasks);

        // Delete processed messages
        foreach (var msg in retrievedMessages)
        {
            await queueClient.DeleteMessageAsync(msg.MessageId, msg.PopReceipt);
        }

        log.LogInformation($"Batch job '{jobId}' with {messages.Count} task(s) submitted successfully.");
    }
}
