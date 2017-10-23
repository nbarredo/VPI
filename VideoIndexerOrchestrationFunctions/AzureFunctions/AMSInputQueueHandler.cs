using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage.Blob;
using OrchestrationFunctions.Helpers;
using VPI.Entities;

// ReSharper disable ReplaceWithSingleCallToFirstOrDefault

namespace OrchestrationFunctions
{
    public static class AMSInputQueueHandler
    {
        // NOTE: You have to update the WebHookEndpoint and Signing Key that you wish to use in the AppSettings to match
        //       your deployed "AMSNotificationHttpHandler". After deployment, you will have a unique endpoint. 
        private static readonly string WebHookEndpoint =
            Environment.GetEnvironmentVariable("MediaServicesNotificationWebhookUrl");

        // this key is used by AMS to sign the webhook payload. Verifying it ensures the webhook wascalled by AMS
        private static string _signingKey = Environment.GetEnvironmentVariable("MediaServicesWebhookSigningKey");


        [FunctionName("AMSInputQueueHandler")]
        public static async Task Run([QueueTrigger("%InputQueue%", Connection = "AzureWebJobsStorage")] VippyProcessingState manifest,
            [Blob("%AmsBlobInputContainer%/{BlobName}", FileAccess.ReadWrite)] CloudBlockBlob videoBlobTriggered,
            [Blob("%ExistingAmsBlobInputContainer%/{BlobName}", FileAccess.ReadWrite)] CloudBlockBlob videoBlobExisting,

            TraceWriter log)
        {

            //================================================================================
            // Function AMSInputQueueHandler
            // Purpose:
            // This is where the start of the pipeline work begins. It will submit an encoding
            // job to Azure Media Services.  When that job completes asyncronously, a notification
            // webhook will be called by AMS which causes the next stage of the pipeline to 
            // continue.
            //================================================================================
            CloudBlockBlob videoBlob = null;
            if (manifest.Origin == Enums.OriginEnum.Existing)
                videoBlob = videoBlobExisting;
            else if (manifest.Origin == Enums.OriginEnum.Trigger)
                videoBlob = videoBlobTriggered;
            if (videoBlob == null)
            {
                log.Error("There is being an error, videoblog not initialized, not marked as existing or trigger or video is null");
                return;
            }
            var context = MediaServicesHelper.Context;
            var cosmosHelper = new CosmosHelper(log);
            var toDeleteContainerName = Environment.GetEnvironmentVariable("AmsBlobToDeleteContainer");
            // only set the starttime if it wasn't already set in blob watcher function (that way
            // it works if the job is iniaited by using this queue directly
            if (manifest.StartTime == null)
                manifest.StartTime = DateTime.Now;

            var videofileName = videoBlob.Name;

            // get a new asset from the blob, and use the file name if video title attribute wasn't passed.
            IAsset newAsset;
            try
            {
                newAsset = BlobHelper.CreateAssetFromBlob(videoBlob, videofileName, log)
                    .GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                throw new ApplicationException($"Error occured creating asset from Blob;/r/n{e.Message}");
            }

            // If an internal_id was passed in the metadata, use it within AMS (AlternateId) and Cosmos(Id - main document id) for correlation.
            // if not, generate a unique id.  If the same id is ever reprocessed, all stored metadata
            // will be overwritten.

            newAsset.AlternateId = manifest.AlternateId;
            newAsset.Update();

            manifest.AmsAssetId = newAsset.Id;

            log.Info($"Deleting  the file  {videoBlob.Name} from the container ");

            //move the video to the to delete folder
            await BlobHelper.Move(videoBlob.Container.Name, toDeleteContainerName, videofileName, log);
            //move the manifest to the to delete folder
            string manifestName = videofileName.Remove(videofileName.IndexOf('.')) + ".json";
            log.Info($"Deleting  the file  {manifestName} from the container ");
            await BlobHelper.Move(videoBlob.Container.Name, toDeleteContainerName, manifestName, log);
            // copy blob into new asset
            // create the encoding job
            var job = context.Jobs.Create("MES encode from input container - ABR streaming");

            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            var processor = MediaServicesHelper.GetLatestMediaProcessorByName("Media Encoder Standard");

            var task = job.Tasks.AddNew("encoding task",
                processor,
                "Content Adaptive Multiple Bitrate MP4",
                TaskOptions.None
            );

            task.Priority = 100;
            task.InputAssets.Add(newAsset);

            // setup webhook notification 
            var keyBytes = new byte[32];

            // Check for existing Notification Endpoint with the name "FunctionWebHook"
            var existingEndpoint = context.NotificationEndPoints.Where(e => e.Name == "FunctionWebHook").FirstOrDefault();
            INotificationEndPoint endpoint;

            if (existingEndpoint != null)
            {
                endpoint = existingEndpoint;
            }
            else
                try
                {
                    endpoint = context.NotificationEndPoints.Create("FunctionWebHook",
                        NotificationEndPointType.WebHook, WebHookEndpoint, keyBytes);
                }
                catch (Exception)
                {
                    throw new ApplicationException(
                        $"The endpoing address specified - '{WebHookEndpoint}' is not valid.");
                }

            task.TaskNotificationSubscriptions.AddNew(NotificationJobState.FinalStatesOnly, endpoint, false);
            cosmosHelper.LogMessage($"Add an output asset to contain the results of the job");
            // Add an output asset to contain the results of the job. 
            // This output is specified as AssetCreationOptions.None, which 
            // means the output asset is not encrypted. 
            task.OutputAssets.AddNew(videofileName, AssetCreationOptions.None);

            // Starts the job in AMS.  AMS will notify the webhook when it completes
            job.Submit();
            cosmosHelper.LogMessage($"Saving on cosmos DB");
            // update processing progress with id and metadata payload
            await cosmosHelper.StoreProcessingStateRecordInCosmosAsync(manifest);

            cosmosHelper.LogMessage($"AMS encoding job submitted for {videofileName}");
        }
    }
}
