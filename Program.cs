using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 🔥 IMPORTANTE
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=licenses.db"));

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();