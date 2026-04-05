using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SqlChatbot.Data;
using SqlChatbot.Services;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// Register Semantic Kernel with Google AI (Gemini)
var googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
var modelId = Environment.GetEnvironmentVariable("GOOGLE_MODEL_ID") ?? "gemini-1.5-flash";

builder.Services.AddGoogleAIGeminiChatCompletion(
    modelId: modelId,
    apiKey: googleApiKey!
);

builder.Services.AddScoped<SqlChatService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Initialize Shoppy database with seed data on startup
await SeedDatabase.InitializeAsync(
    builder.Configuration["Database:ConnectionString"]!);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SQL Chatbot API v1");
    });
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.MapControllers();
app.Run();