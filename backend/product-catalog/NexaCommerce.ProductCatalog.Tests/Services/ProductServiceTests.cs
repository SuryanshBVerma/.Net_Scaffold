using Ardalis.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexaCommerce.ProductCatalog.Data;
using NexaCommerce.ProductCatalog.Data.Entities;
using NexaCommerce.ProductCatalog.Services;
using NexaCommerce.SharedKernel.Storage;
using Shouldly;
using Wolverine;
using Xunit;

namespace NexaCommerce.ProductCatalog.Tests.Services;

public sealed class ProductServiceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly Mock<IMessageBus> _busMock = new();
    private readonly Mock<IObjectStorageService> _storageMock = new();
    private readonly ProductService _sut;

    private static readonly Guid ElectronicsId = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ApparelId     = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Product1Id    = new("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Product2Id    = new("20000000-0000-0000-0000-000000000002");

    public ProductServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new CatalogDbContext(options);

        _db.Categories.AddRange(
            new Category { Id = ElectronicsId, Name = "Electronics" },
            new Category { Id = ApparelId,     Name = "Apparel" });

        _db.Products.AddRange(
            new Product
            {
                Id = Product1Id, Name = "Wireless Keyboard",
                Description = "Compact Bluetooth keyboard.", Price = 79.99m,
                CategoryId = ElectronicsId, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
            },
            new Product
            {
                Id = Product2Id, Name = "Ergonomic Chair",
                Description = "Lumbar support chair.", Price = 349.00m,
                CategoryId = ElectronicsId, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5)
            },
            new Product
            {
                Id = Guid.NewGuid(), Name = "Running Shoes",
                Description = "Lightweight trainers.", Price = 120.00m,
                CategoryId = ApparelId, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });

        _db.SaveChanges();

        _sut = new ProductService(
            _db,
            _busMock.Object,
            _storageMock.Object,
            NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task ListAsync_should_return_all_active_products_when_no_filters_applied()
    {
        var result = await _sut.ListAsync(new ListProductsRequest(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(3);
        result.Value.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ListAsync_should_filter_by_category_name()
    {
        var result = await _sut.ListAsync(
            new ListProductsRequest(Category: "Electronics"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldAllBe(i => i.CategoryName == "Electronics");
        result.Value.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ListAsync_should_filter_by_min_price()
    {
        var result = await _sut.ListAsync(
            new ListProductsRequest(MinPrice: 100m), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldAllBe(i => i.Price >= 100m);
    }

    [Fact]
    public async Task ListAsync_should_return_items_ordered_by_created_at_descending()
    {
        var result = await _sut.ListAsync(new ListProductsRequest(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var names = result.Value.Items.Select(i => i.Name).ToList();
        // Most recently created product (Running Shoes, -1 day) should come first
        names[0].ShouldBe("Running Shoes");
        names[1].ShouldBe("Ergonomic Chair");
        names[2].ShouldBe("Wireless Keyboard");
    }

    [Fact]
    public async Task ListAsync_should_paginate_correctly()
    {
        var result = await _sut.ListAsync(
            new ListProductsRequest(Page: 1, PageSize: 2), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
        result.Value.TotalCount.ShouldBe(3);
        result.Value.TotalPages.ShouldBe(2);
    }

    [Fact]
    public async Task GetByIdAsync_should_return_product_detail_when_id_exists()
    {
        var result = await _sut.GetByIdAsync(Product1Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(Product1Id);
        result.Value.Name.ShouldBe("Wireless Keyboard");
        result.Value.CategoryName.ShouldBe("Electronics");
    }

    [Fact]
    public async Task GetByIdAsync_should_return_not_found_when_id_does_not_exist()
    {
        var result = await _sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Status.ShouldBe(ResultStatus.NotFound);
    }

    [Fact]
    public async Task CreateAsync_should_create_product_and_publish_event()
    {
        var request = new CreateProductRequest(
            "USB-C Hub", "7-port USB-C hub.", 49.99m, ElectronicsId);

        var result = await _sut.CreateAsync(request, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("USB-C Hub");
        result.Value.Price.ShouldBe(49.99m);

        _busMock.Verify(b => b.PublishAsync(
            It.IsAny<Messaging.ProductCreatedEvent>(),
            It.IsAny<DeliveryOptions>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_should_return_invalid_when_category_does_not_exist()
    {
        var request = new CreateProductRequest(
            "Ghost Product", "No category.", 10m, Guid.NewGuid());

        var result = await _sut.CreateAsync(request, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Status.ShouldBe(ResultStatus.Invalid);
    }

    [Fact]
    public async Task DeleteAsync_should_delete_product_and_publish_event()
    {
        var result = await _sut.DeleteAsync(Product1Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var check = await _sut.GetByIdAsync(Product1Id, CancellationToken.None);
        check.Status.ShouldBe(ResultStatus.NotFound);

        _busMock.Verify(b => b.PublishAsync(
            It.IsAny<Messaging.ProductDeletedEvent>(),
            It.IsAny<DeliveryOptions>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_should_return_not_found_when_product_does_not_exist()
    {
        var result = await _sut.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        result.Status.ShouldBe(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetCategoryStatsAsync_should_return_stats_for_all_categories_with_products()
    {
        var result = await _sut.GetCategoryStatsAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(s => s.CategoryName == "Electronics");
        result.Value.First(s => s.CategoryName == "Electronics").ProductCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetCategoryStatsAsync_should_order_categories_by_product_count_descending()
    {
        var result = await _sut.GetCategoryStatsAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var counts = result.Value.Select(s => s.ProductCount).ToList();
        counts.ShouldBeInOrder(SortDirection.Descending);
    }

    public void Dispose() => _db.Dispose();
}