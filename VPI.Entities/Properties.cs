using System.Collections.Generic;

namespace VPI.Entities
{
    public class Properties
    {
        public string Language { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public Dictionary<string,string> Custom { get; set; }
    }
}
