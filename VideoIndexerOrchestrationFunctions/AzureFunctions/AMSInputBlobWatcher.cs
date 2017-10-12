using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using OrchestrationFunctions.Helpers;
using VPI.Entities;

namespace OrchestrationFunctions
{
    public static class AMSInputBlobWatcher 
    {
       
        [FunctionName("AMSInputBlobWatcher")]
        public static async Task RunAsync([BlobTrigger("%AmsBlobInputContainer%/{name}.{extension}", Connection = 
            "AzureWebJobsStorage")] CloudBlockBlob inputVideoBlob,      // video blob that initiated this function
            [Blob("%AmsBlobInputContainer%/{name}.json", FileAccess.Read)] string manifestContents,  // if a json file with the same name exists, it's content will be in this variable.
            [Queue("ams-input")] IAsyncCollector<string> outputQueue,   // output queue for async processing and resiliency
            TraceWriter log)
        {
            //================================================================================
            // Function AMSInputBlobWatcher
            // Purpose:
            // This function monitors a blob container for new mp4 video files.  If the video files are 
            // accompanied by a json file with the same file name, it will use this json file
            // for metadata such as video title, external ids, etc.  Any custom fields added
            // to this meta data file will be stored with the resulting document in Cosmos. 
            // ** Rather than doing any real processing here, just forward the payload to a
            // queue to be more resilient. A client app can either post files to the storage
            // container or add items to the queue directly.  Aspera or Signiant users will 
            // most likely opt to use the watch folder. 
            // ** NOTE - the json file must be dropped into the container first. 
            //================================================================================
            
           
            await FunctionHelper.ProcessBlogIntoQueue(inputVideoBlob, manifestContents, outputQueue, log, Enums.OriginEnum.Trigger);
        }

        
    }
}