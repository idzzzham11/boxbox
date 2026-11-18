using System.Net.Http.Json;
using BoxBox.Core.Analytics;
using BoxBox.Core.Contracts;

namespace BoxBox.Web.Services;

/// <summary>
/// The one place that knows the API's URLs. Components call these typed methods instead of building
/// HTTP requests. The return types come straight from BoxBox.Core, so the compiler checks that this client
/// and the server agree on the JSON shapes.
/// </summary>
public sealed class BoxBoxApi(HttpClient http)
{
    // GetFromJsonAsync<T> sends GET, reads the JSON body and deserialises it into T (camelCase handled).
    public Task<SpendSummaryResult?> GetSummaryAsync() => http.GetFromJsonAsync<SpendSummaryResult>("api/summary");
    public Task<List<EntryDto>?> GetEntriesAsync() => http.GetFromJsonAsync<List<EntryDto>>("api/entries");
    public Task<ForecastResult?> GetForecastAsync() => http.GetFromJsonAsync<ForecastResult>("api/forecast");

    /// <summary>Returns null on success, otherwise the error messages to show the user.</summary>
    public async Task<IReadOnlyList<string>?> AddEntryAsync(NewEntry entry)
    {
        var response = await http.PostAsJsonAsync("api/entries", entry);
        return response.IsSuccessStatusCode ? null : await ReadErrorsAsync(response, "Could not save.");
    }

    public async Task DeleteEntryAsync(Guid id) => (await http.DeleteAsync($"api/entries/{id}")).EnsureSuccessStatusCode();

    /// <summary>Imports CSV text. Returns (count, null) on success or (0, errors) when the file was rejected.</summary>
    public async Task<(int Imported, IReadOnlyList<string>? Errors)> ImportAsync(string csv)
    {
        var response = await http.PostAsync("api/import", new StringContent(csv, System.Text.Encoding.UTF8, "text/plain"));
        if (!response.IsSuccessStatusCode) return (0, await ReadErrorsAsync(response, "Import failed."));
        var ok = await response.Content.ReadFromJsonAsync<ImportResult>();
        return (ok?.Imported ?? 0, null);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
            if (body?.Errors is { Count: > 0 }) return body.Errors;
        }
        catch (Exception) { /* body was not our JSON error shape */ }
        return [fallback];
    }

    private sealed record ErrorBody(List<string>? Errors);
    private sealed record ImportResult(int Imported);
}
