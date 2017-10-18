using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VPI.Entities
{

    public class Summarizedinsights
    {
        public string name { get; set; }
        public string shortId { get; set; }
        public int privacyMode { get; set; }
        public Duration duration { get; set; }
        public string thumbnailUrl { get; set; }
        public Face[] faces { get; set; }
        public object[] topics { get; set; }
        public Sentiment[] sentiments { get; set; }
        public object[] audioEffects { get; set; }
    }
}
