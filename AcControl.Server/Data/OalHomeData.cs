namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using Azure;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Runtime.CompilerServices;

public class OalHomeData : IDisposable
{
    private const string CATS_DATABASE_ID = "Cats";
    private const string FOODS_CONTAINER_ID = "Foods";
    private const string FEEDS_CONTAINER_ID = "Feeds";
    private const string STANDARD_SELECT_ALL_QUERY = "SELECT * FROM Items item";

    private readonly CosmosClient mClient;

    public OalHomeData(IConfiguration config)
    {
        mClient = new(
            accountEndpoint: config.GetValue<string>("CosmosDB:Endpoint"),
            authKeyOrResourceToken: config.GetValue<string>("CosmosDB:Key"),
            clientOptions: new CosmosClientOptions()
            {
                SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
            }
        );
    }

    public async Task<bool> UpsertCatFeed(CatFeed feed, CancellationToken cancellationToken = default)
    {
        var container = mClient.GetDatabase(CATS_DATABASE_ID).GetContainer(FEEDS_CONTAINER_ID);
        var result = await container.UpsertItemAsync(feed, cancellationToken: cancellationToken);

        return result.StatusCode == HttpStatusCode.OK;
    }

    public IAsyncEnumerable<CatFeed> GetCatFeeds(int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        return this.GetItems<CatFeed>(
            CATS_DATABASE_ID, 
            FEEDS_CONTAINER_ID, 
            STANDARD_SELECT_ALL_QUERY + $" OFFSET {offset} LIMIT {limit}", 
            cancellationToken);
    }

    public async Task UpsertCatFood(CatFood food, CancellationToken cancellationToken = default)
    {
        var container = mClient.GetDatabase(CATS_DATABASE_ID).GetContainer(FOODS_CONTAINER_ID);
        _ = await container.UpsertItemAsync(food, cancellationToken: cancellationToken);
    }

    public async Task<bool> DeleteCatFood(CatFood food, CancellationToken cancellationToken = default)
    {
        var container = mClient.GetDatabase(CATS_DATABASE_ID).GetContainer(FOODS_CONTAINER_ID);
        var result = await container.DeleteItemAsync<CatFood>(food.Id, new PartitionKey(food.Id), cancellationToken: cancellationToken);

        return result.StatusCode == HttpStatusCode.OK || result.StatusCode == HttpStatusCode.NoContent;
    }

    public async Task<bool> DeleteCatFeed(CatFeed feed, CancellationToken cancellationToken = default)
    {
        var container = mClient.GetDatabase(CATS_DATABASE_ID).GetContainer(FEEDS_CONTAINER_ID);
        var result = await container.DeleteItemAsync<CatFeed>(feed.Id, new PartitionKey(feed.Id), cancellationToken: cancellationToken);

        return result.StatusCode == HttpStatusCode.OK || result.StatusCode == HttpStatusCode.NoContent;
    }

    public IAsyncEnumerable<CatFood> GetCatFoods(CancellationToken cancellationToken = default)
    {
        return this.GetItems<CatFood>(CATS_DATABASE_ID, FOODS_CONTAINER_ID, cancellationToken: cancellationToken);
    }

    private async IAsyncEnumerable<T> GetItems<T>(
        string databaseId, 
        string containerId, 
        string query = STANDARD_SELECT_ALL_QUERY, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var container = mClient.GetDatabase(databaseId).GetContainer(containerId);
        
        using var feed = container.GetItemQueryIterator<T>(new QueryDefinition(query));

        while (feed.HasMoreResults)
        {
            var response = await feed.ReadNextAsync(cancellationToken);
            foreach (var item in response)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Not sure if this is a thing?
                    yield break;
                }

                yield return item;
            }
        }
    }

    public void Dispose()
    {
        mClient.Dispose();
    }
}

public static class Extensions
{
    public static async Task<List<T>> ToList<T>(this IAsyncEnumerable<T> asyncEnumerable)
    {
        var list = new List<T>();

        await foreach (var t in asyncEnumerable)
        {
            list.Add(t);
        }

        return list;
    }
}