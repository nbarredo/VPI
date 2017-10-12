using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using VPI.Entities;

namespace OrchestrationFunctions.Helpers
{
    public static class FunctionHelper
    {
        public static async Task ProcessBlogIntoQueue(CloudBlockBlob inputVideoBlob, string manifestContents,
            IAsyncCollector<string> outputQueue, TraceWriter log, Enums.OriginEnum origin)
        {
            //HACK: This isn't ideal. I'd rather the trigger for this function NOT kick off
            // for json files.  That way all the app insights metrics aren't polluted with 
            // eroneous runs.
            //TODO: only trigger for video files
            if (inputVideoBlob.Name.ToLower().EndsWith(".json"))
                return;

            var baseHelper = new BaseHelper(log);
            VippyProcessingState manifest;
            try
            {
                if (!string.IsNullOrEmpty(manifestContents))
                {
                    manifest = JsonConvert.DeserializeObject<VippyProcessingState>(manifestContents);
                    baseHelper.LogMessage($"Manifest present, deserializing");
                }
                else
                {
                    manifest = new VippyProcessingState();
                    baseHelper.LogMessage($"Manifest empty");
                }
            }
            catch (Exception e)
            {
                baseHelper.LogMessage($"Error with manifest deserialization:{e.Message}");

                throw new ApplicationException($"Invalid manifest file provided for video {inputVideoBlob.Name}");
            }

            // work out the global id for this video. If internal_id was in manifest json, use that. 
            // Otherwise create a new one
            var internalId = manifest.AlternateId;
            var globalId = !string.IsNullOrEmpty(internalId) ? internalId : Guid.NewGuid().ToString();
            manifest.Origin = origin;
            // stuff it back into the manifest
            manifest.AlternateId = globalId;

            manifest.BlobName = inputVideoBlob.Name;
            manifest.StartTime = DateTime.Now;


            baseHelper.LogMessage($"Video '{inputVideoBlob.Name}' landed in watch folder" +
                                  (manifestContents != null ? " with manifest json" : "without manifest file"));

            await outputQueue.AddAsync(manifest.ToString());
        }
    }
}
