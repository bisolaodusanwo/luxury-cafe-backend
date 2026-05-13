using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using MaisonGlace.API.Services;
using MaisonGlace.API.Settings;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ── Bind settings ──────────────────────────────────────────────────────────
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));

// ── Singleton services ────────────────────────────────────────────────────
builder.Services.AddSingleton<DatabaseContext>();
builder.Services.AddSingleton<BookingService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<AuthService>();

// ── JWT authentication ────────────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings.Issuer,
            ValidAudience            = jwtSettings.Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ── CORS (allow Next.js dev server + production domain) ────────────────────
var allowedOrigins = (builder.Configuration["AllowedOrigins"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(opts =>
{
    opts.AddPolicy("Frontend", p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod());
});

var app = builder.Build();

if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 16)
{
    app.Logger.LogError("JWT secret is missing or too short. Set Jwt__Secret to at least 16 characters in Render env vars.");
}

// ── Startup diagnostics for Render logs ───────────────────────────────────
var envName = app.Environment.EnvironmentName;
var mongoConn = app.Configuration["MongoDb:ConnectionString"] ?? string.Empty;
var mongoConfigured = !string.IsNullOrWhiteSpace(mongoConn);
var mongoTarget = "not-configured";

if (mongoConfigured)
{
    try
    {
        var mongoUrl = MongoUrl.Create(mongoConn);
        var host = mongoUrl.Server?.ToString() ?? "unknown-host";
        var db = string.IsNullOrWhiteSpace(mongoUrl.DatabaseName) ? "unknown-db" : mongoUrl.DatabaseName;
        mongoTarget = $"{host}/{db}";
    }
    catch
    {
        mongoTarget = "invalid-connection-string";
    }
}

app.Logger.LogInformation("Startup environment: {Environment}", envName);
app.Logger.LogInformation("Mongo configured: {MongoConfigured}; target: {MongoTarget}", mongoConfigured, mongoTarget);
app.Logger.LogInformation("AllowedOrigins: {AllowedOrigins}", string.Join(",", allowedOrigins));

// ── Seed admin account on startup ──────────────────────────────────────────
var authService = app.Services.GetRequiredService<AuthService>();
try
{
    await authService.SeedAdminAsync(
        app.Configuration["Admin:Email"] ?? "abizmichelle@gmail.com",
        app.Configuration["Admin:Password"] ?? "Bisola1369");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Admin seed skipped: unable to connect to MongoDB during startup.");
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        app.Logger.LogError(ex, "Unhandled exception. Path: {Path}; Method: {Method}", context.Request.Path, context.Request.Method);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "Internal server error" });
    });
});

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

async Task<IResult> HealthCheck(DatabaseContext db, HttpContext context)
{
    try
    {
        await db.PingAsync(context.RequestAborted);
        return Results.Ok(new { status = "ok", database = "ok", utc = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "MongoDB ping failed from health endpoint.");
        return Results.Problem(
            title: "Database unavailable",
            detail: "MongoDB ping failed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

app.MapGet("/health", HealthCheck);
app.MapGet("/healthz", HealthCheck);

// Render.com sets the PORT env var; fall back to 8080 for local Docker
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
