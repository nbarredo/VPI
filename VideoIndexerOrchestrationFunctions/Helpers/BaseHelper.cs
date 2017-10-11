using System;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;

namespace OrchestrationFunctions
{
    public class BaseHelper
    {
      
     
        private readonly TraceWriter Log;

        public BaseHelper(TraceWriter log)
        {
            Log = log;
        }



        /// <summary>
        ///     Gets a URL with a SAS token that is good to read the file for 1 hour
        /// </summary>
        /// <param name="myBlob"></param>
        /// <returns></returns>
        public string GetSasUrl(CloudBlockBlob myBlob)
        {
            // expiry time set 5 minutes in the past to 1 hour in the future. THis can be
            // moved into configuration if needed
            var sasConstraints = new SharedAccessBlobPolicy
            {
                SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read
            };

            //Generate the shared access signature on the blob, setting the constraints directly on the signature.
            var sasBlobToken = myBlob.GetSharedAccessSignature(sasConstraints);
            return myBlob.Uri + sasBlobToken;
        }

        /// <summary>
        ///     Simple wrapper to write trace messages with a prefix (makes it more
        ///     readable in the output windows)
        /// </summary>
        /// <param name="message"></param>
        /// <param name="callerFilePath"></param>
        public  void LogMessage(string message, [CallerFilePath] string callerFilePath = "")
        {
            callerFilePath = Path.GetFileName(callerFilePath);
            Log.Info($"*** Function '{callerFilePath}' user trace ***  {message}");
        }

        #region Properties

     

        #endregion

     
    }
}