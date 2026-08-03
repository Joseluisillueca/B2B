using B2B.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace B2B.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string SeededEmail = "integracion@test.com";
    public const string SeededPassword = "secreto123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("b2b-tests"));

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            if (!db.Users.Any(u => u.Email == SeededEmail))
            {
                var user = new AppUser { Id = Guid.NewGuid(), Email = SeededEmail, PasswordHash = "" };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, SeededPassword);
                db.Users.Add(user);
                db.SaveChanges();
            }
        });
    }
}
