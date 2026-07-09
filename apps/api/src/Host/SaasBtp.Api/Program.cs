using SaasBtp.Access.Infrastructure.DependencyInjection;
using SaasBtp.Site.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAccessModule(builder.Configuration);
builder.Services.AddSiteModule(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

// Order is load-bearing: Access resolves authentication + tenant first; the Site middleware runs
// after and reads the populated tenant context to validate the requested site.
app.UseAccessModule();
app.UseSiteModule();

app.MapOpenApi();
app.MapAccessEndpoints();
app.MapSiteEndpoints();

app.Run();
