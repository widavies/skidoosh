using System.Text.Json;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace skidoosh;

public partial class WeatherReport {
    //
    // Screen 1 - Forecasted snowfall
    //

    // Forecasted overnight snow, inches
    public string? SnowTonight;

    // Forecast snow during daytime tomorrow, inches
    public string? SnowTomorrow;

    //
    // Screen 2 - Past snowfall
    //

    // Cumulative snow last night, inches
    public string? SnowOvernight;

    // Cumulative snow in the last 24-hour window, inches
    public string? Snow24Hr;

    // Cumulative snow in the last 48-hour window, inches
    public string? Snow48Hr;

    // Cumulative snow in the last 7-day window, inches
    public string? Snow7Days;

    //
    // Screen 3 - Current temperature
    // Useful: https://www.pwsweather.com/station/map/pws/mountainside?ob=temps&lat=39.47777744586028&lon=-106.08299732208252&zoom=14
    //

    // Temperature near Tiger Run
    // https://www.weather.gov/wrh/timeseries?site=E8345&hours=72
    public double? BaseTemperature;

    // Temperature from horseshoe bowl weather station, about half-way up horseshoe
    // https://www.weather.gov/wrh/timeseries?site=CAHSB&hours=72
    public double? MidTemperature;

    // Temperature from peak 6 summit station
    // https://www.weather.gov/wrh/timeseries?site=CABP6&hours=72
    public double? SummitTemperature;

    //
    // Pull the latest weather report. Pulls as much information as it can,
    // leaving fields null otherwise. Returns null if nothing could be retrieved
    // due to a network failure or if the returned weather report would have all null fields.
    //
    public static async Task<WeatherReport?> GetLatestWeatherReport() {
        try {
            WeatherReport report = new();

            using HttpClient client = new HttpClient();

            var opts = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            client.DefaultRequestHeaders.Add("User-Agent", "curl/8.16.0");
            client.DefaultRequestHeaders.Add("Accept", "*/*");

            //
            // Place network requests concurrently
            //

            Task<HttpResponseMessage>[] fetchTasks = [
                client.GetAsync("https://www.breckenridge.com/the-mountain/mountain-conditions/snow-and-weather-report.aspx"),
                client.GetAsync("https://api.weather.gov/stations/E8345/observations"),
                client.GetAsync("https://api.weather.gov/stations/CAHSB/observations"),
                client.GetAsync("https://api.weather.gov/stations/CABP6/observations")
            ];

            try {
                await Task.WhenAll(fetchTasks);
            } catch(Exception) {
                if(fetchTasks.All(t => t.IsFaulted)) {
                    return null;
                }
            }

            //
            // Process responses
            //

            if(fetchTasks is not [var taskSnowReport, var taskWeatherBase, var taskWeatherMid, var taskWeatherSummit]) {
                await Console.Error.WriteLineAsync("Developer error: expect fetchTasks to be array of len 4");
                return null;
            }

            //
            // Process snow report
            //
            try {
                if(!taskSnowReport.IsCompletedSuccessfully) throw new Exception("Snow report: Fetch failed");

                if(!taskSnowReport.Result.IsSuccessStatusCode) {
                    string body;

                    try {
                        body = await taskSnowReport.Result.Content.ReadAsStringAsync();
                    } catch {
                        body = "";
                    }

                    throw new Exception($"Snow report: HTTP response {taskSnowReport.Result.StatusCode}\nBody:\n{body}");
                }

                string html = await taskSnowReport.Result.Content.ReadAsStringAsync();

                var forecastMatch = ForecastRegex().Match(html);

                if(forecastMatch.Success) {
                    try {
                        Forecast[]? forecasts = JsonSerializer.Deserialize<Forecast[]>(forecastMatch.Groups[1].Value);

                        if(forecasts != null) {
                            report.SnowTonight = forecasts.FirstOrDefault()?.ForecastData?.FirstOrDefault()?.SnowFallNightStandard;
                            report.SnowTomorrow = forecasts.FirstOrDefault()?.ForecastData?.Skip(1).FirstOrDefault()?.SnowFallDayStandard;
                        }
                    } catch(Exception e) {
                        await Console.Error.WriteLineAsync("Snow report: Failed to process forecast: " + e);
                    }
                }

                var match = SnowReportRegex().Match(html);

                if(match.Success) {
                    try {
                        SnowReport? result = JsonSerializer.Deserialize<SnowReport>(match.Groups[1].Value);

                        if(result != null) {
                            report.SnowOvernight = result.OvernightSnowfall?.Inches;
                            report.Snow24Hr = result.TwentyFourHourSnowfall?.Inches;
                            report.Snow48Hr = result.FortyEightHourSnowfall?.Inches;
                            report.Snow7Days = result.SevenDaySnowfall?.Inches;
                        }
                    } catch(Exception e) {
                        await Console.Error.WriteLineAsync("Snow report: Failed to process historical: " + e);
                    }
                }
            } catch(Exception e) {
                await Console.Error.WriteLineAsync("Snow report: Error - " + e);
            }

            //
            // Process base weather report
            //

            try {
                if(!taskWeatherBase.IsCompletedSuccessfully) throw new Exception("Base weather report: Fetch failed");

                if(!taskWeatherBase.Result.IsSuccessStatusCode) {
                    string body;

                    try {
                        body = await taskWeatherBase.Result.Content.ReadAsStringAsync();
                    } catch {
                        body = "";
                    }

                    throw new Exception($"Base weather report: HTTP response {taskWeatherBase.Result.StatusCode}\nBody:\n{body}");
                }

                string json = await taskWeatherBase.Result.Content.ReadAsStringAsync();
                var weatherReport = JsonSerializer.Deserialize<WeatherStation>(json, opts);

                report.BaseTemperature = weatherReport?.Features.FirstOrDefault(x => x.Properties?.Temperature != null)?.Properties?.Temperature?.ToDegF();
            } catch(Exception e) {
                await Console.Error.WriteLineAsync("Base weather report: Error - " + e);
            }

            //
            // Process mid-station weather report
            //

            try {
                if(!taskWeatherMid.IsCompletedSuccessfully) throw new Exception("Mid weather report: Fetch failed");

                if(!taskWeatherMid.Result.IsSuccessStatusCode) {
                    string body;

                    try {
                        body = await taskWeatherMid.Result.Content.ReadAsStringAsync();
                    } catch {
                        body = "";
                    }

                    throw new Exception($"Mid weather report: HTTP response {taskWeatherMid.Result.StatusCode}\nBody:\n{body}");
                }

                string json = await taskWeatherMid.Result.Content.ReadAsStringAsync();
                var weatherReport = JsonSerializer.Deserialize<WeatherStation>(json, opts);

                report.MidTemperature = weatherReport?.Features.FirstOrDefault(x => x.Properties?.Temperature != null)?.Properties?.Temperature?.ToDegF();
            } catch(Exception e) {
                await Console.Error.WriteLineAsync("Mid weather report: Error - " + e);
            }


            //
            // Process summit weather report
            //

            try {
                if(!taskWeatherSummit.IsCompletedSuccessfully) throw new Exception("Summit weather report: Fetch failed");

                if(!taskWeatherSummit.Result.IsSuccessStatusCode) {
                    string body;

                    try {
                        body = await taskWeatherSummit.Result.Content.ReadAsStringAsync();
                    } catch {
                        body = "";
                    }

                    throw new Exception($"Summit weather report: HTTP response {taskWeatherSummit.Result.StatusCode}\nBody:\n{body}");
                }

                string json = await taskWeatherSummit.Result.Content.ReadAsStringAsync();
                var weatherReport = JsonSerializer.Deserialize<WeatherStation>(json, opts);

                report.SummitTemperature = weatherReport?.Features.FirstOrDefault(x => x.Properties?.Temperature != null)?.Properties?.Temperature?.ToDegF();
            } catch(Exception e) {
                await Console.Error.WriteLineAsync("Summit weather report: Error - " + e);
            }

            List<bool> test = [
                report.SnowTonight != null,
                report.SnowTomorrow != null,
                report.SnowOvernight != null,
                report.Snow24Hr != null,
                report.Snow48Hr != null,
                report.Snow7Days != null,
                report.BaseTemperature.HasValue,
                report.MidTemperature.HasValue,
                report.SummitTemperature.HasValue
            ];

            return test.Any(t => t) ? report : null;
        } catch(Exception e) {
            await Console.Error.WriteLineAsync("Error - " + e);
            return null;
        }
    }

    [GeneratedRegex(@"(?<=FR.snowReportData\s+=\s+)(.*);")]
    private static partial Regex SnowReportRegex();

    [GeneratedRegex(@"(?<=FR.forecasts\s+=\s+)(.*);")]
    private static partial Regex ForecastRegex();

    public override string ToString() {
        return
            $"{nameof(SnowTonight)}: {SnowTonight}, {nameof(SnowTomorrow)}: {SnowTomorrow}, {nameof(SnowOvernight)}: {SnowOvernight}, {nameof(Snow24Hr)}: {Snow24Hr}, {nameof(Snow48Hr)}: {Snow48Hr}, {nameof(Snow7Days)}: {Snow7Days}, {nameof(BaseTemperature)}: {BaseTemperature}, {nameof(MidTemperature)}: {MidTemperature}, {nameof(SummitTemperature)}: {SummitTemperature}";
    }
}

// Minimum helper classes for JSON deserialization

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class Forecast {
    public ForecastData[]? ForecastData { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class ForecastData {
    public string? SnowFallDayStandard { get; init; }
    public string? SnowFallNightStandard { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class SnowReport {
    public SnowReportDepth? OvernightSnowfall { get; init; }
    public SnowReportDepth? TwentyFourHourSnowfall { get; init; }
    public SnowReportDepth? FortyEightHourSnowfall { get; init; }
    public SnowReportDepth? SevenDaySnowfall { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class SnowReportDepth {
    public string? Inches { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class WeatherStation {
    public WeatherStationFeature[] Features { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class WeatherStationFeature {
    public WeatherStationProperty? Properties { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class WeatherStationProperty {
    public WeatherStationTemperature? Temperature { get; init; }
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
internal class WeatherStationTemperature {
    public string? UnitCode { get; init; }
    public double? Value { get; init; }

    public double? ToDegF() {
        if(Value == null) return null;

        if(UnitCode != null && UnitCode.EndsWith("degF")) return Value;
        // Assume it is Celsius
        return Value * 9 / 5 + 32;
    }
}