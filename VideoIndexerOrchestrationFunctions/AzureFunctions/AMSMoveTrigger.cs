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

                var message = JsonConvert.DeserializeObject<MoveMessage>(myQueueItem);
                if (message == null || string.IsNullOrEmpty(message.SourceContainer) || string.IsNullOrEmpty(message.SourceContainer))
                {
                    log.Error("Message missing parameters or null");
                    return;
                }
                var blobInfoList = BlobHelper.GetBlobInfo(message.SourceContainer);
                if (blobInfoList == null || !blobInfoList.Any())
                {
                    return;
                }
                foreach (var blobInfo in blobInfoList)
                {
                    log.Info($"moving file {blobInfo.Blob.Name } from {message.SourceContainer} to {message.TargetContainer}");
                    var source = blobInfo.Blob.Container.Name;
                    await BlobHelper.Move(source, message.TargetContainer, blobInfo.Blob.Name);
                }
            }
            catch (Exception)
            {
                log.Error($"there's being an error, the message is :{myQueueItem}");
                throw;
            }
        }
    }
}
