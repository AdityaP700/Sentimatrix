using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SentimatrixAPI.Services;
using Microsoft.OpenApi.Models;
using SentimatrixAPI.Hubs;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using SentimatrixAPI.Models.Settings;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Sentimatrix API", 
        Version = "v1",
        Description = "API for processing emails and analyzing sentiment"
    });
});
builder.Services.AddSignalR();

// Register GroqService
builder.Services.AddSingleton<GroqService>();

// Add MongoDB Settings
builder.Services.Configure<MongoDBSettings>(
    builder.Configuration.GetSection("MongoDBSettings"));

builder.Services.AddSingleton<EmailService>();

// Register MongoClient and IMongoDatabase
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<MongoDBSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var settings = sp.GetRequiredService<IOptions<MongoDBSettings>>().Value;
    return client.GetDatabase(settings.DatabaseName);
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Add these lines after other service configurations
builder.Services.Configure<GroqSettings>(
    builder.Configuration.GetSection("GROQ"));
builder.Services.Configure<RedisSettings>(
    builder.Configuration.GetSection("Redis"));

builder.Services.AddHttpClient();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<GroqService>();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetSection("Redis:ConnectionString").Value;
});

if (builder.Environment.IsDevelopment())
{
    // Comment out or remove HTTPS redirection for development
    // builder.Services.AddHttpsRedirection(options =>
    // {
    //     options.HttpsPort = 5001;
    // });
}

builder.WebHost.UseUrls("http://localhost:5000");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sentimatrix API V1");
        c.RoutePrefix = "swagger";
    });
    
    // Skip HTTPS redirection in development
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TicketHub>("/ticketHub");

app.Run();