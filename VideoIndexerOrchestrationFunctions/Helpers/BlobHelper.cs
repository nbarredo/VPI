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
using OrchestrationFunctions.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OrchestrationFunctions
{
    public class BlobHelper : BaseHelper
    {
        static readonly string _storageAccountName = Environment.GetEnvironmentVariable("MediaServicesStorageAccountName");
        static readonly string _storageAccountKey = Environment.GetEnvironmentVariable("MediaServicesStorageAccountKey");

        
        private static CloudStorageAccount _amstorageAccount;

        private static readonly CloudMediaContext _context = MediaServicesHelper.Context;

        public static CloudStorageAccount AmsStorageAccount { get => _amstorageAccount; set => _amstorageAccount = value; }
 
        static BlobHelper()
        {
            //Get a reference to the storage account that is associated with the Media Services account. 
            StorageCredentials mediaServicesStorageCredentials =
                new StorageCredentials(_storageAccountName, _storageAccountKey);
            _amstorageAccount = new CloudStorageAccount(mediaServicesStorageCredentials, false);

        }

        public CloudBlobContainer GetCloudBlobContainer(string storageAccountName, string storageAccountKey, string containerName)
        {
            CloudStorageAccount sourceStorageAccount = new CloudStorageAccount(new StorageCredentials(storageAccountName, storageAccountKey), true);
            CloudBlobClient sourceCloudBlobClient = sourceStorageAccount.CreateCloudBlobClient();
            return sourceCloudBlobClient.GetContainerReference(containerName);
        }

        public void CopyBlobsAsync(CloudBlobContainer sourceBlobContainer, CloudBlobContainer destinationBlobContainer, TraceWriter Log)
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
                //Log.Info("Source blob : " + (sourceBlob as CloudBlob).Uri.ToString());
                CloudBlob destinationBlob = destinationBlobContainer.GetBlockBlobReference((sourceBlob as CloudBlob)?.Name);
                if (destinationBlob.Exists())
                {
                    Log.Info("Destination blob already exists. Skipping: " + destinationBlob.Uri);
                }
                else
                {
                   // Log.Info("Copying blob " + sourceBlob.Uri.ToString() + " to " + destinationBlob.Uri.ToString());
                    CopyBlobAsync(sourceBlob as CloudBlob, destinationBlob);
                }
            }
        }

        private async void CopyBlobAsync(CloudBlob sourceBlob, CloudBlob destinationBlob)
        {
            var signature = sourceBlob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24)
            });
            await destinationBlob.StartCopyAsync(new Uri(sourceBlob.Uri.AbsoluteUri + signature));
        }

        public CopyStatus MonitorBlobContainer(CloudBlobContainer destinationBlobContainer)
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
                    // However, let us Log copy state for diagnostics
                    copyStatus = CopyStatus.Pending;
                }
                // else we completed this pending copy
            }
            return copyStatus;
        }

        public async Task CopyBlobsToTargetContainer(CloudBlobContainer sourceContainer, CloudBlobContainer targetContainer )
        {
            try
            {
                foreach (var blob in sourceContainer.ListBlobs(string.Empty, true))
                {
                    //Log.Info($"Blob URI: {blob.Uri}");
                    if (blob is CloudBlockBlob sourceBlob)
                    {
                        //Log.Info($"Blob Name: {sourceBlob.Name}");
                        CloudBlockBlob targetBlob = targetContainer.GetBlockBlobReference(sourceBlob.Name);

                        using (var stream = await sourceBlob.OpenReadAsync())
                        {
                            await targetBlob.UploadFromStreamAsync(stream);
                        }

                        //Log.Info($"Copied: {sourceBlob.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ERROR copying blobs to target output: {ex.Message}");
            }
        }

        public CloudBlockBlob GetOutputBlob(CloudBlobContainer sourceContainer, string filter )
        {
            CloudBlockBlob outputBlob = null;

            try
            {
                foreach (var blob in sourceContainer.ListBlobs(filter, true))
                {
                    Log.Info($"Blob URI: {blob.Uri}");
                    if (!(blob is CloudBlockBlob blockBlob)) continue;
                    outputBlob = blockBlob;
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error getting output blob: {ex.Message}");
            }

            return outputBlob;
        }

        public async Task<IAsset> CreateAssetFromBlob(CloudBlockBlob blob)
        {
            IAsset newAsset;

            try
            {
                Task<IAsset> copyAssetTask = CreateAssetFromBlobAsync(blob);
                newAsset = await copyAssetTask;
                Log.Info($"Asset Copied : {newAsset.Id}");
            }
            catch (Exception ex)
            {
                Log.Info("Copy Failed");
                Log.Info($"ERROR : {ex.Message}");
                throw;
            }

            return newAsset;
        }

        /// <summary>
        /// Creates a new asset and copies blobs from the specifed storage account.
        /// </summary>
        /// <param name="blob">The specified blob.</param>
        /// <returns>The new asset.</returns>
        private async Task<IAsset> CreateAssetFromBlobAsync(CloudBlockBlob blob )
        {
       
            // Create a new asset. 
            var asset = _context.Assets.Create(blob.Name, AssetCreationOptions.None);
            Log.Info($"Created new asset {asset.Name}");

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
                Log.Error("ERROR:" + ex.Message);
            }

            //Log.Info("Created asset.");

            // Get hold of the destination blob
            CloudBlockBlob destinationBlob = assetContainer.GetBlockBlobReference(blob.Name);

            // Copy Blob
            try
            {
                using (var stream = await blob.OpenReadAsync())
                {
                    await destinationBlob.UploadFromStreamAsync(stream);
                }

                //Log.Info("Copy Complete.");

                var assetFile = asset.AssetFiles.Create(blob.Name);
                assetFile.ContentFileSize = blob.Properties.Length;
                //assetFile.MimeType = "video/mp4";
                assetFile.IsPrimary = true;
                assetFile.Update();
                asset.Update();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Log.Info(ex.StackTrace);
                Log.Info("Copy Failed.");
                throw;
            }

            destinationLocator.Delete();
            writePolicy.Delete();

            return asset;
        }


        public async Task<IAsset> CreateAssetFromBlobMultipleFiles(CloudBlockBlob blob, string assetName,  List<AssetFileinJson> assetfilesAsset)
        {
            IAsset newAsset;

            try
            {
                Task<IAsset> copyAssetTask = CreateAssetFromBlobMultipleFilesAsync(blob, assetName,   assetfilesAsset);
                newAsset = await copyAssetTask;
                Log.Info($"Asset Copied : {newAsset.Id}");
            }
            catch (Exception ex)
            {
                Log.Info("Copy Failed");
                Log.Info($"ERROR : {ex.Message}");
                throw;
            }

            return newAsset;
        }

        public async Task CopyBlob(CloudBlockBlob source, CloudBlockBlob destination)
        {
            using (var stream = await source.OpenReadAsync())
            {
                await destination.UploadFromStreamAsync(stream);
            }

        }

        private async Task<IAsset> CreateAssetFromBlobMultipleFilesAsync(CloudBlockBlob blob, string assetName,  List<AssetFileinJson> assetfilesAsset)
        {
            //Get a reference to the storage account that is associated with the Media Services account. 
            StorageCredentials mediaServicesStorageCredentials =
                new StorageCredentials(_storageAccountName, _storageAccountKey);
            _amstorageAccount = new CloudStorageAccount(mediaServicesStorageCredentials, false);

            // Create a new asset. 
            var asset = _context.Assets.Create(Path.GetFileNameWithoutExtension(blob.Name), AssetCreationOptions.None);
            //Log.Info($"Created new asset {asset.Name}");

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
                Log.Error("ERROR:" + ex.Message);
            }

            //Log.Info("Created asset.");

            var sourceBlobContainer = blob.Container;

            foreach (var file in assetfilesAsset)
            {
                int nbtry = 0;
                var sourceBlob = sourceBlobContainer.GetBlobReference(file.fileName);

                while (!sourceBlob.Exists() && nbtry < 86400) // let's wait 24 hours max
                {
                    Log.Info("File " + file.fileName + " does not exist... waiting...");
                    Thread.Sleep(1000); // let's wait for the blob to be there
                    nbtry++;
                }
                if (nbtry == 86440)
                {
                    Log.Info("File " + file.fileName + " does not exist... File asset transfer canceled.");
                    break;
                }

                Log.Info("File " + file.fileName + " found.");

                // Copy Blob
                try
                {
                    // Get hold of the destination blob
                    CloudBlockBlob destinationBlob = assetContainer.GetBlockBlobReference(file.fileName);

                    using (var stream = await sourceBlob.OpenReadAsync())
                    {
                        await destinationBlob.UploadFromStreamAsync(stream);
                    }

                    //Log.Info("Copy Complete.");

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
                    Log.Error(ex.Message);
                    Log.Info(ex.StackTrace);
                    Log.Info("Copy Failed.");
                    throw;
                }
            }


            destinationLocator.Delete();
            writePolicy.Delete();

            return asset;
        }


        public BlobHelper(TraceWriter Log) : base(Log)
        {
        }

    }
}
