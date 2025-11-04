namespace WeatherAPI.Models
{
    public class WeatherData
    {
        public string CityName { get; set; }
        public double Temperature { get; set; }
        public string Description { get; set; }
        public int Humidity { get; set; }
        public double WindSpeed { get; set; }
    }
}
