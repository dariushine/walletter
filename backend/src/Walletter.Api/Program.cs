using Walletter.Application;
using Walletter.Api;
using Walletter.Infrastructure;
using Walletter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    // Prefijo global /api (equivalente a setGlobalPrefix('api') del rework).
    options.UseGeneralRoutePrefix("/api");
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Capas (Application + Infrastructure = Clean Architecture; la DB queda aislada).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication("BearerOrApi")
    .AddScheme<AuthenticationSchemeOptions, Walletter.Api.Auth.ApiOrBearerHandler>("BearerOrApi", null);
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Middleware de manejo global de errores (mapea excepciones → HTTP).
app.UseMiddleware<Walletter.Api.ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Aplica migraciones y siembra datos al arrancar.
await DatabaseInitializer.InitializeAsync(app.Services);

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Configura la URL de escucha. Respeta ASPNETCORE_URLS si ya está definido
// (Docker lo provee); si no, usa el puerto de PORT o el default 3002.
if (!builder.Configuration["ASPNETCORE_URLS"]?.Contains(':') == true)
{
    var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 3002;
    app.Urls.Add($"http://0.0.0.0:{port}");
}

Console.WriteLine($"[Finance] API (ASP.NET Core + EF Core) iniciada");

await app.RunAsync();
