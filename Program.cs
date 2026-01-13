using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using skidoosh;

StateMachine state = StateMachine.UpdateWeather;

Hardware hardware = new Hardware();

await hardware.Init();

if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    // Register the SIGTERM handler on non-Windows platforms
    PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => {
        hardware.Shutdown();
        // Place your cleanup logic or event triggering here
        // Note: The handler should exit the process or signal the main thread to do so
        Environment.Exit(0);
    });
}

// We want to turn off all LEDs when the program is being terminated,
// otherwise the user may still think
Console.CancelKeyPress += (_, _) => { hardware.Shutdown(); };

AppDomain.CurrentDomain.ProcessExit += (_, _) => { hardware.Shutdown(); };

// Wait for a ping to complete, this will let it start up faster
// otherwise the network request may immediately fail, causing us to wait a full minute
// before trying again.
IPStatus pingStatus = IPStatus.Unknown;

// Only do this for a limited time
int pingAttempts = 0;

while(pingStatus != IPStatus.Success) {
    try {
        using Ping myPing = new Ping();
        const int timeout = 1000;
        PingReply reply = myPing.Send("google.com", timeout);
        pingStatus = reply.Status;
    } catch(Exception e) {
        Console.Error.WriteLine($"Error: {e}");
    }

    await Task.Delay(2000);

    pingAttempts++;

    if(pingAttempts > 30) {
        break;
    }
}

int consecutiveLiftReportErrors = 0;
int consecutiveWeatherReportErrors = 0;
int loops = 0;
WeatherReport? report = null;

// State machine
while(true) {
    Console.WriteLine(state);
    switch(state) {
        case StateMachine.UpdateLiftStatus:
            // If we have been erroring, see if delaying a bit longer will help our plight
            if(consecutiveLiftReportErrors > 0) {
                await Task.Delay(Math.Clamp(consecutiveLiftReportErrors * 5000, 0, 30000));
            }

            Dictionary<string, string>? liftReport = await LiftReport.PullLiftStatuses();

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

            Console.WriteLine("--- Lift report --- ");
            Console.WriteLine(string.Join(",", colors));
            
            // If any colors are set, update
            if(colors.Any(c => !c.IsOff)) {
                consecutiveLiftReportErrors = 0;
                await hardware.SetLEDsLoading(false);
                hardware.SetLEDs(colors);
            }
            // Otherwise, there's a failure
            else {
                consecutiveLiftReportErrors++;

                // The data is too stale now, hide it
                if(consecutiveLiftReportErrors > 5) {
                    await hardware.SetLEDsLoading(true);
                }
            }

            state = StateMachine.WriteWeatherScreen1;
            break;

            Color statusToColor(string key) {
                string? value = liftReport?.GetValueOrDefault(key);

                return value switch {
                    "closed" => Color.Red,
                    "open" => Color.Green,
                    "scheduled" => Color.Blue,
                    "hold" => Color.Orange,
                    _ => Color.Off
                };
            }
        case StateMachine.UpdateWeather:
            // If we have been erroring, see if delaying a bit longer will help our plight
            if(consecutiveWeatherReportErrors > 0) {
                await Task.Delay(Math.Clamp(consecutiveWeatherReportErrors * 5000, 0, 30000));
            }

            WeatherReport? latest = await WeatherReport.GetLatestWeatherReport();

            Console.WriteLine("--- Weather report ---");
            Console.WriteLine(latest);

            // Success
            if(latest != null) {
                consecutiveWeatherReportErrors = 0;
                report = latest;
            }
            // Failure
            else {
                if(consecutiveWeatherReportErrors >= 2) {
                    report = null;
                    hardware.ClearLCDs(); // Clear LCDs, data is too stale now
                }

                consecutiveWeatherReportErrors++;
            }

            state = StateMachine.UpdateLiftStatus;

            break;
        case StateMachine.WriteWeatherScreen1:
            if(report != null) {
                hardware.SetLCDs("Overnight", report.SnowOvernight, "Tonight", report.SnowTonight,
                    "Base", report.BaseTemperature);
            }

            await Task.Delay(12000);
            state = StateMachine.WriteWeatherScreen2;

            break;
        case StateMachine.WriteWeatherScreen2:
            if(report != null) {
                hardware.SetLCDs("Snow 24 hr", report.Snow24Hr, "Tomorrow", report.SnowTomorrow,
                    "Mid", report.MidTemperature);
            }

            await Task.Delay(12000);
            state = StateMachine.WriteWeatherScreen3;
            break;
        case StateMachine.WriteWeatherScreen3:
            if(report != null) {
                hardware.SetLCDs("Snow 48 hr", report.Snow48Hr, "Tonight", report.SnowTonight,
                    "Summit", report.SummitTemperature);
            }

            await Task.Delay(12000);
            state = StateMachine.WriteWeatherScreen4;
            break;
        case StateMachine.WriteWeatherScreen4:
            if(report != null) {
                hardware.SetLCDs("Snow 7 day", report.Snow7Days, "Tomorrow", report.SnowTomorrow,
                    "Mid", report.MidTemperature);
            }

            await Task.Delay(12000);

            // 1 loop takes 48 seconds
            loops++;

            // If we don't have weather data, we'll poll more often
            if((report == null && loops >= 4) || (report != null && loops >= 10)) {
                loops = 0;
                state = StateMachine.UpdateWeather;
            } 
            else if(loops % 2 == 0) {
                state = StateMachine.UpdateLiftStatus;
            } 
            else {
                state = StateMachine.WriteWeatherScreen1;
            }

            break;
        default:
            throw new Exception("Failed to read");
    }
}

internal enum StateMachine {
    UpdateLiftStatus,
    UpdateWeather,
    WriteWeatherScreen1,
    WriteWeatherScreen2,
    WriteWeatherScreen3,
    WriteWeatherScreen4,
}