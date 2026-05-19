namespace ImprovedSalesForecast.SalesDashboard.Settings;

public class MLModelSettings
{
    public string RegressionModelPath { get; set; } = string.Empty;
    public string SsaModelFolder { get; set; } = string.Empty;
    public string SsaModelPathFormat { get; set; } = string.Empty;
}

public class EnsembleWeightSettings
{
    public double Regression { get; set; } = 0.6;
    public double TimeSeries { get; set; } = 0.4;
}
