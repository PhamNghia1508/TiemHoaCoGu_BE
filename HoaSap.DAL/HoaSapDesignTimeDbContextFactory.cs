using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HoaSap.DAL;

public sealed class HoaSapDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HoaSapDbContext>
{
    public HoaSapDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HOASAP_SQLSERVER_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=SchemaGenerationOnly;Integrated Security=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<HoaSapDbContext>();
        options.UseSqlServer(
            connection,
            sql => sql.MigrationsAssembly(typeof(HoaSapDbContext).Assembly.GetName().Name));
        return new HoaSapDbContext(options.Options);
    }
}
