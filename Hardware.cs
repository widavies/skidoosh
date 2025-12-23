using System.Runtime.InteropServices;

namespace skidoosh;

/// <summary>
/// Calls C FFI for controlling the PI functions
/// </summary>
public static partial class Hardware {
    private const int NUM_LEDS = 16;

    private static int _cleanupOnce;

    public const float Brightness = 0.1f;

    private static CancellationTokenSource? _cts;

    /// <summary>
    /// Call immediately when the program starts.
    ///
    /// This will restore the LEDs/LCDs to a good state. In particular,
    /// if they were left in a stale state, we need to clear them or show
    /// the loading animation to indicate that the C# program is now
    /// working again.
    /// </summary>
    public static void Init() {
        init();
        SetLEDs([]);
    }

    /// <summary>
    /// Draw random sparkles - useful for a loading animation
    /// </summary>
    public static async Task SetLoading(bool on) {
        if(_cts != null) {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        SetLEDs([]);

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
                        UpdateLedsWithBrightness(encoded, encoded.Length);
                    }

                    await Task.Delay(400, _cts.Token);
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
    public static void SetLEDs(Color[] colors) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(colors.Length, 16);

        int[] encoded = new int[NUM_LEDS];

        for(int i = 0; i < colors.Length; i++) {
            encoded[i] = colors[i].g << 16 | colors[i].r << 8 | colors[i].b;
        }

        UpdateLedsWithBrightness(encoded, encoded.Length);
    }

    public static void SetLCDS() {
        updateForecast("1", "1", "1");
        updateSnow("2", "2", "2");
        updateTemperature("2", "2", (byte) 'c');
    }

    /// <summary>
    /// Call this to reset all the LCDs/LEDs to blank. It's a good
    /// idea to do this so that the user knows that the C# program has
    /// terminated and is no longer updating the LEDs instead of leaving
    /// them in a stale state.
    /// </summary>
    public static void Shutdown() {
        if(Interlocked.Exchange(ref _cleanupOnce, 1) != 0)
            return;

        Console.WriteLine("SHUTDOWN");
        _cts?.Dispose();
        _cts = null;
        SetLEDs([]);
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

    private static void UpdateLedsWithBrightness(int[] data, int length) {
        float b = Math.Clamp(Brightness, 0f, 1f);
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

public struct Color(byte r, byte g, byte b) {
    public readonly byte r = r;
    public readonly byte g = g;
    public readonly byte b = b;

    public static readonly Color Red = new(0xFF, 00, 00);
    public static readonly Color Blue = new(0x00, 00, 0xFF);
    public static readonly Color Orange = new(255, 165, 00);
    public static readonly Color Green = new(00, 0xFF, 00);
    public static readonly Color Off = new(00, 0, 00);
}