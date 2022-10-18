using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

namespace CloudLab2_WeatherLambda;

public static class WeatherLambda
{
    private class WeatherTableEntity : TableEntity
    {
        public string ResponseData { get; set; }
        public DateTime RequestDate { get; set; }
    }
    
    
    private static string DbConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=cosmo-db-for-weatherforecast;AccountKey=b1RJ4paEuSWYN3ILJQzM3OghvPjFr9K874TXgfYcJWtu2oJcs1is1CIlAZ8jcdoz2Qo2b52PLjgAuxrehqQwwA==;TableEndpoint=https://cosmo-db-for-weatherforecast.table.cosmos.azure.com:443/;";

    private static string AccuWeatherAPIKey = "ur2zbfkAsn8luOuKbXlmbnGfAUa7NfIk";

    [FunctionName("WeatherLambda")]
    public static async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)]
        HttpRequest req, ILogger log)
    {
        string searchCity = req.Query["city"];

        if (searchCity is null)
        {
            return new BadRequestObjectResult("Please pass a city name on the query string");
        }

        var httpClient = new HttpClient();
        try
        {
            var locationResponse = await httpClient.GetAsync(
                $"http://dataservice.accuweather.com/locations/v1/cities/search?apikey={AccuWeatherAPIKey}&q={searchCity}");
            if (!locationResponse.IsSuccessStatusCode)
            {
                return new BadRequestObjectResult("Failed to get weather forecast for given city");
            }

            var responseString = await locationResponse.Content.ReadAsStringAsync();
            var dynamicLocationResponse = JsonConvert.DeserializeObject<dynamic>(responseString);

            var locationKey = dynamicLocationResponse[0].Key;

            var weatherResponse = await httpClient.GetAsync(
                $"http://dataservice.accuweather.com/forecasts/v1/daily/5day/{locationKey}?apikey={AccuWeatherAPIKey}");
            if (!weatherResponse.IsSuccessStatusCode)
            {
                return new BadRequestObjectResult("Failed to get weather forecast for given city");
            }

            var result = await weatherResponse.Content.ReadAsStringAsync();

            var storageAccount = CloudStorageAccount.Parse(DbConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();

            var tableEntity = new WeatherTableEntity
            {
                PartitionKey = searchCity.ToUpper().Trim(),
                RowKey = Guid.NewGuid().ToString(),
                ResponseData = result,
                RequestDate = DateTime.Now
            };

            var table = tableClient.GetTableReference("WeatherRadek");
            await table.ExecuteAsync(TableOperation.InsertOrMerge(tableEntity));

            return new OkObjectResult(result);
        }
        catch (HttpRequestException e)
        {
            return new BadRequestObjectResult("Failed to get weather forecast for given city");
        }
        catch (Exception)
        {
            return new InternalServerErrorResult();
        }
    }
}