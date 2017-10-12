#region Usings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using OrchestrationFunctions.Helpers;
using VPI.Entities;
// ReSharper disable ReplaceWithSingleCallToFirstOrDefault

#endregion

namespace OrchestrationFunctions
{
    public static class AmsNotificationQueueHandler
    {
        private static CloudMediaContext _context;

        /// <summary>
        ///     This function will submit a Video Indexing job in response to an Azure Media Services
        ///     queue notification upon encoding completion.  To use this function, the AMS encoding task
        ///     must be configured with a queue notification using the queue named below in the arguments
        ///     to this method.  After the VI job completes, VI will use a web callback to invoke the rest
        ///     of the processing in order to store the VI results in Cosmos Db.
        /// </summary>
        /// <param name="myQueueItem"></param>
        /// <param name="log"></param>
        [FunctionName("AMSNotificationQueueHandler")]
        public static async Task RunAsync(
            [QueueTrigger("encoding-complete", Connection = "AzureWebJobsStorage")] string myQueueItem, TraceWriter log)
        {
            var videoIndexerHelper = new VideoIndexerHelper(log);
            var cosmosHelper = new CosmosHelper(log);
            var msg = JsonConvert.DeserializeObject<NotificationMessage>(myQueueItem);
            if (msg.EventType != NotificationEventType.TaskStateChange)
                return; // ignore anything but job complete 


            var newJobStateStr = msg.Properties.FirstOrDefault(j => j.Key == Constants.NEWSTATE).Value;
            if (newJobStateStr == Enums.StateEnum.Finished.ToString())
            {

                var jobId = msg.Properties["JobId"];
                var taskId = msg.Properties["TaskId"];

                _context = MediaServicesHelper.Context;

                var job = _context.Jobs.Where(j => j.Id == jobId).FirstOrDefault();
                if (job == null)
                {
                    videoIndexerHelper.LogMessage($"Job for JobId:{jobId} is null");
                    return;
                }
                var task = job.Tasks.Where(l => l.Id == taskId).FirstOrDefault();
                if (task == null)
                {
                    videoIndexerHelper.LogMessage($"Task for taskId:{taskId} is null");
                    return;
                }
                var outputAsset = task.OutputAssets[0];
                var inputAsset = task.InputAssets[0];


                cosmosHelper.LogMessage("Read policy");
                var readPolicy =
                    _context.AccessPolicies.Create("readPolicy", TimeSpan.FromHours(4), AccessPermissions.Read);
                var outputLocator = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset, readPolicy);
                cosmosHelper.LogMessage("Create cloud blob client");
                var destBlobStorage = BlobHelper.AmsStorageAccount.CreateCloudBlobClient();
                cosmosHelper.LogMessage("get asset container");
                // Get the asset container reference
                var outContainerName = new Uri(outputLocator.Path).Segments[1];
                var outContainer = destBlobStorage.GetContainerReference(outContainerName);
                cosmosHelper.LogMessage("use largest single mp4 ");
                // use largest single mp4 output (highest bitrate) to send to Video Indexer
                var biggestblob = outContainer.ListBlobs().OfType<CloudBlockBlob>()
                    .Where(b => b.Name.ToLower().EndsWith(".mp4"))
                    .OrderBy(u => u.Properties.Length).Last();
                cosmosHelper.LogMessage("GetSasUrl");
                var sas = videoIndexerHelper.GetSasUrl(biggestblob);
                cosmosHelper.LogMessage(" submit to VI ");
                // Submit processing job to Video Indexer
                await videoIndexerHelper.SubmitToVideoIndexerAsync(biggestblob.Name, sas, inputAsset.AlternateId, log);
            }
        }

        private static string PublishAsset(IAsset outputAsset)
        {
            // You cannot create a streaming locator using an AccessPolicy that includes write or delete permissions.
            var policy = _context.AccessPolicies.Create("Streaming policy",
                TimeSpan.FromDays(30),
                AccessPermissions.Read);

            // Create a locator to the streaming content on an origin. 
            var originLocator = _context.Locators.CreateLocator(LocatorType.OnDemandOrigin, outputAsset,
                policy,
                DateTime.UtcNow.AddMinutes(-5));

            // Get a reference to the streaming manifest file from the  
            // collection of files in the asset. 
            var manifestFile = outputAsset.AssetFiles.Where(f => f.Name.ToLower().EndsWith(".ism")).FirstOrDefault();
            if (manifestFile == null)
            {
                throw new Exception($"Manifest file not found for asset alternate Id: {outputAsset.AlternateId}");
            }
            // Create a full URL to the manifest file. Use this for playback
            // in streaming media clients. 
            var urlForClientStreaming = originLocator.Path + manifestFile.Name + "/manifest";


            return urlForClientStreaming;
        }
    }

    internal sealed class NotificationMessage
    {
        public string MessageVersion { get; set; }
        public string ETag { get; set; }
        public NotificationEventType EventType { get; set; }
        public DateTime TimeStamp { get; set; }
        public IDictionary<string, string> Properties { get; set; }
    }
}