using System.Runtime.InteropServices;

namespace skidoosh;

/// <summary>
/// Calls C FFI for controlling the PI functions
/// </summary>
public partial class Hardware(float brightness = 0.1f) {
    private const int NUM_LEDS = 16;

    private int _cleanupOnce;

    private CancellationTokenSource? _cts;

    /// <summary>
    /// Call immediately when the program starts.
    ///
    /// This will restore the LEDs/LCDs to a good state. In particular,
    /// if they were left in a stale state, we need to clear them or show
    /// the loading animation to indicate that the C# program is now
    /// working again.
    /// </summary>
    public virtual async Task Init() {
        init();
        SetLEDs([]);
        ClearLCDs();
        await SetLEDsLoading(true);

        Console.WriteLine("Hardware initialized.");
    }

    /// <summary>
    /// Draw random sparkles - useful for a loading animation
    /// </summary>
    public virtual async Task SetLEDsLoading(bool on) {
        if(_cts != null) {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        // Start a background job that continuously updates
        // the LEDs
        if(on) {
            _cts = new CancellationTokenSource();
            _ = Task.Run(async () => {
                Random r = new();

                bool[] flakesLeft = new bool[NUM_LEDS / 2];
                bool[] flakesRight = new bool[NUM_LEDS / 2];

                while(!_cts.IsCancellationRequested) {
                    // Apply gravity
                    for(int i = flakesLeft.Length - 1; i > 0; i--) {
                        flakesLeft[i] = flakesLeft[i - 1];
                        flakesLeft[i - 1] = false;

                        flakesRight[i] = flakesRight[i - 1];
                        flakesRight[i - 1] = false;
                    }

                    // Spawn new flakes
                    flakesLeft[0] = r.NextDouble() < 0.5;
                    flakesRight[0] = r.NextDouble() < 0.5;

                    // Draw the flakes
                    int[] encoded = new int[NUM_LEDS];

                    for(int i = 0; i < flakesLeft.Length; i++) {
                        encoded[i + 8] = flakesLeft[i] ? int.MaxValue : 0;
                        encoded[7 - i] = flakesRight[i] ? int.MaxValue : 0;
                    }

                    if(!_cts.IsCancellationRequested) {
                        UpdateLEDsWithBrightness(encoded, encoded.Length);
                    }

                    await Task.Delay(400);
                }
            }, _cts.Token);
        }
    }

    /// <summary>
    /// Set the LEDs colors. The indices of the <paramref name="colors"/>
    /// correspond to the following LEDs on the board:
    ///  8 -------------- 7
    ///  9 -------------- 6
    /// 10 -------------- 5
    /// 11 -------------- 4
    /// 12 -------------- 3
    /// 13 -------------- 2
    /// 14 -------------- 1
    /// 15 -------------- 0
    /// </summary>
    /// <param name="colors">The desired color state for each LED. Pass the empty array to
    /// set all to OFF.</param>
    public virtual void SetLEDs(Color[] colors) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(colors.Length, 16);

        int[] encoded = new int[NUM_LEDS];

        for(int i = 0; i < colors.Length; i++) {
            encoded[i] = colors[i].g << 16 | colors[i].r << 8 | colors[i].b;
        }

        UpdateLEDsWithBrightness(encoded, encoded.Length);
    }

    /// <summary>
    /// Write to the LCDs
    /// </summary>
    public virtual void SetLCDs(string pastSnowLabel, string? pastSnowValue, string futureSnowLabel, string? futureSnowValue,
        string currentTempLabel, double? currentTempValue) {

        updateForecast(convertRange(pastSnowValue), pastSnowLabel, "in");
        updateSnow(convertRange(futureSnowValue), futureSnowLabel, "in");
        updateTemperature(convertTemp(currentTempValue), currentTempLabel, (byte) 'f');
    }

    // This will try to keep values
    private static string convertRange(string? input) {
        if(input == null) return "--";

        string[] tokens = input.Split('-');

        return tokens.Length switch {
            0 => "",
            1 => tokens.First(),
            _ => tokens.First() + "+"
        };
    }

    private static string convertTemp(double? input) {
        return input switch {
            null => "--",
            // If number is negative or 3 digits, only room for 3 characters
            < 0 => $"{input:#}",
            _ => Math.Round(input.Value) >= 100 ? $"{input:#}" : $"{input:0.#}"
        };
    }


    public virtual void ClearLCDs() {
        SetLCDs("Snow 24 hr", null, "Tonight", null, "Base", null);
    }

    /// <summary>
    /// Call this to reset all the LCDs/LEDs to blank. It's a good
    /// idea to do this so that the user knows that the C# program has
    /// terminated and is no longer updating the LEDs instead of leaving
    /// them in a stale state.
    /// </summary>
    public void Shutdown() {
        if(Interlocked.Exchange(ref _cleanupOnce, 1) != 0)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        SetLEDs([]);
        ClearLCDs();
    }

    //
    //
    // libskidoosh interface methods
    //
    //
    [LibraryImport("libskidoosh")]
    private static partial void init();

    [LibraryImport("libskidoosh")]
    private static partial int updateLeds([Out] int[] data, int length);

    [LibraryImport("libskidoosh", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int updateTemperature(string temp, string label, byte celsiusOrDegrees);

    [LibraryImport("libskidoosh", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int updateSnow(string snowTotal, string label, string unitOfMeasure);

    [LibraryImport("libskidoosh", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int updateForecast(string snowTotal, string label, string unitOfMeasure);

    private void UpdateLEDsWithBrightness(int[] data, int length) {
        float b = Math.Clamp(brightness, 0f, 1f);
        for(int i = 0; i < length; i++) {
            int g = (data[i] >> 16) & 0xFF;
            int r = (data[i] >> 8) & 0xFF;
            int bl = data[i] & 0xFF;
            g = (byte) (g * b);
            r = (byte) (r * b);
            bl = (byte) (bl * b);
            data[i] = g << 16 | r << 8 | bl;
        }

        updateLeds(data, length);
    }
}

public class FakeHardware : Hardware {
    public override Task Init() {
        Console.WriteLine("FAKE: Init()");
        return Task.CompletedTask;
    }

    public override Task SetLEDsLoading(bool on) {
        Console.WriteLine($"FAKE: SetLoading({on})");
        return Task.CompletedTask;
    }

    public override void SetLEDs(Color[] colors) {
        Console.WriteLine($"FAKE: SetLEDs({colors})");
    }

    public override void SetLCDs(string pastSnowLabel, string? pastSnowValue, string futureSnowLabel, string? futureSnowValue, string currentTempLabel,
        double? currentTempValue) {
        Console.WriteLine("---- Screen ---- ");
        Console.WriteLine($"{pastSnowLabel}: {pastSnowValue} in");
        Console.WriteLine($"{futureSnowLabel}: {futureSnowValue} in");
        Console.WriteLine($"{currentTempLabel}: {currentTempValue} F");
    }

    public override void ClearLCDs() {
        Console.WriteLine("---- Screen ---- ");
        Console.WriteLine("Snow 24 hr: -- in");
        Console.WriteLine("Tomorrow: -- in");
        Console.WriteLine("Today: -- F");
    }
}

public readonly struct Color(byte r, byte g, byte b) {
    public readonly byte r = r;
    public readonly byte g = g;
    public readonly byte b = b;
    public bool IsOff => r == 0 && g == 0 && b == 0;

    public static readonly Color Red = new(0xFF, 00, 00);
    public static readonly Color Blue = new(0x00, 00, 0xFF);
    public static readonly Color Orange = new(255, 165, 00);
    public static readonly Color Green = new(00, 0xFF, 00);
    public static readonly Color Off = new(00, 0, 00);
}