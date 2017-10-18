namespace VPI.Entities
{
    public class Insights
    {
        public Transcriptblock[] transcriptBlocks { get; set; }
        public object[] topics { get; set; }
        public Face2[] faces { get; set; }
        public object[] participants { get; set; }
        public Contentmoderation contentModeration { get; set; }
        public object[] audioEffectsCategories { get; set; }
    }

}
