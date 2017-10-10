// =============================================================================================================
// Adapted code from https://github.com/johndeu/media-services-dotnet-functions-integration/tree/master/shared
//  Special thanks to John Deutscher (https://github.com/johndeu) and Xavier Pouyat (https://github.com/xpouyat)
// 
// =============================================================================================================


using Microsoft.WindowsAzure.MediaServices.Client;
using System;
using System.Collections.Generic;
using System.Linq;

// ReSharper disable PossibleMultipleEnumeration
// ReSharper disable UseMethodAny.2

namespace OrchestrationFunctions
{
    public static class MediaServicesHelper
    {
        // Read values from the App.config file.
        private static readonly string _mediaServicesAccountName = Environment.GetEnvironmentVariable("MediaServicesAccountName");
        private static readonly string _mediaServicesAccountKey = Environment.GetEnvironmentVariable("MediaServicesAccountKey");



        // Field for service context.
        private static CloudMediaContext _context = null;

        public static CloudMediaContext Context { get => _context; set => _context = value; }

        static MediaServicesHelper()
        {
            // Static class initialization.. get a media context, etc. 
            // Create and cache the Media Services credentials in a static class variable.
            var cachedCredentials = new MediaServicesCredentials(
                _mediaServicesAccountName,
                _mediaServicesAccountKey);

            // Used the chached credentials to create CloudMediaContext.
            Context = new CloudMediaContext(cachedCredentials);
        }

        internal static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = Context.MediaProcessors.Where(p => p.Name == mediaProcessorName).
            ToList().OrderBy(p => new Version(p.Version)).LastOrDefault();

            if (processor == null)
                throw new ArgumentException($"Unknown media processor {mediaProcessorName}");

            return processor;
        }

        public static Uri GetValidOnDemandURI(IAsset asset)
        {
            var aivalidurls = GetValidURIs(asset);
            return aivalidurls?.FirstOrDefault();
        }

        public static IEnumerable<Uri> GetValidURIs(IAsset asset)
        {
            var ismFile = asset.AssetFiles.AsEnumerable().Where(f => f.Name.EndsWith(".ism")).OrderByDescending(f => f.IsPrimary).FirstOrDefault();

            if (ismFile == null) return null;
            var locators = asset.Locators.Where(l => l.Type == LocatorType.OnDemandOrigin && l.ExpirationDateTime > DateTime.UtcNow).OrderByDescending(l => l.ExpirationDateTime);

            var se = Context.StreamingEndpoints.AsEnumerable().Where(o => (o.State == StreamingEndpointState.Running) && (CanDoDynPackaging(o))).OrderByDescending(o => o.CdnEnabled);
            
            if (se.Count() == 0) // No running which can do dynpackaging SE. Let's use the default one to get URL
            {
                se = Context.StreamingEndpoints.AsEnumerable().Where(o => o.Name == "default").OrderByDescending(o => o.CdnEnabled);
            }

            var template = new UriTemplate("{contentAccessComponent}/{ismFileName}/manifest");

            IEnumerable<Uri> ValidURIs = locators.SelectMany(l =>
                    se.Select(
                        o =>
                            template.BindByPosition(new Uri("http://" + o.HostName), l.ContentAccessComponent,
                                ismFile.Name)))
                .ToArray();

            return ValidURIs;
        }

        public static Uri GetValidOnDemandPath(IAsset asset)
        {
            var aivalidurls = GetValidPaths(asset);
            return aivalidurls?.FirstOrDefault();
        }

        public static IEnumerable<Uri> GetValidPaths(IAsset asset)
        {
            var locators = asset.Locators.Where(l => l.Type == LocatorType.OnDemandOrigin && l.ExpirationDateTime > DateTime.UtcNow).OrderByDescending(l => l.ExpirationDateTime);

            var se = Context.StreamingEndpoints.AsEnumerable().Where(o => (o.State == StreamingEndpointState.Running) && (CanDoDynPackaging(o))).OrderByDescending(o => o.CdnEnabled);

            if (se.Count() == 0) // No running which can do dynpackaging SE. Let's use the default one to get URL
            {
                se = Context.StreamingEndpoints.AsEnumerable().Where(o => o.Name == "default").OrderByDescending(o => o.CdnEnabled);
            }

            var template = new UriTemplate("{contentAccessComponent}/");
            IEnumerable<Uri> ValidURIs = locators.SelectMany(l => se.Select(
                    o =>
                        template.BindByPosition(new Uri("http://" + o.HostName), l.ContentAccessComponent)))
                .ToArray();

            return ValidURIs;
        }

        public static bool CanDoDynPackaging(IStreamingEndpoint mySE)
        {
            return ReturnTypeSE(mySE) != StreamEndpointType.Classic;
        }

        public static StreamEndpointType ReturnTypeSE(IStreamingEndpoint mySE)
        {
            if (mySE.ScaleUnits != null && mySE.ScaleUnits > 0)
            {
                return StreamEndpointType.Premium;
            }
            if (new Version(mySE.StreamingEndpointVersion) == new Version("1.0"))
            {
                return StreamEndpointType.Classic;
            }
            return StreamEndpointType.Standard;
        }

        public enum StreamEndpointType
        {
            Classic = 0,
            Standard,
            Premium
        }

        //public static string ReturnContent(IAssetFile assetFile)
        //{
        //    string datastring = null;

        //    try
        //    {
        //        string tempPath = System.IO.Path.GetTempPath();
        //        string filePath = Path.Combine(tempPath, assetFile.Name);

        //        if (File.Exists(filePath))
        //        {
        //            File.Delete(filePath);
        //        }
        //        assetFile.Download(filePath);

        //        StreamReader streamReader = new StreamReader(filePath);
        //        datastring = streamReader.ReadToEnd();
        //        streamReader.Close();

        //        File.Delete(filePath);
        //    }
        //    catch
        //    {

        //    }

        //    return datastring;
        //}
    }
}
