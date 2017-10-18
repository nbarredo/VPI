namespace VPI.Entities
{
    public class Scene
    {
        public int id { get; set; }
        public Timerange2 timeRange { get; set; }
        public string keyFrame { get; set; }
        public Shot[] shots { get; set; }
    }

}
