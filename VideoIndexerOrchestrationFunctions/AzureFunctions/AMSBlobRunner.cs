using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using OrchestrationFunctions.Helpers;

namespace OrchestrationFunctions.AzureFunctions
{
    public static class AMSBlobRunner
    {
        /// <summary>
        /// gets all the blobs on the existing container and send them to the queue for transcoding 
        /// and processing 
        /// </summary>
        /// <param name="myTimer"></param>
        /// <param name="outputQueue"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("AMSBlobRunner")]
        public static async Task Run([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, 
            [Queue("ams-input")] IAsyncCollector<string> outputQueue,   // output queue for async processing and resiliency
            TraceWriter log)
        {
            log.Info($"AMSBlobRunner Timer trigger function executed at: {DateTime.Now}");
            var blobHelper=new BlobHelper(log);
            var blobInfoList = blobHelper.GetBlobInfo();

            foreach (var blobInfo in blobInfoList)
            {
                await FunctionHelper.ProcessBlogIntoQueue(blobInfo.Blob, blobInfo.JsonManifest, outputQueue, log);

            }
        }
    }
}
