using SaasBtp.Access.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAccessModule(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAccessModule();

app.MapOpenApi();
app.MapAccessEndpoints();

app.Run();
