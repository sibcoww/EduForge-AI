using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EduForge.Api;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MaterialDbContext>
{
    public MaterialDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<MaterialDbContext>()
        .UseNpgsql("Host=localhost;Database=eduforge;Username=eduforge", options => options.MigrationsAssembly("EduForge.Infrastructure")).Options);
}
