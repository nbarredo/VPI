using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace OrchestrationFunctions
{
    public static class AMSNotificationHttpHandler
    {
        [FunctionName("AMSNotificationHttpHandler")]
        public static async Task<object> Run(
            [HttpTrigger(WebHookType = "genericJson", Route = "amscallback")] HttpRequestMessage req,
            [Queue("%EncodingCompleteQueue%", Connection = "AzureWebJobsStorage")] IAsyncCollector<string> outputQueue,
            TraceWriter log)
        { 
            log.Info($"Webhook was triggered by {req.Headers.UserAgent}");


            var jsonContent = await req.Content.ReadAsStringAsync();
            await outputQueue.AddAsync(jsonContent); 
            //log.Info($"Input JSON is-:{jsonContent}");

            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}       