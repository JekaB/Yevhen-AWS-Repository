namespace AwsEc2Subtask4.Models
{    
    public class Image
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime LastUpdateDate { get; set; }
        public long ImageSize { get; set; }
        public string Extension { get; set; }
    }
}
