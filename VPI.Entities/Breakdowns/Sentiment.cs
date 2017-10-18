using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VPI.Entities
{

    public class Sentiment
    {
        public string sentimentKey { get; set; }
        public Appearance1[] appearances { get; set; }
        public float seenDurationRatio { get; set; }
    }
}
