// =============================================================================================================
// Adapted code from https://github.com/johndeu/media-services-dotnet-functions-integration/tree/master/shared
//  Special thanks to John Deutscher (https://github.com/johndeu) and Xavier Pouyat (https://github.com/xpouyat)
// 
// =============================================================================================================



using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrchestrationFunctions.Helpers;
using VPI.Entities;

namespace OrchestrationFunctions
{
    public static class  BlobHelper
    {
        static readonly string _storageAccountName = Environment.GetEnvironmentVariable("MediaServicesStorageAccountName");
        static readonly string _storageAccountKey = Environment.GetEnvironmentVariable("MediaServicesStorageAccountKey");

        internal static List<BlobInfo> GetBlobInfo(string sourceContainer)
        {
            var blobInfoList = new List<BlobInfo>();
            List<CloudBlockBlob> blobList = GetBlobList(sourceContainer);
            if (blobList.Count == 0)
            {
                return null;
            }
            foreach (var blob in blobList)
            {
                var blobInfo = new BlobInfo();
                var manifest = blobList
                    .FirstOrDefault(m => m.Name.GetFileNameWithoutExtension() == blob.Name.GetFileNameWithoutExtension()
                                         && m.Name.ToLower().EndsWith(".json"));
                var data = manifest?.DownloadText();
                blobInfo.Blob = blob;
                blobInfo.JsonManifest = data;
                blobInfoList.Add(blobInfo);
            }
            return blobInfoList;
        }

        internal static List<CloudBlockBlob> GetBlobList(string sourceContainer)
        {
            CloudBlobClient sourceCloudBlobClient = _amstorageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = sourceCloudBlobClient.GetContainerReference(sourceContainer);
            var blobList = container.ListBlobs()
                .Where(b => b.GetType() == typeof(CloudBlockBlob))
                .Cast<CloudBlockBlob>()
                .ToList();
            return blobList;
        }

        private static CloudStorageAccount _amstorageAccount;

        private static readonly CloudMediaContext _context = MediaServicesHelper.Context;

        public static CloudStorageAccount AmsStorageAccount { get => _amstorageAccount; set => _amstorageAccount = value; }

        public class AssetfileinJson
        {
            public string fileName = String.Empty;
            public bool isPrimary = false;
        }

        static  BlobHelper()
        {
            //Get a reference to the storage account that is associated with the Media Services account. 
            StorageCredentials mediaServicesStorageCredentials =
                new StorageCredentials(_storageAccountName, _storageAccountKey);
            _amstorageAccount = new CloudStorageAccount(mediaServicesStorageCredentials, false);

        }

        public static CloudBlobContainer GetCloudBlobContainer(string storageAccountName, string storageAccountKey, string containerName)
        {
            CloudStorageAccount sourceStorageAccount = new CloudStorageAccount(new StorageCredentials(storageAccountName, storageAccountKey), true);
            CloudBlobClient sourceCloudBlobClient = sourceStorageAccount.CreateCloudBlobClient();
            return sourceCloudBlobClient.GetContainerReference(containerName);
        }

        public static async Task Move(string sourceContainerName, string targetContainerName, string fileName, TraceWriter log)
        {
            try
            {
                CloudBlobClient blobClient = _amstorageAccount.CreateCloudBlobClient();
                CloudBlobContainer sourceContainer = blobClient.GetContainerReference(sourceContainerName);
                CloudBlobContainer targetContainer = blobClient.GetContainerReference(targetContainerName);
                log.Info($"moving file {fileName} from { sourceContainerName} to {targetContainerName}");
                CloudBlockBlob sourceBlob = sourceContainer.GetBlockBlobReference(fileName);
                CloudBlockBlob targetBlob = targetContainer.GetBlockBlobReference(fileName);
                var result= await targetBlob.StartCopyAsync(sourceBlob);
                await sourceBlob.DeleteIfExistsAsync();
            }
            catch (Exception)
            {
                log.Error($"error moving file {fileName} from { sourceContainerName} to {targetContainerName}");
                throw;
            }
        }

        public static void CopyBlobsAsync(CloudBlobContainer sourceBlobContainer, CloudBlobContainer destinationBlobContainer, TraceWriter log)
        {
            if (destinationBlobContainer.CreateIfNotExists())
            {
                destinationBlobContainer.SetPermissions(new BlobContainerPermissions
                {
                    PublicAccess = BlobContainerPublicAccessType.Blob
                });
            }

            var blobList = sourceBlobContainer.ListBlobs(null, true);
            foreach (var sourceBlob in blobList)
            {
                //log.Info("Source blob : " + (sourceBlob as CloudBlob).Uri.ToString());
                CloudBlob destinationBlob = destinationBlobContainer.GetBlockBlobReference((sourceBlob as CloudBlob)?.Name);
                if (destinationBlob.Exists())
                {
                    log.Info("Destination blob already exists. Skipping: " + destinationBlob.Uri);
                }
                else
                {
                    // log.Info("Copying blob " + sourceBlob.Uri.ToString() + " to " + destinationBlob.Uri.ToString());
                    CopyBlobAsync(sourceBlob as CloudBlob, destinationBlob);
                }
            }
        }

        private static async void CopyBlobAsync(CloudBlob sourceBlob, CloudBlob destinationBlob)
        {
            var signature = sourceBlob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24)
            });
            await destinationBlob.StartCopyAsync(new Uri(sourceBlob.Uri.AbsoluteUri + signature));
        }

        public static CopyStatus MonitorBlobContainer(CloudBlobContainer destinationBlobContainer)
        {
            var destBlobList = destinationBlobContainer.ListBlobs(null, true, BlobListingDetails.Copy);
            CopyStatus copyStatus = CopyStatus.Success;
            foreach (var dest in destBlobList)
            {
                if (!(dest is CloudBlob destBlob)) return CopyStatus.Failed;
                if (destBlob.CopyState.Status == CopyStatus.Aborted || destBlob.CopyState.Status == CopyStatus.Failed)
                {
                    // Log the copy status description for diagnostics and restart copy
                    destBlob.StartCopyAsync(destBlob.CopyState.Source);
                    copyStatus = CopyStatus.Pending;
                }
                else if (destBlob.CopyState.Status == CopyStatus.Pending)
                {
                    // We need to continue waiting for this pending copy
                    // However, let us log copy state for diagnostics
                    copyStatus = CopyStatus.Pending;
                }
                // else we completed this pending copy
            }
            return copyStatus;
        }

        public static async Task CopyBlobsToTargetContainer(CloudBlobContainer sourceContainer, CloudBlobContainer targetContainer, TraceWriter log)
        {
            try
            {
                foreach (var blob in sourceContainer.ListBlobs(string.Empty, true))
                {
                    //log.Info($"Blob URI: {blob.Uri}");
                    if (blob is CloudBlockBlob sourceBlob)
                    {
                        //log.Info($"Blob Name: {sourceBlob.Name}");
                        CloudBlockBlob targetBlob = targetContainer.GetBlockBlobReference(sourceBlob.Name);

                        using (var stream = await sourceBlob.OpenReadAsync())
                        {
                            await targetBlob.UploadFromStreamAsync(stream);
                        }

                        //log.Info($"Copied: {sourceBlob.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"ERROR copying blobs to target output: {ex.Message}");
            }
        }

        public static CloudBlockBlob GetOutputBlob(CloudBlobContainer sourceContainer, string filter, TraceWriter log)
        {
            CloudBlockBlob outputBlob = null;

            try
            {
                foreach (var blob in sourceContainer.ListBlobs(filter, true))
                {
                    log.Info($"Blob URI: {blob.Uri}");
                    if (!(blob is CloudBlockBlob blockBlob)) continue;
                    outputBlob = blockBlob;
                    break;
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error getting output blob: {ex.Message}");
            }

            return outputBlob;
        }

        public static async Task<IAsset> CreateAssetFromBlob(CloudBlockBlob blob, string assetName, TraceWriter log)
        {
            IAsset newAsset;

            try
            {
                Task<IAsset> copyAssetTask = CreateAssetFromBlobAsync(blob, assetName, log);
                newAsset = await copyAssetTask;
                log.Info($"Asset Copied : {newAsset.Id}");
            }
            catch (Exception ex)
            {
                log.Info("Copy Failed");
                log.Info($"ERROR : {ex.Message}");
                throw;
            }

            return newAsset;
        }

        /// <summary>
        /// Creates a new asset and copies blobs from the specifed storage account.
        /// </summary>
        /// <param name="blob">The specified blob.</param>
        /// <returns>The new asset.</returns>
        private static async Task<IAsset> CreateAssetFromBlobAsync(CloudBlockBlob blob, string assetName, TraceWriter log)
        {

            // Create a new asset. 
            var asset = _context.Assets.Create(blob.Name, AssetCreationOptions.None);
            log.Info($"Created new asset {asset.Name}");

            IAccessPolicy writePolicy = _context.AccessPolicies.Create("writePolicy",
                TimeSpan.FromHours(4), AccessPermissions.Write);
            ILocator destinationLocator = _context.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);
            CloudBlobClient destBlobStorage = _amstorageAccount.CreateCloudBlobClient();

            // Get the destination asset container reference
            string destinationContainerName = (new Uri(destinationLocator.Path)).Segments[1];
            CloudBlobContainer assetContainer = destBlobStorage.GetContainerReference(destinationContainerName);

            try
            {
                assetContainer.CreateIfNotExists();
            }
            catch (Exception ex)
            {
                log.Error("ERROR:" + ex.Message);
            }

            //log.Info("Created asset.");

            // Get hold of the destination blob
            CloudBlockBlob destinationBlob = assetContainer.GetBlockBlobReference(blob.Name);

            // Copy Blob
            try
            {
                using (var stream = await blob.OpenReadAsync())
                {
                    await destinationBlob.UploadFromStreamAsync(stream);
                }

                //log.Info("Copy Complete.");

                var assetFile = asset.AssetFiles.Create(blob.Name);
                assetFile.ContentFileSize = blob.Properties.Length;
                //assetFile.MimeType = "video/mp4";
                assetFile.IsPrimary = true;
                assetFile.Update();
                asset.Update();
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                log.Info(ex.StackTrace);
                log.Info("Copy Failed.");
                throw;
            }

            destinationLocator.Delete();
            writePolicy.Delete();

            return asset;
        }


        public static async Task<IAsset> CreateAssetFromBlobMultipleFiles(CloudBlockBlob blob, string assetName, TraceWriter log, List<AssetfileinJson> assetfilesAsset)
        {
            IAsset newAsset;

            try
            {
                Task<IAsset> copyAssetTask = CreateAssetFromBlobMultipleFilesAsync(blob, assetName, log, assetfilesAsset);
                newAsset = await copyAssetTask;
                //log.Info($"Asset Copied : {newAsset.Id}");
            }
            catch (Exception ex)
            {
                log.Info("Copy Failed");
                log.Info($"ERROR : {ex.Message}");
                throw;
            }

            return newAsset;
        }

        public static async Task CopyBlob(CloudBlockBlob source, CloudBlockBlob destination)
        {
            using (var stream = await source.OpenReadAsync())
            {
                await destination.UploadFromStreamAsync(stream);
            }

        }

        public static async Task<IAsset> CreateAssetFromBlobMultipleFilesAsync(CloudBlockBlob blob, string assetName, TraceWriter log, List<AssetfileinJson> assetfilesAsset)
        {
            //Get a reference to the storage account that is associated with the Media Services account. 
            StorageCredentials mediaServicesStorageCredentials =
                new StorageCredentials(_storageAccountName, _storageAccountKey);
            _amstorageAccount = new CloudStorageAccount(mediaServicesStorageCredentials, false);

            // Create a new asset. 
            var asset = _context.Assets.Create(Path.GetFileNameWithoutExtension(blob.Name), AssetCreationOptions.None);
            //log.Info($"Created new asset {asset.Name}");

            IAccessPolicy writePolicy = _context.AccessPolicies.Create("writePolicy",
                TimeSpan.FromHours(4), AccessPermissions.Write);
            ILocator destinationLocator = _context.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);
            CloudBlobClient destBlobStorage = _amstorageAccount.CreateCloudBlobClient();

            // Get the destination asset container reference
            string destinationContainerName = (new Uri(destinationLocator.Path)).Segments[1];
            CloudBlobContainer assetContainer = destBlobStorage.GetContainerReference(destinationContainerName);

            try
            {
                assetContainer.CreateIfNotExists();
            }
            catch (Exception ex)
            {
                log.Error("ERROR:" + ex.Message);
            }

            //log.Info("Created asset.");

            var sourceBlobContainer = blob.Container;

            foreach (var file in assetfilesAsset)
            {
                int nbtry = 0;
                var sourceBlob = sourceBlobContainer.GetBlobReference(file.fileName);

                while (!sourceBlob.Exists() && nbtry < 86400) // let's wait 24 hours max
                {
                    log.Info("File " + file.fileName + " does not exist... waiting...");
                    Thread.Sleep(1000); // let's wait for the blob to be there
                    nbtry++;
                }
                if (nbtry == 86440)
                {
                    log.Info("File " + file.fileName + " does not exist... File asset transfer canceled.");
                    break;
                }

                log.Info("File " + file.fileName + " found.");

                // Copy Blob
                try
                {
                    // Get hold of the destination blob
                    CloudBlockBlob destinationBlob = assetContainer.GetBlockBlobReference(file.fileName);

                    using (var stream = await sourceBlob.OpenReadAsync())
                    {
                        await destinationBlob.UploadFromStreamAsync(stream);
                    }

                    //log.Info("Copy Complete.");

                    var assetFile = asset.AssetFiles.Create(sourceBlob.Name);
                    assetFile.ContentFileSize = sourceBlob.Properties.Length;
                    //assetFile.MimeType = "video/mp4";
                    if (file.isPrimary)
                    {
                        assetFile.IsPrimary = true;
                    }
                    assetFile.Update();
                    asset.Update();
                }
                catch (Exception ex)
                {
                    log.Error(ex.Message);
                    log.Info(ex.StackTrace);
                    log.Info("Copy Failed.");
                    throw;
                }
            }


            destinationLocator.Delete();
            writePolicy.Delete();

            return asset;
        }





    }
}
