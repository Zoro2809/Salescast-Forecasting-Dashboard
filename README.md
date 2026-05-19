# SalesCast: ML-Based Sales Forecasting Dashboard

A full-stack machine learning sales forecasting dashboard built on the Microsoft .NET ecosystem. Predicts next month's sales for any retail product using an ensemble of LightGBM and SSA Time Series models.

## Project Overview

SalesCast processes the UCI Online Retail Dataset (541,909 transactions) and provides real-time sales forecasts through an interactive web dashboard. Business analysts can search any product and instantly get a data-driven forecast — no coding required.


## Features

- **LightGBM Regression** — Predicts next month's units using 13 engineered features
- **SSA Time Series** — 3-month forecast with 95% confidence intervals per product
- **Ensemble Model** — 60% LightGBM + 40% SSA weighted combination (best accuracy)
- **Model Comparison** — All 3 forecasts overlaid on one interactive chart
- **Live Autocomplete Search** — Search across 4,000+ products instantly
- **Interactive Charts** — Plotly.js time-series visualizations on every page

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 8 |
| Web Framework | ASP.NET Core 8 Razor Pages |
| ML Framework | ML.NET 3.0.1 |
| Gradient Boosting | LightGBM |
| Time Series | SSA (Singular Spectrum Analysis) |
| ORM | Entity Framework Core 8 |
| Micro-ORM | Dapper |
| Database | SQL Server / LocalDB |
| CSV Parsing | CsvHelper |
| Logging | Serilog |
| Charts | Plotly.js |
| UI | Bootstrap 5, Inter Font, Font Awesome 6 |

## Dataset

- **Name:** UCI Online Retail Dataset
- **Source:** UCI Machine Learning Repository
- **Size:** 541,909 transaction rows
- **Period:** December 2010 – December 2011
- **After Cleaning:** ~397,884 rows
- **Searchable Products:** ~430 products (6+ months history)

## Project Structure

```text
ImprovedSalesForecast/
│
├── src/
│   │
│   ├── Shared/                 # Shared data structures (SaleData, Predictions)
│   │
│   ├── ModelTrainer/           # Console app — trains LightGBM + SSA models
│   │   │
│   │   ├── DataProcessing/     # CSV loading, cleaning, monthly aggregation
│   │   ├── RegressionTrainer/  # LightGBM training pipeline
│   │   └── TimeSeriesTrainer/  # SSA per-product training
│   │
│   └── SalesDashboard/         # ASP.NET Core 8 web application
│       │
│       ├── Controllers/        # ForecastController, CatalogController
│       ├── Pages/Reports/      # Razor Pages (Regression, TimeSeries, Ensemble, Comparison)
│       ├── EntityModels/       # Product, MonthlySale EF Core entities
│       ├── Queries/            # Dapper SQL queries
│       ├── Infrastructure/     # DbContext, DatabaseSeeder
│       └── wwwroot/            # CSS, JS (forecast.js, Plotly.js)
```


### Prerequisites
- .NET 8 SDK
- SQL Server or SQL Server LocalDB
- UCI Online Retail CSV file placed in the data folder

The database seeds automatically on first run (~30–60 seconds).


ML Models
LightGBM
13 input features: lag-1, lag-3, lag-12, rolling mean-3, rolling mean-6, units, avg/max/min daily units, sales days, unit price, year, month
One-hot encoding on ProductId
5-fold cross-validation before final training
Hyperparameters: numberOfLeaves=40, numberOfIterations=200, learningRate=0.05, minimumExampleCountPerLeaf=10
SSA (Singular Spectrum Analysis)
Pre-trained for top 50 products by volume
Minimum 12 months of history required
3-month forecast horizon with 95% confidence intervals
On-demand fallback for all other products
Ensemble
60% LightGBM + 40% SSA (configurable in appsettings.json)
Consistently lower error than either model alone
Database
Name: ImprovedSalesDashboard (SQL Server)
Tables: Products (~4,000 rows), MonthlySales (~5,800 rows)
Schema created automatically via EF Core on first run
Runtime queries use Dapper with SQL window functions (LAG, LEAD, AVG OVER)
Dashboard Pages
Page	Description
Model Comparison	All 3 forecasts on one chart — default landing page
LightGBM Regression	Single next-month prediction with history chart
SSA Time Series	3-month forecast table with confidence intervals
Ensemble Forecast	Weighted combination KPI cards + chart

