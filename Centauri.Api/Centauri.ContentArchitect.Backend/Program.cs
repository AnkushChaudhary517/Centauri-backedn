using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Services;
using Centauri.ContentArchitect.Backend.Services.Calculators;
using Centauri.ContentArchitect.Backend.Services.Clients;
using Centauri.ContentArchitect.Backend.Services.Parsers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DataForSeoOptions>(builder.Configuration.GetSection("ExternalApis:DataForSeo"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("ExternalApis:Gemini"));
builder.Services.Configure<SearchConsoleOptions>(builder.Configuration.GetSection("ExternalApis:SearchConsole"));
builder.Services.Configure<AnalysisOptions>(builder.Configuration.GetSection("Analysis"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(
            "http://localhost:8080",
            "https://localhost:8080",
            "http://127.0.0.1:8080",
            "https://127.0.0.1:8080")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddScoped<IContentArchitectService, ContentArchitectService>();
builder.Services.AddScoped<IKeywordCalculator, KeywordCalculator>();
builder.Services.AddScoped<KeywordDifficultyCalculator>();
builder.Services.AddScoped<IndexabilityCalculator>();
builder.Services.AddScoped<TrafficPotentialCalculator>();
builder.Services.AddScoped<QuestionCoverageCalculator>();
builder.Services.AddScoped<ContentGapCalculator>();
builder.Services.AddScoped<EeatInformationGainCalculator>();

builder.Services.AddScoped<IKeywordDataClient, DataForSeoClient>();
builder.Services.AddScoped<ISerpDataClient, DataForSeoClient>();
builder.Services.AddScoped<IBacklinkDataClient, DataForSeoClient>();
builder.Services.AddScoped<ISearchConsoleClient, GoogleSearchConsoleClient>();
builder.Services.AddScoped<IPublicIndexabilityClient, PublicIndexabilityClient>();
builder.Services.AddScoped<IGeminiClient, GeminiClient>();
builder.Services.AddScoped<IWebPageParser, HtmlWebPageParser>();
builder.Services.AddScoped<ISitemapService, SitemapService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.MapControllers();

app.Run();
