using Microsoft.AspNetCore.Mvc;
using WeatherAPI.Models;
using System.Text.Json;

namespace WeatherAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WeatherController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public WeatherController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("{city}")]
        public async Task<ActionResult<WeatherData>> GetWeather(string city)
        {
            var client = _httpClientFactory.CreateClient();
            
            // Step 1: Geocode the city name to get coordinates
            var geocodeUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=1&language=en&format=json";
            var geocodeResponse = await client.GetAsync(geocodeUrl);
            
            if (!geocodeResponse.IsSuccessStatusCode)
            {
                return NotFound($"City {city} not found");
            }

            var geocodeContent = await geocodeResponse.Content.ReadAsStringAsync();
            var geocodeJson = JsonDocument.Parse(geocodeContent);
            
            if (!geocodeJson.RootElement.TryGetProperty("results", out var results) || 
                results.GetArrayLength() == 0)
            {
                return NotFound($"City {city} not found");
            }

            var firstResult = results[0];
            var latitude = firstResult.GetProperty("latitude").GetDouble();
            var longitude = firstResult.GetProperty("longitude").GetDouble();
            var cityName = firstResult.GetProperty("name").GetString();
            
            // Step 2: Get weather data using coordinates
            var weatherUrl = $"https://api.open-meteo.com/v1/forecast?" +
                           $"latitude={latitude}&longitude={longitude}" +
                           $"&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code" +
                           $"&temperature_unit=celsius&wind_speed_unit=ms";
            
            var weatherResponse = await client.GetAsync(weatherUrl);
            
            if (!weatherResponse.IsSuccessStatusCode)
            {
                return StatusCode(500, "Failed to fetch weather data");
            }

            var weatherContent = await weatherResponse.Content.ReadAsStringAsync();
            var weatherJson = JsonDocument.Parse(weatherContent);
            var current = weatherJson.RootElement.GetProperty("current");
            
            var weatherCode = current.GetProperty("weather_code").GetInt32();
            
            var weatherData = new WeatherData
            {
                CityName = cityName,
                Temperature = current.GetProperty("temperature_2m").GetDouble(),
                Description = GetWeatherDescription(weatherCode),
                Humidity = current.GetProperty("relative_humidity_2m").GetInt32(),
                WindSpeed = current.GetProperty("wind_speed_10m").GetDouble()
            };

            return Ok(weatherData);
        }

        private string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "Clear sky",
                1 => "Mainly clear",
                2 => "Partly cloudy",
                3 => "Overcast",
                45 or 48 => "Foggy",
                51 or 53 or 55 => "Drizzle",
                61 or 63 or 65 => "Rain",
                71 or 73 or 75 => "Snow",
                77 => "Snow grains",
                80 or 81 or 82 => "Rain showers",
                85 or 86 => "Snow showers",
                95 => "Thunderstorm",
                96 or 99 => "Thunderstorm with hail",
                _ => "Unknown"
            };
        }
    }
}
