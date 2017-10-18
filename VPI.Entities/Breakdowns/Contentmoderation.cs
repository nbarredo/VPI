namespace VPI.Entities
{
    public class Contentmoderation
    {
        public float adultClassifierValue { get; set; }
        public int bannedWordsCount { get; set; }
        public float bannedWordsRatio { get; set; }
        public bool isSuspectedAsAdult { get; set; }
        public bool isAdult { get; set; }
    }

}
