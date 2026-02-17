namespace WeatherAPI.Models
{
    public class WeatherData
    {
        public string CityName { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public double Temperature { get; set; }
        public double FeelsLike { get; set; }
        public double TempHigh { get; set; }
        public double TempLow { get; set; }
        public string Description { get; set; }
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
        public string LocalTime { get; set; }
        public string Sunrise { get; set; }
        public string Sunset { get; set; }
        public int? AirQuality { get; set; }
        public string AirQualityLabel { get; set; }
        public List<DailyForecast> Forecast { get; set; }
    }

    public class DailyForecast
    {
        public string Date { get; set; }
        public double TempHigh { get; set; }
        public double TempLow { get; set; }
        public string Description { get; set; }
    }
}
