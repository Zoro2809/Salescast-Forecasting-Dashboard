using ImprovedSalesForecast.DataStructures;
using ImprovedSalesForecast.SalesDashboard.Infrastructure.Data;
using ImprovedSalesForecast.SalesDashboard.Infrastructure.Setup;
using ImprovedSalesForecast.SalesDashboard.Queries;
using ImprovedSalesForecast.SalesDashboard.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ML;
using Serilog;
using Serilog.Events;

// ── Serilog early init ─────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting Improved Sales Forecast Dashboard");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // ── Database ───────────────────────────────────────────────────────────
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    builder.Services.AddDbContext<SalesContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.CommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds)));

    // ── Queries & Seeder ──────────────────────────────────────────────────
    builder.Services.AddScoped<ISalesQueries, SalesQueries>();
    builder.Services.AddScoped<DatabaseSeeder>();

    // ── Settings ───────────────────────────────────────────────────────────
    builder.Services.Configure<MLModelSettings>(
        builder.Configuration.GetSection("MLModels"));
    builder.Services.Configure<EnsembleWeightSettings>(
        builder.Configuration.GetSection("EnsembleWeights"));

    // ── ML Regression Model (only register if file exists) ────────────────
    var regressionModelPath = builder.Configuration["MLModels:RegressionModelPath"]
                              ?? "Forecast/ModelFiles/lightgbm_regression.zip";

    if (System.IO.File.Exists(regressionModelPath))
    {
        builder.Services
            .AddPredictionEnginePool<SaleData, SaleRegressionPrediction>()
            .FromFile(modelName: "LightGbm", filePath: regressionModelPath, watchForChanges: true);
        Log.Information("Regression model loaded from {Path}", regressionModelPath);
    }
    else
    {
        // Register a dummy pool so DI doesn't crash — controllers check for null model
        builder.Services.AddPredictionEnginePool<SaleData, SaleRegressionPrediction>();
        Log.Warning("Regression model not found at {Path}. Run ModelTrainer first.", regressionModelPath);
    }

    // ── MVC + Razor Pages ──────────────────────────────────────────────────
    builder.Services.AddControllersWithViews();
    builder.Services.AddRazorPages();

    var app = builder.Build();

    // ── Seed Database in background ────────────────────────────────────────
    _ = Task.Run(async () =>
    {
        await Task.Delay(2000); // wait for app to start
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        try { await seeder.SeedAsync(); }
        catch (Exception ex) { Log.Error(ex, "Database seeding failed"); }
    });

    // ── Pipeline ───────────────────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();
    else
        app.UseExceptionHandler("/Error");

    app.UseStaticFiles();
    app.UseRouting();
    app.MapControllers();
    app.MapRazorPages();
    app.MapGet("/", () => Results.Redirect("/Reports/Comparison"));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
