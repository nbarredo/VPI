namespace VPI.Entities
{
    public class Line
    {
        public int id { get; set; }
        public Timerange timeRange { get; set; }
        public Adjustedtimerange adjustedTimeRange { get; set; }
        public int participantId { get; set; }
        public string text { get; set; }
        public bool isIncluded { get; set; }
        public float confidence { get; set; }
    }

}
