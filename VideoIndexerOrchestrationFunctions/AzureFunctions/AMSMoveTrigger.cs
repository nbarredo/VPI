using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using VPI.Entities;

namespace OrchestrationFunctions.AzureFunctions
{
    public static class AMSMoveTrigger
    {
        [FunctionName("AMSMoveTrigger")]
        public static async Task Run([QueueTrigger("ams-move-queue", Connection = "AzureWebJobsStorage")]string myQueueItem, TraceWriter log)
        {
            try       
            {
                log.Info($"AMSMoveTrigger triggered by message: {myQueueItem} on queue: ams-move-queue ");
                var message = JsonConvert.DeserializeObject<MoveMessage>(myQueueItem);
                if (string.IsNullOrEmpty(message?.SourceContainer) || string.IsNullOrEmpty(message.SourceContainer))
                {
                    log.Error("Message missing parameters or null");
                    return;
                }
                var blobInfoList = BlobHelper.GetBlobInfo(message.SourceContainer);
                if (blobInfoList == null || !blobInfoList.Any())
                {
                    log.Info($"no Files found on target ");
                    return;
                }
                foreach (var blobInfo in blobInfoList)
                {
                    log.Info($"moving file {blobInfo.Blob.Name } from {message.SourceContainer} to {message.TargetContainer}");
                    var source = blobInfo.Blob.Container.Name;
                    await BlobHelper.Move(source, message.TargetContainer, blobInfo.Blob.Name, log);
                }

                log.Info($"Move finished ");
            }
            catch (Exception)
            {
                log.Error($"there's being an error, the message is :{myQueueItem}");
                throw;
            }
        }
    }
}
