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
        public async Task<ActionResult<WeatherData>> GetWeather(string city, [FromQuery] string? state = null, [FromQuery] string? country = null)
        {
            var client = _httpClientFactory.CreateClient();

            // Step 1: Geocode the city name to get coordinates
            var geocodeUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=10&language=en&format=json";
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

            // Expand 2-letter state abbreviation to full name
            var expandedState = state;
            if (!string.IsNullOrWhiteSpace(state) && state.Length == 2)
            {
                var fullName = ExpandStateAbbreviation(state);
                if (fullName != null) expandedState = fullName;
            }

            // Filter results by state and country using best-match scoring
            JsonElement matchedResult = results[0];
            int bestScore = 0;

            for (int i = 0; i < results.GetArrayLength(); i++)
            {
                var result = results[i];
                int score = 0;

                if (!string.IsNullOrWhiteSpace(expandedState) && result.TryGetProperty("admin1", out var admin1))
                {
                    var admin1Str = admin1.GetString();
                    if (admin1Str?.Contains(expandedState, StringComparison.OrdinalIgnoreCase) == true)
                        score++;
                }

                if (!string.IsNullOrWhiteSpace(country))
                {
                    if (result.TryGetProperty("country", out var countryProp) &&
                        countryProp.GetString()?.Contains(country, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        score++;
                    }
                    else if (result.TryGetProperty("country_code", out var countryCode) &&
                             countryCode.GetString()?.Equals(country, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        score++;
                    }
                }

                if (score > bestScore)
                {
                    matchedResult = result;
                    bestScore = score;
                }
            }

            var latitude = matchedResult.GetProperty("latitude").GetDouble();
            var longitude = matchedResult.GetProperty("longitude").GetDouble();
            var cityName = matchedResult.GetProperty("name").GetString();
            var matchedState = matchedResult.TryGetProperty("admin1", out var stateEl) ? stateEl.GetString() : null;
            var matchedCountry = matchedResult.TryGetProperty("country", out var countryEl) ? countryEl.GetString() : null;

            // Step 2: Get weather + daily forecast data
            var weatherUrl = $"https://api.open-meteo.com/v1/forecast?" +
                           $"latitude={latitude}&longitude={longitude}" +
                           $"&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code,apparent_temperature" +
                           $"&daily=temperature_2m_max,temperature_2m_min,weather_code,sunrise,sunset" +
                           $"&forecast_days=5&timezone=auto" +
                           $"&temperature_unit=celsius&wind_speed_unit=ms";

            // Step 3: Get air quality data (fire and forget style, non-blocking)
            var aqiUrl = $"https://air-quality-api.open-meteo.com/v1/air-quality?" +
                        $"latitude={latitude}&longitude={longitude}&current=us_aqi";

            var weatherTask = client.GetAsync(weatherUrl);
            var aqiTask = client.GetAsync(aqiUrl);

            await Task.WhenAll(weatherTask, aqiTask);

            var weatherResponse = await weatherTask;
            if (!weatherResponse.IsSuccessStatusCode)
            {
                return StatusCode(500, "Failed to fetch weather data");
            }

            var weatherContent = await weatherResponse.Content.ReadAsStringAsync();
            var weatherJson = JsonDocument.Parse(weatherContent);
            var current = weatherJson.RootElement.GetProperty("current");
            var daily = weatherJson.RootElement.GetProperty("daily");

            var weatherCode = current.GetProperty("weather_code").GetInt32();
            var localTime = current.GetProperty("time").GetString();

            // Parse daily forecast
            var dates = daily.GetProperty("time");
            var maxTemps = daily.GetProperty("temperature_2m_max");
            var minTemps = daily.GetProperty("temperature_2m_min");
            var dailyCodes = daily.GetProperty("weather_code");
            var sunrises = daily.GetProperty("sunrise");
            var sunsets = daily.GetProperty("sunset");

            var forecast = new List<DailyForecast>();
            for (int i = 0; i < dates.GetArrayLength(); i++)
            {
                forecast.Add(new DailyForecast
                {
                    Date = dates[i].GetString(),
                    TempHigh = maxTemps[i].GetDouble(),
                    TempLow = minTemps[i].GetDouble(),
                    Description = GetWeatherDescription(dailyCodes[i].GetInt32())
                });
            }

            // Parse AQI
            int? aqi = null;
            var aqiResponse = await aqiTask;
            if (aqiResponse.IsSuccessStatusCode)
            {
                var aqiContent = await aqiResponse.Content.ReadAsStringAsync();
                var aqiJson = JsonDocument.Parse(aqiContent);
                if (aqiJson.RootElement.TryGetProperty("current", out var aqiCurrent) &&
                    aqiCurrent.TryGetProperty("us_aqi", out var aqiValue))
                {
                    aqi = aqiValue.GetInt32();
                }
            }

            var weatherData = new WeatherData
            {
                CityName = cityName,
                State = matchedState,
                Country = matchedCountry,
                Temperature = current.GetProperty("temperature_2m").GetDouble(),
                FeelsLike = current.GetProperty("apparent_temperature").GetDouble(),
                TempHigh = maxTemps[0].GetDouble(),
                TempLow = minTemps[0].GetDouble(),
                Description = GetWeatherDescription(weatherCode),
                Humidity = current.GetProperty("relative_humidity_2m").GetInt32(),
                WindSpeed = current.GetProperty("wind_speed_10m").GetDouble(),
                LocalTime = localTime,
                Sunrise = sunrises[0].GetString(),
                Sunset = sunsets[0].GetString(),
                AirQuality = aqi,
                AirQualityLabel = aqi.HasValue ? GetAqiLabel(aqi.Value) : null,
                Forecast = forecast
            };

            return Ok(weatherData);
        }

        private static readonly Dictionary<string, string> UsStateAbbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas",
            ["CA"] = "California", ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware",
            ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho",
            ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa", ["KS"] = "Kansas",
            ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland",
            ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi",
            ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada",
            ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York",
            ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio", ["OK"] = "Oklahoma",
            ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island", ["SC"] = "South Carolina",
            ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
            ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia",
            ["WI"] = "Wisconsin", ["WY"] = "Wyoming", ["DC"] = "District of Columbia"
        };

        private static string? ExpandStateAbbreviation(string abbreviation)
        {
            return UsStateAbbreviations.TryGetValue(abbreviation, out var fullName) ? fullName : null;
        }

        private static string GetAqiLabel(int aqi)
        {
            return aqi switch
            {
                <= 50 => "Good",
                <= 100 => "Moderate",
                <= 150 => "Unhealthy for Sensitive",
                <= 200 => "Unhealthy",
                <= 300 => "Very Unhealthy",
                _ => "Hazardous"
            };
        }

        private static string GetWeatherDescription(int weatherCode)
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
