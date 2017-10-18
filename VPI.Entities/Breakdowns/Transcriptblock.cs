namespace VPI.Entities
{
    public class Transcriptblock
    {
        public int id { get; set; }
        public Line[] lines { get; set; }
        public object[] sentimentIds { get; set; }
        public object[] thumbnailsIds { get; set; }
        public float sentiment { get; set; }
        public Face1[] faces { get; set; }
        public object[] ocrs { get; set; }
        public object[] audioEffectInstances { get; set; }
        public Scene[] scenes { get; set; }
        public Annotation[] annotations { get; set; }
    }

}
