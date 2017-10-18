using System;
using Microsoft.Azure.Documents;
using Newtonsoft.Json;

namespace VPI.Entities
{

    public class VideoBreakdown :Resource
    {
      
        [JsonProperty(PropertyName = "id")]
        public string id { get; set; }
        public string accountId { get; set; }
        public object partition { get; set; }
        public string name { get; set; }
        public object description { get; set; }
        public string userName { get; set; }
        public DateTime createTime { get; set; }
        public string organization { get; set; }
        public string privacyMode { get; set; }
        public string state { get; set; }
        public bool isOwned { get; set; }
        public bool isBase { get; set; }
        public int durationInSeconds { get; set; }
        public Summarizedinsights summarizedInsights { get; set; }
        public Breakdown[] breakdowns { get; set; }
        public Social social { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    } 

}
