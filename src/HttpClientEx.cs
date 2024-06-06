using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AzureMigrateDataExtractor;

internal static class HttpClientEx
{
    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync())!;
    }

    public static async Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object obj)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response;
    }
}