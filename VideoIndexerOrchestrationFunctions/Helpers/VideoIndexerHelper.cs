using System;
using System.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace OrchestrationFunctions.Helpers
{
    public class VideoIndexerHelper: BaseHelper
    {
        private readonly CosmosHelper _cosmosHelper;
        private   CloudBlobContainer _videoIndexerResourcesContainer;

        public readonly string VideoIndexerApiUrl =
            "https://videobreakdown.azure-api.net/Breakdowns/Api/Partner/Breakdowns";


        public CloudBlobContainer VideoIndexerResourcesContainer
        {
            get
            {
                if (_videoIndexerResourcesContainer != null) return _videoIndexerResourcesContainer;

                // initialize VI resources container
                var amsStorageClient = CopyBlobHelper.AmsStorageAccount.CreateCloudBlobClient();
                var imageContainer = amsStorageClient.GetContainerReference("video-indexer-resources");

                if (imageContainer.CreateIfNotExists())
                {
                    // configure container for public access
                    var permissions = imageContainer.GetPermissions();
                    permissions.PublicAccess = BlobContainerPublicAccessType.Container;
                    imageContainer.SetPermissions(permissions);
                }
                _videoIndexerResourcesContainer = imageContainer;

                return _videoIndexerResourcesContainer;
            }

        }

      

    
        public HttpClient GetVideoIndexerHttpClient()
        {
            var client = new HttpClient();

            // Video Indexer API key stored in settings (App Settings in Azure Function portal)
            var videoIndexerKey = ConfigurationManager.AppSettings["VideoIndexer_Key"];
            if (String.IsNullOrEmpty(videoIndexerKey))
                throw new ApplicationException("VideoIndexerKey app setting not set");


            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", videoIndexerKey);
            return client;
        }

        /// <summary>
        /// </summary>
        /// <param name="blobName"></param>
        /// <param name="saSUrl">Secure link to video file in Azure Storage</param>
        /// <param name="alternateId"></param>
        /// <returns>VideoBreakdown in JSON format</returns>
        public async Task<string> SubmitToVideoIndexerAsync(string blobName, string saSUrl,
            string alternateId, TraceWriter log)
        {
            // need to get the processing state to set some of the properties on the VI job
            var state = _cosmosHelper. GetManifestStateRecord(alternateId, log);
            var props = state.CustomProperties ?? new Properties();
            var jsonProps = JsonConvert.SerializeObject(props);
            LogMessage( $"props == {jsonProps}");
            LogMessage( $"videoIndexerCallbackUrl");
            var videoIndexerCallbackUrl = ConfigurationManager.AppSettings["Video_Indexer_Callback_url"];
            if (String.IsNullOrEmpty(videoIndexerCallbackUrl))
                throw new ApplicationException("Video_Indexer_Callback_url app setting not set");

            var queryString = HttpUtility.ParseQueryString(String.Empty);

            // These can be used to set meta data visible in the VI portal.  
            // required settings
            queryString["videoUrl"] = saSUrl;
            var language = !string.IsNullOrEmpty(props.Language) ? props.Language : "English";
            queryString["language"] = language;
            queryString["privacy"] = "private";
            LogMessage( $"language set to {language}");
            // optional settings - mostly VI portal UI related
            var videoTitle = !String.IsNullOrEmpty(props.Title) ? props.Title : state.BlobName;
            var videoDescription = !String.IsNullOrEmpty(props.Title) ? props.Title : "video desc not set in json";
            queryString["name"] = videoTitle;
            queryString["description"] = videoDescription;
            queryString["callbackUrl"] = videoIndexerCallbackUrl;
            queryString["externalId"] = alternateId;

            var apiUrl = VideoIndexerApiUrl;
            var client = GetVideoIndexerHttpClient();

            // post to the API
            var result = await client.PostAsync(apiUrl + $"?{queryString}", null);

            // the JSON result in this case is the VideoIndexer assigned ID for this video.
            var json = result.Content.ReadAsStringAsync().Result;
            var videoIndexerId = JsonConvert.DeserializeObject<string>(json);
            state.VideoIndexerId = videoIndexerId;
            // save a record of this job submission
            await _cosmosHelper.StoreProcessingStateRecordInCosmosAsync(state);
             
            LogMessage( $"Submitted videoId '{alternateId}', title '{videoTitle}'");

            return videoIndexerId;
        }

       


        public VideoIndexerHelper(TraceWriter log) : base(log)
        {
            _cosmosHelper=new CosmosHelper(log);
        }
    }
}
