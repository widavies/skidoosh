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
int errors = int.MaxValue;

while(true) {
    switch(state) {
        case StateMachine.Init:
            Hardware.Init();
            await Hardware.SetLoading(true);
            Hardware.SetLCDS();

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
static async Task<Dictionary<string, dynamic>> PullWeatherReport() {
    HttpClient client = new HttpClient();
    var response = await client.GetAsync("https://www.breckenridge.com/api/PageApi/GetWeatherDataForHeader");

    if(response.IsSuccessStatusCode) {
        var result = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(await response.Content.ReadAsStringAsync());

        return result;
    }

    throw new Exception(await response.Content.ReadAsStringAsync());
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
