namespace VPI.Entities
{
    public class Breakdown
    {
        public string accountId { get; set; }
        public string id { get; set; }
        public string state { get; set; }
        public string processingProgress { get; set; }
        public object externalId { get; set; }
        public object externalUrl { get; set; }
        public object metadata { get; set; }
        public Insights insights { get; set; }
        public string thumbnailUrl { get; set; }
        public string publishedUrl { get; set; }
        public string viewToken { get; set; }
        public string sourceLanguage { get; set; }
        public string language { get; set; }
    }

}
