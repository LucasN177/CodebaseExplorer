using Shop.Application;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();

var app = builder.Build();
app.MapGet("/", () => "Shop");
app.Run();
