using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexaCommerce.Notifications.Handlers;
using NexaCommerce.Notifications.Services;
using NexaCommerce.ProductCatalog.Messaging;
using Shouldly;
using Xunit;

namespace NexaCommerce.Notifications.Tests.Handlers;

public sealed class ProductCreatedHandlerTests
{
    private readonly Mock<INotificationSender> _senderMock = new();
    private readonly ProductCreatedHandler _sut;

    private static readonly ProductCreatedEvent SampleEvent = new(
        ProductId:    Guid.Parse("20000000-0000-0000-0000-000000000001"),
        ProductName:  "Wireless Keyboard",
        Price:        79.99m,
        CategoryName: "Electronics",
        CreatedAt:    DateTimeOffset.UtcNow);

    public ProductCreatedHandlerTests()
    {
        _sut = new ProductCreatedHandler(_senderMock.Object, NullLogger<ProductCreatedHandler>.Instance);
    }

    [Fact]
    public async Task Handle_should_call_sender_with_correct_product_details()
    {
        await _sut.Handle(SampleEvent, CancellationToken.None);

        _senderMock.Verify(s => s.SendProductCreatedAsync(
            SampleEvent.ProductName,
            SampleEvent.Price,
            SampleEvent.CategoryName,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_should_complete_without_throwing()
    {
        var act = async () => await _sut.Handle(SampleEvent, CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }
}

public sealed class ProductDeletedHandlerTests
{
    private readonly Mock<INotificationSender> _senderMock = new();
    private readonly ProductDeletedHandler _sut;

    private static readonly ProductDeletedEvent SampleEvent = new(
        ProductId:   Guid.Parse("20000000-0000-0000-0000-000000000001"),
        ProductName: "Wireless Keyboard",
        DeletedAt:   DateTimeOffset.UtcNow);

    public ProductDeletedHandlerTests()
    {
        _sut = new ProductDeletedHandler(_senderMock.Object, NullLogger<ProductDeletedHandler>.Instance);
    }

    [Fact]
    public async Task Handle_should_call_sender_with_correct_product_name()
    {
        await _sut.Handle(SampleEvent, CancellationToken.None);

        _senderMock.Verify(s => s.SendProductDeletedAsync(
            SampleEvent.ProductName,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_should_complete_without_throwing()
    {
        var act = async () => await _sut.Handle(SampleEvent, CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }
}

public sealed class NotificationSenderTests
{
    private readonly NotificationSender _sut = new(NullLogger<NotificationSender>.Instance);

    [Fact]
    public async Task SendProductCreatedAsync_should_complete_without_throwing()
    {
        var act = async () => await _sut.SendProductCreatedAsync("Widget", 9.99m, "Gadgets");

        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task SendProductDeletedAsync_should_complete_without_throwing()
    {
        var act = async () => await _sut.SendProductDeletedAsync("Widget");

        await act.ShouldNotThrowAsync();
    }
}
