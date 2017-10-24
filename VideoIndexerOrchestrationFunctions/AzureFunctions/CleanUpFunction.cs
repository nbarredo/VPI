using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using VPI.Entities;

namespace OrchestrationFunctions.AzureFunctions
{
    public static class CleanUpFunction
    {
        [FunctionName("CleanUpFunction")]
        public static void Run([QueueTrigger("cleanup-queue", Connection = "")]string myQueueItem, TraceWriter log)
        {
            try
            {
                log.Info($"C# Queue trigger function processed: {myQueueItem}");
                var message = JsonConvert.DeserializeObject<DeleteMessage>(myQueueItem);
                if (!message.DeleteAll) return;
                var blobList = BlobHelper.GetBlobList(message.TargetContainer);
                foreach (var blob in blobList)
                {
                    blob.DeleteIfExistsAsync();
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error on clean up function exception message: {ex.Message}");
                throw;
            }
        }
    }
}
