using Microsoft.EntityFrameworkCore;
using UsersApp.Data;

// Рядок підключення береться зі змінної середовища (різний для різних середовищ)
var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("ConnectionStrings__Default is not set.");
    return 1;
}

var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;

// до 10 спроб: база може ще стартувати
for (var attempt = 1; attempt <= 10; attempt++)
{
    try
    {
        using var db = new AppDbContext(options);
        db.Database.Migrate();
        Console.WriteLine("Migrations applied.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Attempt {attempt} failed: {ex.Message}");
        Thread.Sleep(3000);
    }
}
return 1;
