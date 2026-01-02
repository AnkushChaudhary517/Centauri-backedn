using Microsoft.EntityFrameworkCore;
using CentauriSeo.Infrastructure.LlmClients;
using CentauriSeo.Application.Services;
using CentauriSeo.Infrastructure.Data;
using CentauriSeo.Application.Pipeline;
using CentauriSeo.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

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

// register EF Core Sqlite for caching
var connectionString = builder.Configuration.GetConnectionString("LlmCache") 
                       ?? "Data Source=llmcache.db";
builder.Services.AddDbContext<LlmCacheDbContext>(options =>
    options.UseSqlite(connectionString));

// register cache service
builder.Services.AddScoped<ILlmCacheService, LlmCacheService>();

builder.Services.AddHttpClient<GroqClient>(c =>
{
    c.BaseAddress = new Uri("https://api.groq.com");
    var apiKey = builder.Configuration["GroqApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        c.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
});

// register LLM clients (HttpClient already configured earlier)
builder.Services.AddHttpClient<OpenAiClient>(c =>
{
    c.BaseAddress = new Uri("https://api.openai.com");
    c.DefaultRequestHeaders.Add("Authorization", "Bearer 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");
});

builder.Services.AddHttpClient<GeminiClient>(c =>
{
    c.BaseAddress = new Uri("https://generativelanguage.googleapis.com");
    c.DefaultRequestHeaders.Add("x-goog-api-key", "AIzaSyBVxLk4soBJFpbuAXSU8W7tPAQTI-nmWjw");
});

builder.Services.AddHttpClient<PerplexityClient>(c =>
{
    c.BaseAddress = new Uri("https://api.perplexity.ai");
    c.DefaultRequestHeaders.Add("Authorization", "Bearer YOUR_KEY");
});

// register orchestrator service
builder.Services.AddScoped<Phase1And2OrchestratorService>();

var app = builder.Build();

// Ensure database created (simple and safe)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LlmCacheDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// MUST call UseCors before MapControllers / endpoints
app.UseCors("DefaultCorsPolicy");

app.MapControllers();
app.Run();

