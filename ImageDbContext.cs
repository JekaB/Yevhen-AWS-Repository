using AwsEc2Subtask4.Models;
using Microsoft.EntityFrameworkCore;

namespace AwsEc2Subtask4
{
    public class ImageDbContext(DbContextOptions<ImageDbContext> options) : DbContext(options)
    {
        public DbSet<Image> Image { get; set; }
    }
}
