using System.Net;
using FluentAssertions;
using FluentAssertions.Extensions;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestBuildingBlocks;
using Xunit;

namespace JsonApiDotNetCoreTests.IntegrationTests.ResourceConstructorInjection;

public sealed class ResourceInjectionTests : IClassFixture<IntegrationTestContext<TestableStartup<InjectionDbContext>, InjectionDbContext>>
{
    private readonly IntegrationTestContext<TestableStartup<InjectionDbContext>, InjectionDbContext> _testContext;
    private readonly InjectionFakers _fakers;

    public ResourceInjectionTests(IntegrationTestContext<TestableStartup<InjectionDbContext>, InjectionDbContext> testContext)
    {
        _testContext = testContext;

        testContext.UseController<GiftCertificatesController>();
        testContext.UseController<PostOfficesController>();

        testContext.ConfigureServices(services =>
        {
            services.AddSingleton<ISystemClock, FrozenSystemClock>();
        });

        _fakers = new InjectionFakers(testContext.Factory.Services);
    }

    [Fact]
    public async Task Can_get_resource_by_ID()
    {
        // Arrange
        var clock = (FrozenSystemClock)_testContext.Factory.Services.GetRequiredService<ISystemClock>();
        clock.UtcNow = 27.January(2021).AsUtc();

        GiftCertificate certificate = _fakers.GiftCertificate.Generate();
        certificate.IssueDate = 28.January(2020).AsUtc();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.GiftCertificates.Add(certificate);
            await dbContext.SaveChangesAsync();
        });

        string route = $"/giftCertificates/{certificate.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecuteGetAsync<Document>(route);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();
        responseDocument.Data.SingleValue.Id.Should().Be(certificate.StringId);
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("issueDate").With(value => value.Should().Be(certificate.IssueDate));
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("hasExpired").With(value => value.Should().Be(false));
    }

    [Fact]
    public async Task Can_filter_resources_by_ID()
    {
        // Arrange
        var clock = (FrozenSystemClock)_testContext.Factory.Services.GetRequiredService<ISystemClock>();
        clock.UtcNow = 27.January(2021).At(13, 53).AsUtc();

        List<PostOffice> postOffices = _fakers.PostOffice.Generate(2);

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            await dbContext.ClearTableAsync<PostOffice>();
            dbContext.PostOffices.AddRange(postOffices);
            await dbContext.SaveChangesAsync();
        });

        string route = $"/postOffices?filter=equals(id,'{postOffices[1].StringId}')";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecuteGetAsync<Document>(route);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.ManyValue.ShouldHaveCount(1);
        responseDocument.Data.ManyValue[0].Id.Should().Be(postOffices[1].StringId);
        responseDocument.Data.ManyValue[0].Attributes.ShouldContainKey("address").With(value => value.Should().Be(postOffices[1].Address));
        responseDocument.Data.ManyValue[0].Attributes.ShouldContainKey("isOpen").With(value => value.Should().Be(true));
    }

    [Fact]
    public async Task Can_get_secondary_resource_with_fieldset()
    {
        // Arrange
        var clock = (FrozenSystemClock)_testContext.Factory.Services.GetRequiredService<ISystemClock>();
        clock.UtcNow = 27.January(2021).At(13, 53).AsUtc();

        GiftCertificate certificate = _fakers.GiftCertificate.Generate();
        certificate.Issuer = _fakers.PostOffice.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.GiftCertificates.Add(certificate);
            await dbContext.SaveChangesAsync();
        });

        string route = $"/giftCertificates/{certificate.StringId}/issuer?fields[postOffices]=id,isOpen";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecuteGetAsync<Document>(route);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();
        responseDocument.Data.SingleValue.Id.Should().Be(certificate.Issuer.StringId);
        responseDocument.Data.SingleValue.Attributes.ShouldHaveCount(1);
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("isOpen").With(value => value.Should().Be(true));
    }

    [Fact]
    public async Task Can_create_resource_with_ToOne_relationship_and_include()
    {
        // Arrange
        var clock = (FrozenSystemClock)_testContext.Factory.Services.GetRequiredService<ISystemClock>();
        clock.UtcNow = 19.March(1998).At(6, 34).AsUtc();

        PostOffice existingOffice = _fakers.PostOffice.Generate();

        DateTimeOffset newIssueDate = 18.March(1997).AsUtc();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.PostOffices.Add(existingOffice);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "giftCertificates",
                attributes = new
                {
                    issueDate = newIssueDate
                },
                relationships = new
                {
                    issuer = new
                    {
                        data = new
                        {
                            type = "postOffices",
                            id = existingOffice.StringId
                        }
                    }
                }
            }
        };

        const string route = "/giftCertificates?include=issuer";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePostAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.Created);

        responseDocument.Data.SingleValue.ShouldNotBeNull();
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("issueDate").With(value => value.Should().Be(newIssueDate));
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("hasExpired").With(value => value.Should().Be(true));

        responseDocument.Data.SingleValue.Relationships.ShouldContainKey("issuer").With(value =>
        {
            value.ShouldNotBeNull();
            value.Data.SingleValue.ShouldNotBeNull();
            value.Data.SingleValue.Id.Should().Be(existingOffice.StringId);
        });

        responseDocument.Included.ShouldHaveCount(1);

        responseDocument.Included[0].With(resource =>
        {
            resource.Id.Should().Be(existingOffice.StringId);
            resource.Attributes.ShouldContainKey("address").With(value => value.Should().Be(existingOffice.Address));
            resource.Attributes.ShouldContainKey("isOpen").With(value => value.Should().Be(false));
        });

        int newCertificateId = int.Parse(responseDocument.Data.SingleValue.Id.ShouldNotBeNull());

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            GiftCertificate certificateInDatabase = await dbContext.GiftCertificates
                .Include(certificate => certificate.Issuer).FirstWithIdAsync(newCertificateId);

            certificateInDatabase.IssueDate.Should().Be(newIssueDate);

            certificateInDatabase.Issuer.ShouldNotBeNull();
            certificateInDatabase.Issuer.Id.Should().Be(existingOffice.Id);
        });
    }

    [Fact]
    public async Task Can_update_resource_with_ToMany_relationship()
    {
        // Arrange
        var clock = (FrozenSystemClock)_testContext.Factory.Services.GetRequiredService<ISystemClock>();
        clock.UtcNow = 19.March(1998).At(6, 34).AsUtc();

        PostOffice existingOffice = _fakers.PostOffice.Generate();
        existingOffice.GiftCertificates = _fakers.GiftCertificate.Generate(1);

        string newAddress = _fakers.PostOffice.Generate().Address;

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.PostOffices.Add(existingOffice);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "postOffices",
                id = existingOffice.StringId,
                attributes = new
                {
                    address = newAddress
                },
                relationships = new
                {
                    giftCertificates = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "giftCertificates",
                                id = existingOffice.GiftCertificates[0].StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/postOffices/{existingOffice.StringId}";

        // Act
        (HttpResponseMessage httpResponse, string responseDocument) = await _testContext.ExecutePatchAsync<string>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        responseDocument.Should().BeEmpty();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            PostOffice officeInDatabase = await dbContext.PostOffices.Include(postOffice => postOffice.GiftCertificates).FirstWithIdAsync(existingOffice.Id);

            officeInDatabase.Address.Should().Be(newAddress);

            officeInDatabase.GiftCertificates.ShouldHaveCount(1);
            officeInDatabase.GiftCertificates[0].Id.Should().Be(existingOffice.GiftCertificates[0].Id);
        });
    }

    [Fact]
    public async Task Can_delete_resource()
    {
        // Arrange
        PostOffice existingOffice = _fakers.PostOffice.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.PostOffices.Add(existingOffice);
            await dbContext.SaveChangesAsync();
        });

        string route = $"/postOffices/{existingOffice.StringId}";

        // Act
        (HttpResponseMessage httpResponse, string responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        responseDocument.Should().BeEmpty();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            PostOffice? officeInDatabase = await dbContext.PostOffices.FirstWithIdOrDefaultAsync(existingOffice.Id);

            officeInDatabase.Should().BeNull();
        });
    }

    [Fact]
    public async Task Cannot_delete_unknown_resource()
    {
        // Arrange
        string officeId = Unknown.StringId.For<PostOffice, int>();

        string route = $"/postOffices/{officeId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecuteDeleteAsync<Document>(route);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.NotFound);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error.Title.Should().Be("The requested resource does not exist.");
        error.Detail.Should().Be($"Resource of type 'postOffices' with ID '{officeId}' does not exist.");
    }

    [Fact]
    public async Task Can_add_to_ToMany_relationship()
    {
        // Arrange
        PostOffice existingOffice = _fakers.PostOffice.Generate();
        existingOffice.GiftCertificates = _fakers.GiftCertificate.Generate(1);

        GiftCertificate existingCertificate = _fakers.GiftCertificate.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.AddInRange(existingOffice, existingCertificate);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new[]
            {
                new
                {
                    type = "giftCertificates",
                    id = existingCertificate.StringId
                }
            }
        };

        string route = $"/postOffices/{existingOffice.StringId}/relationships/giftCertificates";

        // Act
        (HttpResponseMessage httpResponse, string responseDocument) = await _testContext.ExecutePostAsync<string>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        responseDocument.Should().BeEmpty();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            PostOffice officeInDatabase = await dbContext.PostOffices.Include(postOffice => postOffice.GiftCertificates).FirstWithIdAsync(existingOffice.Id);

            officeInDatabase.GiftCertificates.ShouldHaveCount(2);
        });
    }
}
