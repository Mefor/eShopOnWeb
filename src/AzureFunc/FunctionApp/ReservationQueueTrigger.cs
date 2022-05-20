using System;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FunctionApp
{
    public static class ReservationQueueTrigger
    {
        [FunctionName("ReservationQueueTrigger")]
        public static void Run(
            [ServiceBusTrigger("reservationmessages", Connection = "ServiceBusConnectionString")]
            string myQueueItem,
            Int32 deliveryCount,
            DateTime enqueuedTimeUtc,
            string messageId,
            [Blob("reservationscontainer/reservation-{rand-guid}.json", FileAccess.Write)] Stream outputBlob,
            ExecutionContext context,
            ILogger log)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            var forceThrowException = !string.IsNullOrWhiteSpace(config["ForceFail"]);
            if (forceThrowException)
            {
                throw new ApplicationException("Manually throw exception on Message Queue handling...");
            }

            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
            log.LogInformation($"EnqueuedTimeUtc={enqueuedTimeUtc}");
            log.LogInformation($"DeliveryCount={deliveryCount}");
            log.LogInformation($"MessageId={messageId}");

            outputBlob.Write(Encoding.UTF8.GetBytes(myQueueItem));
        }
    }
}
