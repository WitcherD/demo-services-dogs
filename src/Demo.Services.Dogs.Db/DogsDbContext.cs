using Demo.Services.Dogs.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace Demo.Services.Dogs.Db;

public class DogsDbContext : DbContext
{
    public DogsDbContext(DbContextOptions<DogsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }

    public DbSet<DogImage> Dogs { get; set; }
}