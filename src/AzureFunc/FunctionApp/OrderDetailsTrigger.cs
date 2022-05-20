using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace FunctionApp;

public static class OrderDetailsTrigger
{
    [FunctionName("OrderProcessingFunc")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]
        HttpRequest req,
        [CosmosDB(
            "ToDoList",
            "Items",
            ConnectionStringSetting = "CosmosdbConnectionString")]
        out dynamic document,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function processed a request.");

        var requestBody = new StreamReader(req.Body).ReadToEnd();
        var data = JsonConvert.DeserializeObject<ReservationModel>(requestBody);
        var orderId = data?.OrderId;

        document = data;

        var responseMessage = !orderId.HasValue
            ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
            : $"Hello, {orderId}. This HTTP triggered function executed successfully.";

        return new OkObjectResult(responseMessage);
    }

    private class ReservationModel
    {
        public int OrderId { get; set; }
        public IList<ReservationItemModel> Items { get; set; }
    }

    private class ReservationItemModel
    {
        public int ItemId { get; set; }
        public int CatalogItemId { get; set; }
        public int Units { get; set; }
        public string ProductName { get; set; }
    }
}
