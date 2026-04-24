namespace BionicPRO.Reports
{
    public class UserReport
    {
        public Guid UserId { get; set; }
        public DateTime ReportDate { get; set; }
        public string FullName { get; set; }
        public string ProstheticModel { get; set; }
        public uint StepsCount { get; set; }
        public float AvgBatteryLevel { get; set; }
        public byte ErrorsCount { get; set; }
    }

}
