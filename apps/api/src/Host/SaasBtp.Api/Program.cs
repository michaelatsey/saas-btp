using SaasBtp.Access.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// The Access module: the founding capability (BP-001), its CQRS pipeline, its persistence adapters
// over the DbUp-owned 'access' schema, and the GoTrue identity boundary.
// NOT YET EXPOSED: no endpoint is mapped. The founding call is necessarily unauthenticated and
// createUser confirms the address with no proof of control, so exposing it anonymously would allow
// identity pre-hijacking. The guard is an open decision — see the BP-001 plan's identity-bootstrap
// gap. Composing the module is safe; publishing a route is not.
builder.Services.AddAccessModule(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

// Site and Safety remain Host-dormant (their AddXModule / UseXModule / MapXEndpoints calls, and the
// connection strings they require at startup, are reinstated when their turn comes — their code is
// untouched). Access composes no request-pipeline stage yet: it has no endpoint to map.
app.MapOpenApi();

app.Run();
