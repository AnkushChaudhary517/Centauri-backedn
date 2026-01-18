using Amazon.DynamoDBv2;
using Amazon.S3;
using Centauri_Api.Impl;
using Centauri_Api.Interface;
using Centauri_Api.Middleware;
using CentauriSeo.Application.Pipeline;
using CentauriSeo.Application.Services;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.Data;
using CentauriSeo.Infrastructure.LlmClients;
using CentauriSeo.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSwaggerGen();
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddSingleton<AiUsageRepository>();
builder.Services.AddSingleton<AiCallTracker>();

// CORS - allow React dev origin by default, configurable via appsettings.json
//var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
//                     ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        policy.AllowAnyOrigin()
        //.WithOrigins(allowedOrigins)
              .AllowAnyHeader().AllowAnyMethod()
              .AllowAnyMethod();
              //.AllowCredentials();
    });
});

//// register EF Core Sqlite for caching
//var connectionString = builder.Configuration.GetConnectionString("LlmCache") 
//                       ?? "Data Source=llmcache.db";
//builder.Services.AddDbContext<LlmCacheDbContext>(options =>
//    options.UseSqlite(connectionString));

// register cache service
builder.Services.AddSingleton<ILlmCacheService, InMemoryCacheService>();

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings.GetValue<string>("SecretKey") ?? "your-secret-key-that-is-at-least-32-characters-long-for-hs256";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});

builder.Services.AddHttpClient<GroqClient>(c =>
{
    c.BaseAddress = new Uri("https://api.groq.com");
    var apiKey = builder.Configuration["GroqApiKey"]?.DecodeBase64();
    if (!string.IsNullOrWhiteSpace(apiKey))
        c.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
});

builder.Services.AddMemoryCache();
var openAiKey = builder.Configuration["OpenAiKey"]?.DecodeBase64();
// register LLM clients (HttpClient already configured earlier)
builder.Services.AddHttpClient<OpenAiClient>(c =>
{
    c.BaseAddress = new Uri("https://api.openai.com");
    c.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiKey}");
});

builder.Services.AddHttpClient<GeminiClient>(c =>
{
    c.BaseAddress = new Uri("https://generativelanguage.googleapis.com");
    c.DefaultRequestHeaders.Add("x-goog-api-key", "AIzaSyB2NNIPmTtdbZV7sjNgDeVgyVkyqOa0Rt8");
});

builder.Services.AddHttpClient<PerplexityClient>(c =>
{
    c.BaseAddress = new Uri("https://api.perplexity.ai");
    c.DefaultRequestHeaders.Add("Authorization", "Bearer YOUR_KEY");
});


// register orchestrator service
builder.Services.AddSingleton<Phase1And2OrchestratorService>();
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
// Register DynamoDB client for DI
builder.Services.AddAWSService<IAmazonDynamoDB>();

// Register your repository
builder.Services.AddSingleton<IDynamoDbService, DynamoDbService>();
// Application services
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IAuthService, AuthService>();

var app = builder.Build();
app.UseMiddleware<UserContextMiddleware>();
// Ensure database created (simple and safe)
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<LlmCacheDbContext>();
//    db.Database.EnsureCreated();
//}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// MUST call UseCors before MapControllers / endpoints
app.UseCors("DefaultCorsPolicy");

app.MapControllers();
app.Run();

