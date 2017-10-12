using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace VPI.Entities
{
    public class BlobInfo
    {
        public CloudBlockBlob Blob { get; set; }

        public string JsonManifest { get; set; }
    }
}
