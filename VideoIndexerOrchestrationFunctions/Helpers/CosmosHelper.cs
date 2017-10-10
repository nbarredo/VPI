using System;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Host;

namespace OrchestrationFunctions.Helpers
{
    public class CosmosHelper : BaseHelper
    {
        public string ProcessingStateCosmosCollectionName => "VIProcessingState";
        public string CosmosDatabasename
        {
            get
            {
                var cosmosDatabaseName = ConfigurationManager.AppSettings["Cosmos_Database_Name"];
                if (string.IsNullOrEmpty(cosmosDatabaseName))
                    throw new ApplicationException("Cosmos_Database_Name app setting not set");
                return cosmosDatabaseName;
            }
        }

        /// <summary>
        ///     Returns a new DocumentClient instantiated with endpoint and key
        /// </summary>
        /// <returns></returns>
        private DocumentClient GetCosmosClient()
        {
            var endpoint = ConfigurationManager.AppSettings["Cosmos_Endpoint"];
            if (string.IsNullOrEmpty(endpoint))
                throw new ApplicationException("Cosmos_Endpoint app setting not set");

            var key = ConfigurationManager.AppSettings["Cosmos_Key"];
            if (string.IsNullOrEmpty(key))
                throw new ApplicationException("Cosmos_Key app setting not set");

            var client = new DocumentClient(new Uri(endpoint), key);

            return client;
        }

        /// <summary>
        ///     Returns a new DocumentClient instantiated with endpoint and key, AND
        ///     creates the database and collection they don't already exist.
        /// </summary>
        /// <param name="database"></param>
        /// <param name="collection"></param>
        /// <returns></returns>
        public DocumentClient GetCosmosClient(string collection)
        {
            var client = GetCosmosClient();

            // ensure database and collection exist
            CreateCosmosDbAndCollectionIfNotExists(client, CosmosDatabasename, collection);

            return client;
        }

        /// <summary>
        ///     This makes sure the database and collection exist.  It will create them
        ///     in the event they don't. Makes deployment cleaner.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="database"></param>
        /// <param name="collection"></param>
        private async void CreateCosmosDbAndCollectionIfNotExists(DocumentClient client, string database,
            string collection)
        {
            // make sure the database and collection already exist           
            await client.CreateDatabaseIfNotExistsAsync(new Database { Id = database });
            await client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri(database),
                new DocumentCollection { Id = collection });
        }


        /// <summary>
        ///     Inserts a receipt like record in the database. This record will be updated when the processing
        ///     is completed with success or error details
        /// </summary>
        /// <param name="state">metadata provided with input video (manifest type data)</param>
        /// <returns></returns>
        public async Task StoreProcessingStateRecordInCosmosAsync(VippyProcessingState state)
        {

            var collectionName = ProcessingStateCosmosCollectionName;
            var client = GetCosmosClient(collectionName);


            // upsert the json as a new document
            try
            {
                Document r =
                       await client.UpsertDocumentAsync(
                           UriFactory.CreateDocumentCollectionUri(CosmosDatabasename, collectionName), state);
            }
            catch (Exception e)
            {

                throw new ApplicationException($"Error in StoreProcessingStateRecordInCosmosAsync:/r/n{e.Message}");
            }
        }

        public VippyProcessingState GetManifestStateRecord(string Id, TraceWriter log)
        {
            LogMessage($" get manifest id: {Id} ");
            var client = GetCosmosClient(ProcessingStateCosmosCollectionName);
            var uri = UriFactory.CreateDocumentCollectionUri(CosmosDatabasename, ProcessingStateCosmosCollectionName);

            var result = client
                .CreateDocumentQuery<VippyProcessingState>(uri)
                .Where(q => q.DocumentType == "state" && q.AlternateId == Id).AsEnumerable();

            var state = result.FirstOrDefault();
            LogMessage($"got manifest with id: {state?.AlternateId} ");
            return state;
        }



        public VippyProcessingState GetManifestStateRecordByVideoId(string videoIndexerId, TraceWriter log)
        {
            LogMessage($" get manifest VideoId: {videoIndexerId} ");
            var collectionName = ProcessingStateCosmosCollectionName;
            var client = GetCosmosClient(collectionName);
            var uri = UriFactory.CreateDocumentCollectionUri(CosmosDatabasename, collectionName);

            var result = client
                .CreateDocumentQuery<VippyProcessingState>(uri)
                .Where(q => q.DocumentType == "state" && q.VideoIndexerId == videoIndexerId).AsEnumerable();

            var state = result.FirstOrDefault();
            LogMessage($"got manifest with VideoId: {state?.VideoIndexerId} ");
            return state;
        }

        public async Task UpdateProcessingStateAsync(string viUniqueId)
        {
            var client = GetCosmosClient(ProcessingStateCosmosCollectionName);
            var collectionLink = UriFactory.CreateDocumentCollectionUri(CosmosDatabasename, ProcessingStateCosmosCollectionName);

            // since Video Indexer Id is not the primary Id of the document, query by Document Type and
            // Video Index Id
            try
            {
                var state = client.CreateDocumentQuery<VippyProcessingState>(collectionLink)
                    .Where(so => so.VideoIndexerId == viUniqueId && so.DocumentType == "state")
                    .AsEnumerable()
                    .FirstOrDefault();
                if (state == null)
                {
                    var message = $"state with video id {viUniqueId} not found!!";
                    LogMessage(message);
                    throw new Exception(message);
                }

                state.EndTime = DateTime.Now;
                await client.UpsertDocumentAsync(collectionLink, state);
            }
            catch (Exception e)
            {

                throw new ApplicationException($"Error trying to update processing state in Cosmos:\r\n{e.Message}");
            }


        }

        public CosmosHelper(TraceWriter log) : base(log)
        {
          
        }
    }
}
