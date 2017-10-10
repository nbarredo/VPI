using System;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Newtonsoft.Json;

namespace VPI.Entities
{
    public class VippyProcessingState : Resource
    {
        //[JsonProperty(PropertyName = "id")]
        //public string Id { get; set; }
        public string AlternateId { get; set; }

        public string ProcessingState { get; set; }
    
        public string DocumentType => "state";

        public string AmsAssetId { get; set; }
    
        public string VideoIndexerId { get; set; }    

        public string BlobName { get; set; }

        public string videoTitle { get; set; }

        public string VideoDescription { get; set; }
    
        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public string ErrorMessage { get; set; }

        public string[] Transcripts { get; set; }

        public List<ProcessingMessage> ProcessingMessage { get; set; }
    
        public int StepNumber { get; set; }

        public int StepCount { get; set; }
        /// <summary>
        /// This holds the optional client values passed in via the json manifest
        /// </summary>
        public  Properties  CustomProperties { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class ProcessingMessage
    {
        public string Message { get; set; }

        public string LogLevel { get; set; }

        public DateTime TimeStamp { get; set; }
    }
    
    
}
