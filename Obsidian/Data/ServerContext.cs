using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Obsidian.Data
{
    public class ServerContext : DbContext
    {
        // TODO: Add DbSet<T>s for each database model

        public ServerContext(DbContextOptions<ServerContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            base.OnModelCreating(modelBuilder);
        }
    }
}
