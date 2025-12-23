using System.Text.Json;
using System.Text.Json.Nodes;

namespace skidoosh;

public static class LiftReport {
    public static async Task<Dictionary<string, string>?> PullLiftStatuses() {
        try {
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
        } catch(Exception e) {
            await Console.Error.WriteLineAsync($"Error: {e}");
            return null;
        }
    }
}