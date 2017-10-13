namespace VPI.Entities
{
    public class BlobInfo
    {
        public Microsoft.WindowsAzure.Storage.Blob.CloudBlockBlob Blob { get; set; }

        public string JsonManifest { get; set; }
    }
}
