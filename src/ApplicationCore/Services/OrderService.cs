using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Newtonsoft.Json;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IAppLogger<OrderService> _logger;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IAppLogger<OrderService> logger,
        IUriComposer uriComposer)
    {
        _logger = logger;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.GetBySpecAsync(basketSpec);

        Guard.Against.NullBasket(basketId, basket);
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);
        await CreateReservation(order);
        await TriggerAzureFuncReservation(order);
    }

    const string QueueName = "reservationmessages";

    public async Task CreateReservation(Order order)
    {
        var serviceBusConnectionString = _uriComposer.GetServiceBusConnectionString();

        await using var client = new ServiceBusClient(serviceBusConnectionString);
        await using ServiceBusSender sender = client.CreateSender(QueueName);

        try
        {
            ReservationModel reservationModel = new ReservationModel
            {
                OrderId = order.Id,
                Items = order.OrderItems.Select(e => new ReservationItemModel
                {
                    ItemId = e.Id,
                    CatalogItemId = e.ItemOrdered.CatalogItemId,
                    ProductName = e.ItemOrdered.ProductName,
                    Units = e.Units
                }).ToList(),
            };
            
            var reservationJson = JsonConvert.SerializeObject(reservationModel);

            _logger.LogInformation($"Sending message: {reservationJson}");
            var message = new ServiceBusMessage(reservationJson);
            await sender.SendMessageAsync(message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"{DateTime.Now} :: Exception: {exception.Message}");
        }
        finally
        {
            // Calling DisposeAsync on client types is required to ensure that network
            // resources and other unmanaged objects are properly cleaned up.
            await sender.DisposeAsync();
            await client.DisposeAsync();
        }
    }

    public async Task TriggerAzureFuncReservation(Order order)
    {
        var azureFuncUrl = _uriComposer.GetAzureFuncUrl();

        try
        {
            ReservationModel reservationModel = new ReservationModel
            {
                OrderId = order.Id,
                Items = order.OrderItems.Select(e => new ReservationItemModel
                {
                    ItemId = e.Id,
                    CatalogItemId = e.ItemOrdered.CatalogItemId,
                    ProductName = e.ItemOrdered.ProductName,
                    Units = e.Units
                }).ToList(),
            };

            var reservationJson = JsonConvert.SerializeObject(reservationModel);
            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(
                    azureFuncUrl,
                    new StringContent(reservationJson, Encoding.UTF8, "application/json"));
            }

            _logger.LogInformation($"Sending JSON message: {reservationJson}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"{DateTime.Now} :: Exception: {exception.Message}");
        }
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
