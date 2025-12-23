using System.Text.Json;
using System.Text.Json.Nodes;
using skidoosh;

StateMachine state = StateMachine.Init;

// How often to update from API
// Should be at least 60 seconds, or we may get ratelimited.
const int period = 60000;
// If we fail to update from API this many times in a row,
// we will go back to the loading animation. We don't want to
// show stale data for too long.
const int freshness = 5;
// Every time we fail to update from the API, we will back off a little
// bit more each time, maybe we're hitting a rate limit?
const int backoff = 5000;

// The number of times we've failed to update in a row.
int errors = 0;

while(true) {
    switch(state) {
        case StateMachine.Init:
            Hardware.Init();
            await Hardware.SetLoading(true);

            Console.WriteLine("Hardware initialized.");

            // We want to turn off all LEDs when the program is being terminated,
            // otherwise the user may still think
            Console.CancelKeyPress += (_, _) => { Hardware.Shutdown(); };

            AppDomain.CurrentDomain.ProcessExit += (_, _) => { Hardware.Shutdown(); };

            state = StateMachine.Update;
            break;
        case StateMachine.Update:
            try {
                Dictionary<string, string> statuses = await PullLiftStatuses();

                Color[] colors = [
                    // Right side, bottom to top
                    statusToColor("Zendo Chair"), // Bottom
                    statusToColor("Kensho SuperChair"),
                    statusToColor("Freedom SuperChair"),
                    statusToColor("BreckConnect Gondola"),
                    statusToColor("Quicksilver SuperChair"),
                    statusToColor("Mercury SuperChair"),
                    statusToColor("Beaver Run SuperChair"),
                    statusToColor("E-Chair"), // Top

                    // Left side, top to bottom
                    statusToColor("Imperial SuperChair"), // Top
                    statusToColor("Horseshoe Bowl T-Bar"),
                    statusToColor("6-Chair"),
                    statusToColor("Rocky Mountain SuperChair"),
                    statusToColor("Colorado SuperChair"),
                    statusToColor("Peak 8 SuperConnect"),
                    statusToColor("Independence SuperChair"),
                    statusToColor("Falcon SuperChair"), // Bottom
                ];

                Color statusToColor(string key) {
                    string? value = statuses.GetValueOrDefault(key);

                    return value switch {
                        "closed" => Color.Red,
                        "open" => Color.Green,
                        "scheduled" => Color.Blue,
                        "hold" => Color.Orange,
                        _ => Color.Off
                    };
                }

                await Hardware.SetLoading(false);

                Hardware.SetLEDs(colors);

                try {
                    var report = await PullWeatherReport();

                    Hardware.SetLCDS(report.Snow24Hr, report.SnowTomorrow, report.CurrentTemp);
                } catch(Exception) {
                    // TODO, not super reliable right now, so just do it like this
                    Hardware.SetLCDS();
                }
                state = StateMachine.Idle;
            } catch(Exception e) {
                Console.Error.WriteLine(e);
                state = StateMachine.Error;
            }

            break;
        case StateMachine.Idle:
            errors = Math.Max(errors - 1, 0);

            await Task.Delay(Math.Clamp(period + errors * backoff, period, period * 5));
            state = StateMachine.Update;
            break;
        case StateMachine.Error:
            errors++;

            if(errors >= freshness) {
                Hardware.SetLCDS();
                Hardware.SetLEDs([]);
                await Hardware.SetLoading(true);
            }

            await Task.Delay(Math.Clamp(period + errors * backoff, period, period * 5));
            state = StateMachine.Update;

            break;
        default:
            throw new Exception($"Unknown state: {state}");
    }
}

// Pulls the lift statuses, throws an exception if they fail
static async Task<Dictionary<string, string>> PullLiftStatuses() {
    using HttpClient client = new HttpClient();
    var response = await client.GetAsync("https://liftie.info/api/resort/breck");

    if(!response.IsSuccessStatusCode) {
        string body;
        try {
            body = await response.Content.ReadAsStringAsync();
        } catch {
            body = "";
        }

        throw new Exception($"Liftie API: {response.StatusCode}: {body}");
    }

    var result = JsonSerializer.Deserialize<JsonObject>(await response.Content.ReadAsStringAsync());

    var status = result?["lifts"]?["status"];

    var res = status?.Deserialize<Dictionary<string, string>>();

    return res ?? throw new Exception("Deserializing 'lifts.status' = null. Did the API format change?");
}

// Pulls the weather status from breck website, throws an exception if it fails
static async Task<WeatherReport> PullWeatherReport() {
    using HttpClient client = new HttpClient();
    client.DefaultRequestHeaders.Add("User-Agent", "curl/8.16.0");
    client.DefaultRequestHeaders.Add("Accept", "*/*");
    var response = await client.GetAsync("https://www.breckenridge.com/api/PageApi/GetWeatherDataForHeader");

    if(!response.IsSuccessStatusCode) {
        string body;
        try {
            body = await response.Content.ReadAsStringAsync();
        } catch {
            body = "";
        }

        throw new Exception($"Breck API: {response.StatusCode}: {body}");
    }

    var result = JsonSerializer.Deserialize<JsonObject>(await response.Content.ReadAsStringAsync());

    double? currentTemp = null;
    double? snow24Hr = null;

    // Try parse current temperature
    try {
        currentTemp = result?["CurrentTempStandard"].Deserialize<double?>();
    } catch(Exception e) {
        Console.Error.WriteLine("Failed to parse current temp: " + e);
    }

    // Try parse current snow fall
    try {
        string? inchesStr = result?["SnowReportSections"].Deserialize<SnowReportSection[]>()
            ?.FirstOrDefault(v => v.Description == "24 Hour<br/>Snowfall")
            ?.Depth.Inches;

        if(double.TryParse(inchesStr, out double parsed)) {
            snow24Hr = parsed;
        }
    } catch(Exception e) {
        Console.Error.WriteLine("Failed to parse inches: " + e);
    }

    return new WeatherReport {
        CurrentTemp = currentTemp,
        Snow24Hr = snow24Hr,
        SnowTomorrow = null
    };
}

internal enum StateMachine {
    // Startup state, initialize hardware
    Init,

    // Pull data from API, update as much as possible.
    // - If successful, go to IDLE
    // - If any exceptions, go to IDLE_LONG
    // Set error indicator to 0
    Update,

    // Decrement the error indicator
    // Do nothing for timeout period
    Idle,

    // Increment the error indicator
    // If the error indicator reaches 5, then consider the data
    // stale and wipe the screen
    // Do nothing for timeout period
    Error
};

internal struct WeatherReport {
    public required double? CurrentTemp;
    public required double? Snow24Hr;
    public required double? SnowTomorrow;
}

internal class SnowReportSection {
    public required SnowReportDepth Depth { get; set; }
    public required string Description { get; set; }
}

internal class SnowReportDepth {
    public required string Inches { get; set; }
}