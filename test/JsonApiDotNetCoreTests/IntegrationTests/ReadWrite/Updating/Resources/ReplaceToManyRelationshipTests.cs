using System.Net;
using FluentAssertions;
using JsonApiDotNetCore;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.EntityFrameworkCore;
using TestBuildingBlocks;
using Xunit;

namespace JsonApiDotNetCoreTests.IntegrationTests.ReadWrite.Updating.Resources;

public sealed class ReplaceToManyRelationshipTests : IClassFixture<IntegrationTestContext<TestableStartup<ReadWriteDbContext>, ReadWriteDbContext>>
{
    private readonly IntegrationTestContext<TestableStartup<ReadWriteDbContext>, ReadWriteDbContext> _testContext;
    private readonly ReadWriteFakers _fakers = new();

    public ReplaceToManyRelationshipTests(IntegrationTestContext<TestableStartup<ReadWriteDbContext>, ReadWriteDbContext> testContext)
    {
        _testContext = testContext;

        testContext.UseController<WorkItemsController>();
        testContext.UseController<WorkItemGroupsController>();
        testContext.UseController<UserAccountsController>();

        testContext.ConfigureServices(services =>
        {
            services.AddResourceDefinition<ImplicitlyChangingWorkItemDefinition>();
        });
    }

    [Fact]
    public async Task Can_clear_OneToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        existingWorkItem.Subscribers = _fakers.UserAccount.Generate(2).ToHashSet();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = Array.Empty<object>()
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Subscribers).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Subscribers.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Can_clear_ManyToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        existingWorkItem.Tags = _fakers.WorkTag.Generate(1).ToHashSet();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    tags = new
                    {
                        data = Array.Empty<object>()
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Tags).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Tags.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Can_replace_OneToMany_relationship_with_already_assigned_resources()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        existingWorkItem.Subscribers = _fakers.UserAccount.Generate(2).ToHashSet();

        UserAccount existingSubscriber = _fakers.UserAccount.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.AddInRange(existingWorkItem, existingSubscriber);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "userAccounts",
                                id = existingWorkItem.Subscribers.ElementAt(1).StringId
                            },
                            new
                            {
                                type = "userAccounts",
                                id = existingSubscriber.StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Subscribers).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Subscribers.ShouldHaveCount(2);
            workItemInDatabase.Subscribers.Should().ContainSingle(userAccount => userAccount.Id == existingWorkItem.Subscribers.ElementAt(1).Id);
            workItemInDatabase.Subscribers.Should().ContainSingle(userAccount => userAccount.Id == existingSubscriber.Id);
        });
    }

    [Fact]
    public async Task Can_replace_ManyToMany_relationship_with_already_assigned_resources()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        existingWorkItem.Tags = _fakers.WorkTag.Generate(2).ToHashSet();

        List<WorkTag> existingTags = _fakers.WorkTag.Generate(2);

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            dbContext.WorkTags.AddRange(existingTags);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    tags = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "workTags",
                                id = existingWorkItem.Tags.ElementAt(0).StringId
                            },
                            new
                            {
                                type = "workTags",
                                id = existingTags[0].StringId
                            },
                            new
                            {
                                type = "workTags",
                                id = existingTags[1].StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Tags).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Tags.ShouldHaveCount(3);
            workItemInDatabase.Tags.Should().ContainSingle(workTag => workTag.Id == existingWorkItem.Tags.ElementAt(0).Id);
            workItemInDatabase.Tags.Should().ContainSingle(workTag => workTag.Id == existingTags[0].Id);
            workItemInDatabase.Tags.Should().ContainSingle(workTag => workTag.Id == existingTags[1].Id);
        });
    }

    [Fact]
    public async Task Can_replace_OneToMany_relationship_with_include()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        UserAccount existingUserAccount = _fakers.UserAccount.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.AddInRange(existingWorkItem, existingUserAccount);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "userAccounts",
                                id = existingUserAccount.StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}?include=subscribers";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();
        responseDocument.Data.SingleValue.Type.Should().Be("workItems");
        responseDocument.Data.SingleValue.Id.Should().Be(existingWorkItem.StringId);
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("priority").With(value => value.Should().Be(existingWorkItem.Priority));
        responseDocument.Data.SingleValue.Relationships.ShouldNotBeEmpty();

        responseDocument.Included.ShouldHaveCount(1);
        responseDocument.Included[0].Type.Should().Be("userAccounts");
        responseDocument.Included[0].Id.Should().Be(existingUserAccount.StringId);
        responseDocument.Included[0].Attributes.ShouldContainKey("firstName").With(value => value.Should().Be(existingUserAccount.FirstName));
        responseDocument.Included[0].Attributes.ShouldContainKey("lastName").With(value => value.Should().Be(existingUserAccount.LastName));
        responseDocument.Included[0].Relationships.ShouldNotBeEmpty();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Subscribers).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Subscribers.ShouldHaveCount(1);
            workItemInDatabase.Subscribers.Single().Id.Should().Be(existingUserAccount.Id);
        });
    }

    [Fact]
    public async Task Can_replace_ManyToMany_relationship_with_include_and_fieldsets()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        WorkTag existingTag = _fakers.WorkTag.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.AddInRange(existingWorkItem, existingTag);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    tags = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "workTags",
                                id = existingTag.StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}?fields[workItems]=priority,tags&include=tags&fields[workTags]=text";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();
        responseDocument.Data.SingleValue.Type.Should().Be("workItems");
        responseDocument.Data.SingleValue.Id.Should().Be(existingWorkItem.StringId);
        responseDocument.Data.SingleValue.Attributes.ShouldHaveCount(1);
        responseDocument.Data.SingleValue.Attributes.ShouldContainKey("priority").With(value => value.Should().Be(existingWorkItem.Priority));
        responseDocument.Data.SingleValue.Relationships.ShouldHaveCount(1);

        responseDocument.Data.SingleValue.Relationships.ShouldContainKey("tags").With(value =>
        {
            value.ShouldNotBeNull();
            value.Data.ManyValue.ShouldHaveCount(1);
            value.Data.ManyValue[0].Id.Should().Be(existingTag.StringId);
        });

        responseDocument.Included.ShouldHaveCount(1);
        responseDocument.Included[0].Type.Should().Be("workTags");
        responseDocument.Included[0].Id.Should().Be(existingTag.StringId);
        responseDocument.Included[0].Attributes.ShouldHaveCount(1);
        responseDocument.Included[0].Attributes.ShouldContainKey("text").With(value => value.Should().Be(existingTag.Text));
        responseDocument.Included[0].Relationships.Should().BeNull();

        int newWorkItemId = int.Parse(responseDocument.Data.SingleValue.Id.ShouldNotBeNull());

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Tags).FirstWithIdAsync(newWorkItemId);

            workItemInDatabase.Tags.ShouldHaveCount(1);
            workItemInDatabase.Tags.Single().Id.Should().Be(existingTag.Id);
        });
    }

    [Fact]
    public async Task Cannot_replace_for_missing_relationship_type()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                id = Unknown.StringId.For<UserAccount, long>()
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: The 'type' element is required.");
        error.Detail.Should().BeNull();
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/subscribers/data[0]");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Cannot_replace_for_unknown_relationship_type()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = Unknown.ResourceType,
                                id = Unknown.StringId.For<UserAccount, long>()
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: Unknown resource type found.");
        error.Detail.Should().Be($"Resource type '{Unknown.ResourceType}' does not exist.");
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/subscribers/data[0]/type");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Cannot_replace_for_missing_relationship_ID()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "userAccounts"
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: The 'id' element is required.");
        error.Detail.Should().BeNull();
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/subscribers/data[0]");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Cannot_replace_with_unknown_relationship_IDs()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        string userAccountId1 = Unknown.StringId.For<UserAccount, long>();
        string userAccountId2 = Unknown.StringId.AltFor<UserAccount, long>();

        string tagId1 = Unknown.StringId.For<WorkTag, int>();
        string tagId2 = Unknown.StringId.AltFor<WorkTag, int>();

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "userAccounts",
                                id = userAccountId1
                            },
                            new
                            {
                                type = "userAccounts",
                                id = userAccountId2
                            }
                        }
                    },
                    tags = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "workTags",
                                id = tagId1
                            },
                            new
                            {
                                type = "workTags",
                                id = tagId2
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.NotFound);

        responseDocument.Errors.ShouldHaveCount(4);

        ErrorObject error1 = responseDocument.Errors[0];
        error1.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error1.Title.Should().Be("A related resource does not exist.");
        error1.Detail.Should().Be($"Related resource of type 'userAccounts' with ID '{userAccountId1}' in relationship 'subscribers' does not exist.");

        ErrorObject error2 = responseDocument.Errors[1];
        error2.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error2.Title.Should().Be("A related resource does not exist.");
        error2.Detail.Should().Be($"Related resource of type 'userAccounts' with ID '{userAccountId2}' in relationship 'subscribers' does not exist.");

        ErrorObject error3 = responseDocument.Errors[2];
        error3.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error3.Title.Should().Be("A related resource does not exist.");
        error3.Detail.Should().Be($"Related resource of type 'workTags' with ID '{tagId1}' in relationship 'tags' does not exist.");

        ErrorObject error4 = responseDocument.Errors[3];
        error4.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error4.Title.Should().Be("A related resource does not exist.");
        error4.Detail.Should().Be($"Related resource of type 'workTags' with ID '{tagId2}' in relationship 'tags' does not exist.");
    }

    [Fact]
    public async Task Cannot_replace_on_relationship_type_mismatch()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "rgbColors",
                                id = "0A0B0C"
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.Conflict);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.Conflict);
        error.Title.Should().Be("Failed to deserialize request body: Incompatible resource type found.");
        error.Detail.Should().Be("Type 'rgbColors' is not convertible to type 'userAccounts' of relationship 'subscribers'.");
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/subscribers/data[0]/type");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Can_replace_with_duplicates()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();
        existingWorkItem.Subscribers = _fakers.UserAccount.Generate(1).ToHashSet();

        UserAccount existingSubscriber = _fakers.UserAccount.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.AddInRange(existingWorkItem, existingSubscriber);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "userAccounts",
                                id = existingSubscriber.StringId
                            },
                            new
                            {
                                type = "userAccounts",
                                id = existingSubscriber.StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Subscribers).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Subscribers.ShouldHaveCount(1);
            workItemInDatabase.Subscribers.Single().Id.Should().Be(existingSubscriber.Id);
        });
    }

    [Fact]
    public async Task Cannot_replace_with_missing_data_in_OneToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    subscribers = new
                    {
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: The 'data' element is required.");
        error.Detail.Should().BeNull();
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/subscribers");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Cannot_replace_with_null_data_in_ManyToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    tags = new
                    {
                        data = (object?)null
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: Expected an array, instead of 'null'.");
        error.Detail.Should().BeNull();
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/tags/data");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Cannot_replace_with_object_data_in_ManyToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    tags = new
                    {
                        data = new
                        {
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: Expected an array, instead of an object.");
        error.Detail.Should().BeNull();
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/tags/data");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }

    [Fact]
    public async Task Can_clear_cyclic_OneToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();

            existingWorkItem.Children = existingWorkItem.AsList();
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    children = new
                    {
                        data = Array.Empty<object>()
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Children).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Children.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Can_clear_cyclic_ManyToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();

            existingWorkItem.RelatedFrom = ArrayFactory.Create(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    relatedFrom = new
                    {
                        data = Array.Empty<object>()
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            // @formatter:wrap_chained_method_calls chop_always
            // @formatter:wrap_after_property_in_chained_method_calls true

            WorkItem workItemInDatabase = await dbContext.WorkItems
                .Include(workItem => workItem.RelatedFrom)
                .Include(workItem => workItem.RelatedTo)
                .FirstWithIdAsync(existingWorkItem.Id);

            // @formatter:wrap_after_property_in_chained_method_calls restore
            // @formatter:wrap_chained_method_calls restore

            workItemInDatabase.RelatedFrom.Should().BeEmpty();
            workItemInDatabase.RelatedTo.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Can_assign_cyclic_OneToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    children = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "workItems",
                                id = existingWorkItem.StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            WorkItem workItemInDatabase = await dbContext.WorkItems.Include(workItem => workItem.Children).FirstWithIdAsync(existingWorkItem.Id);

            workItemInDatabase.Children.ShouldHaveCount(1);
            workItemInDatabase.Children[0].Id.Should().Be(existingWorkItem.Id);
        });
    }

    [Fact]
    public async Task Can_assign_cyclic_ManyToMany_relationship()
    {
        // Arrange
        WorkItem existingWorkItem = _fakers.WorkItem.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.WorkItems.Add(existingWorkItem);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItems",
                id = existingWorkItem.StringId,
                relationships = new
                {
                    relatedTo = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "workItems",
                                id = existingWorkItem.StringId
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItems/{existingWorkItem.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        responseDocument.Data.SingleValue.ShouldNotBeNull();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            // @formatter:wrap_chained_method_calls chop_always
            // @formatter:wrap_after_property_in_chained_method_calls true

            WorkItem workItemInDatabase = await dbContext.WorkItems
                .Include(workItem => workItem.RelatedFrom)
                .Include(workItem => workItem.RelatedTo)
                .FirstWithIdAsync(existingWorkItem.Id);

            // @formatter:wrap_after_property_in_chained_method_calls restore
            // @formatter:wrap_chained_method_calls restore

            workItemInDatabase.RelatedFrom.ShouldHaveCount(1);
            workItemInDatabase.RelatedFrom[0].Id.Should().Be(existingWorkItem.Id);

            workItemInDatabase.RelatedTo.ShouldHaveCount(1);
            workItemInDatabase.RelatedTo[0].Id.Should().Be(existingWorkItem.Id);
        });
    }

    [Fact]
    public async Task Cannot_assign_relationship_with_blocked_capability()
    {
        // Arrange
        WorkItemGroup existingWorkItemGroup = _fakers.WorkItemGroup.Generate();

        await _testContext.RunOnDatabaseAsync(async dbContext =>
        {
            dbContext.Groups.Add(existingWorkItemGroup);
            await dbContext.SaveChangesAsync();
        });

        var requestBody = new
        {
            data = new
            {
                type = "workItemGroups",
                id = existingWorkItemGroup.StringId,
                relationships = new
                {
                    items = new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "workItems",
                                id = Unknown.StringId.For<WorkItem, int>()
                            }
                        }
                    }
                }
            }
        };

        string route = $"/workItemGroups/{existingWorkItemGroup.StringId}";

        // Act
        (HttpResponseMessage httpResponse, Document responseDocument) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

        // Assert
        httpResponse.ShouldHaveStatusCode(HttpStatusCode.UnprocessableEntity);

        responseDocument.Errors.ShouldHaveCount(1);

        ErrorObject error = responseDocument.Errors[0];
        error.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.Title.Should().Be("Failed to deserialize request body: Relationship cannot be assigned.");
        error.Detail.Should().Be("The relationship 'items' on resource type 'workItemGroups' cannot be assigned to.");
        error.Source.ShouldNotBeNull();
        error.Source.Pointer.Should().Be("/data/relationships/items");
        error.Meta.ShouldContainKey("requestBody").With(value => value.ShouldNotBeNull().ToString().ShouldNotBeEmpty());
    }
}
