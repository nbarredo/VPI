#region Usings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using OrchestrationFunctions.Helpers;
using VPI.Entities;

#endregion

namespace OrchestrationFunctions
{
    public static class VideoIndexerCompleteQueueHandler
    {
        [FunctionName("VideoIndexerCompleteQueueHandler")]
        public static async Task RunAsync(
            [QueueTrigger("%VIProcessingCompleteQueue%", Connection = "AzureWebJobsStorage")] CloudQueueMessage myQueueItem,
            TraceWriter log)
        {
            var videoIndexerHelper=new VideoIndexerHelper(log);
            var cosmosHelper=new CosmosHelper(log); 
            
            var queueContents = myQueueItem.AsString;

            // queue item should be id & state
            var completionData = JsonConvert.DeserializeObject<Dictionary<string, string>>(queueContents);

            // ignore if not proper state
            if (completionData["state"] != "Processed")
                return;

            var videoIndexerVideoId = completionData["id"];

            var apiUrl = videoIndexerHelper.VideoIndexerApiUrl;
            var client = videoIndexerHelper.GetVideoIndexerHttpClient();
             
            var uri = string.Format(apiUrl + "/{0}", videoIndexerVideoId);
            videoIndexerHelper.LogMessage( $" call VI with uri{uri}");
            var response = await client.GetAsync(uri);
            var json =await response.Content.ReadAsStringAsync();
            videoIndexerHelper.LogMessage( $"get state with id: {videoIndexerVideoId}");
            var state = cosmosHelper.GetManifestStateRecordByVideoId(videoIndexerVideoId, log);
            videoIndexerHelper.LogMessage( "create poco from json ");
            var videoBreakdownPoco = JsonConvert.DeserializeObject<VideoBreakdown>(json);

         

            var taskList = new List<Task>();
           

            // these tasks are network io dependant and can happen in parallel
            //TODO:  setup default languages to be pulled from app settings, but
            //TODO: the languages set in config would override
            //TODO: validate languages
            videoIndexerHelper.LogMessage( "Process transcripts");
            if (state.Transcripts != null)
            {
                 taskList = state.Transcripts
                    .Select(language => GetCaptionsVttAsync(videoIndexerVideoId, videoBreakdownPoco, language, videoIndexerHelper))
                    .ToList();               
            }
            videoIndexerHelper.LogMessage( "start task extract images");
            taskList.Add(ExtractImages(videoBreakdownPoco, log, videoIndexerHelper));
            //var englishCaptionsTask = GetCaptionsVttAsync(videoIndexerVideoId, videoBreakdownPoco, "English");
            //var japaneseCaptionsTask = GetCaptionsVttAsync(videoIndexerVideoId, videoBreakdownPoco, "Japanese");
            //var imagesTask = ExtractImages(videoBreakdownPoco);
            videoIndexerHelper.LogMessage( $"wait for tasks to finish");
            await Task.WhenAll(taskList.ToArray());

            videoIndexerHelper.LogMessage( $"store in json breakdowns");
            // we wait to store breakdown json because it's modified in previous tasks
            var storeTask1 = StoreBreakdownJsonInCosmos(videoBreakdownPoco, log);
            videoIndexerHelper.LogMessage( $"update processing ");
            var storeTask2 = cosmosHelper.UpdateProcessingStateAsync(completionData["id"]);
            videoIndexerHelper.LogMessage( $"wait for  store tasks to finish");
            await Task.WhenAll(storeTask1, storeTask2);

        }

        private static async Task GetCaptionsVttAsync(string id, VideoBreakdown videoBreakdownPoco, string language, VideoIndexerHelper videoIndexerHelper)
        {
            var client = videoIndexerHelper.GetVideoIndexerHttpClient();
            var queryString = HttpUtility.ParseQueryString(string.Empty);

            // Request parameters
            queryString["language"] = language;
            var uri = $"https://videobreakdown.azure-api.net/Breakdowns/Api/Partner/Breakdowns/{id}/VttUrl?" +
                      queryString;

            // this returns a url to the captions file
            var response = await client.GetAsync(uri);
            var vttUrl =
                response.Content.ReadAsStringAsync().Result
                    .Replace("\"", ""); // seems like the url is always wrapped in quotes

            // download actual vtt file and store in blob storage
            var vttStream = await DownloadWebResource(vttUrl);
            await UploadFileToBlobStorage(vttStream, $"{videoBreakdownPoco.id}/{language}.vtt", "text/plain", videoIndexerHelper);

            //TODO: put reference to vtt in breakdown?
        }

        /// <summary>
        ///     Store the images referenced in the breakdown and update their location in the JSON prior to
        ///     storing in the database.
        /// </summary>
        /// <param name="poco"></param>
        /// <param name="videoIndexerHelper"></param>
        /// <returns></returns>
        private static async Task ExtractImages(VideoBreakdown poco, TraceWriter log, VideoIndexerHelper videoIndexerHelper)
        {
            var baseHelper = new BaseHelper(log); 
            baseHelper.LogMessage( $"start task extract images");
            // download thumbnail and store in blob storage

            var memSreamOfResource = await DownloadWebResource(poco.summarizedInsights.thumbnailUrl);
            var newBlob = await UploadFileToBlobStorage(memSreamOfResource, $"{poco.id}/video-thumbnail.jpg",
                "image/jpg", videoIndexerHelper);

            string newImageUrl = newBlob.Uri.AbsoluteUri;

            // replace urls in breakdown
            poco.summarizedInsights.thumbnailUrl = newImageUrl;
            poco.breakdowns[0].thumbnailUrl = newImageUrl;
            baseHelper.LogMessage( $"wait for task extract images to finish");
            await Task.WhenAll(poco.summarizedInsights.faces.Select(f => StoreFacesAsync(f, poco, videoIndexerHelper)));
        }

        private static async Task StoreFacesAsync(Face f, VideoBreakdown poco, VideoIndexerHelper videoIndexerHelper)
        {
            var faceStream = await DownloadWebResource(f.thumbnailFullUrl);
            var blob = await UploadFileToBlobStorage(faceStream, $"{poco.id}/faces/{f.shortId}.jpg", "image/jpg", videoIndexerHelper);

            f.thumbnailFullUrl = blob.Uri.ToString();
        }

        private static async Task<CloudBlockBlob> UploadFileToBlobStorage(MemoryStream blobContents, string blobName,
            string contentType, VideoIndexerHelper videoIndexerHelper)
        {
            var resourcesContainer = videoIndexerHelper.VideoIndexerResourcesContainer;
            var newBlob = resourcesContainer.GetBlockBlobReference(blobName);
            newBlob.Properties.ContentType = contentType;
            await newBlob.UploadFromStreamAsync(blobContents);

            return newBlob;
        }

        private static async Task<MemoryStream> DownloadWebResource(string Url)
        {
            using (var httpClient = new HttpClient())
            {
                return new MemoryStream(await httpClient.GetByteArrayAsync(Url));
            }
        }

     

        private static async Task StoreBreakdownJsonInCosmos(VideoBreakdown videoBreakdownJson, TraceWriter log)
        {
            //string CosmosCollectionName = ConfigurationManager.AppSettings["CosmosCollectionName"];
            //if (String.IsNullOrEmpty(CosmosCollectionName))
            //    throw new ApplicationException("CosmosCollectionName app setting not set");
            var cosmosHelper=new CosmosHelper(log);
            var collectionName = "Breakdowns";
            var client = cosmosHelper.GetCosmosClient(collectionName);
            var json = JsonConvert.SerializeObject(videoBreakdownJson);
            cosmosHelper.LogMessage( $"saving json: {json}");
            // save the json as a new document
            try
            {
                Document r =
                    await client.UpsertDocumentAsync(
                        UriFactory.CreateDocumentCollectionUri(CosmosHelper.CosmosDatabasename, collectionName),
                        videoBreakdownJson);
            }
            catch (Exception e)
            {
                cosmosHelper.LogMessage( $"error inserting document in cosmos: {e.Message}");
                // ignore for now, but maybe should replace the document if it already exists.. 
                // seems to be caused by dev environment where queue items are being reprocesssed
            }
        }
    }
}