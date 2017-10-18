using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VPI.Entities
{
    public class Face
    {
        public int id { get; set; }
        public string shortId { get; set; }
        public string name { get; set; }
        public object description { get; set; }
        public object title { get; set; }
        public string thumbnailUrl { get; set; }
        public string thumbnailFullUrl { get; set; }
        public Appearance[] appearances { get; set; }
        public float seenDuration { get; set; }
        public float seenDurationRatio { get; set; }
    }
}
