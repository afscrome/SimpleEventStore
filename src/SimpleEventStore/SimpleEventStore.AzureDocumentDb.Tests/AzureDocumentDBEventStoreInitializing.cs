using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using NUnit.Framework;

namespace SimpleEventStore.AzureDocumentDb.Tests
{
    [TestFixture]
    public class AzureDocumentDBEventStoreInitializing
    {
        private const string DatabaseName = "InitializeTests";
        private readonly Uri databaseUri = UriFactory.CreateDatabaseUri(DatabaseName);
        private readonly DocumentClient client = DocumentClientFactory.Create();

        [TearDown]
        public Task TearDownDatabase()
        {
            return client.DeleteDatabaseAsync(databaseUri);
        }

        [Test]
        public async Task when_initializing_all_expected_resources_are_created()
        {
            var collectionName = "AllExpectedResourcesAreCreated_" + Guid.NewGuid();
            var storageEngine = await StorageEngineFactory.Create(DatabaseName, o => o.CollectionName = collectionName);

            await storageEngine.Initialise();

            var database = (await client.ReadDatabaseAsync(databaseUri)).Resource;
            var collection = (await client.ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(DatabaseName, collectionName))).Resource;
            var storedProcedure = (await client.ReadStoredProcedureAsync(UriFactory.CreateStoredProcedureUri(DatabaseName, collectionName, TestConstants.AppendStoredProcedureName))).Resource;
            var offer = client.CreateOfferQuery()
                .Where(r => r.ResourceLink == collection.SelfLink)
                .AsEnumerable()
                .OfType<OfferV2>()
                .Single();

            Assert.That(offer.Content.OfferThroughput, Is.EqualTo(TestConstants.RequestUnits));
            Assert.That(collection.DefaultTimeToLive, Is.Null);
            Assert.That(collection.PartitionKey.Paths.Count, Is.EqualTo(1));
            Assert.That(collection.PartitionKey.Paths.Single(), Is.EqualTo("/streamId"));
            Assert.That(collection.IndexingPolicy.IncludedPaths.Count, Is.EqualTo(1));
            Assert.That(collection.IndexingPolicy.IncludedPaths[0].Path, Is.EqualTo("/*"));
            Assert.That(collection.IndexingPolicy.ExcludedPaths.Count, Is.EqualTo(3));
            Assert.That(collection.IndexingPolicy.ExcludedPaths[0].Path, Is.EqualTo("/body/*"));
            Assert.That(collection.IndexingPolicy.ExcludedPaths[1].Path, Is.EqualTo("/metadata/*"));
        }

        [Test]
        public async Task when_initializing_with_a_time_to_live_it_is_set()
        {
            var ttl = 60;
            var collectionName = "TimeToLiveIsSet_" + Guid.NewGuid();
            var storageEngine = await StorageEngineFactory.Create(DatabaseName, o =>
            {
                o.CollectionName = collectionName;
                o.DefaultTimeToLive = ttl;
            });

            await storageEngine.Initialise();

            var collection =
                (await client.ReadDocumentCollectionAsync(
                    UriFactory.CreateDocumentCollectionUri(DatabaseName, collectionName))).Resource;
            Assert.That(collection.DefaultTimeToLive, Is.EqualTo(ttl));
        }

        [Test]
        public async Task when_using_shared_throughput_it_is_set_at_a_database_level()
        {
            const int throughput = 800;
            var collectionName = "SharedCollection_" + Guid.NewGuid();

            var storageEngine = await StorageEngineFactory.Create(DatabaseName,
                collectionOptions =>
                {
                    collectionOptions.CollectionName = collectionName;
                    collectionOptions.CollectionRequestUnits = null;
                },
                databaseOptions => { databaseOptions.DatabaseRequestUnits = throughput; });

            await storageEngine.Initialise();

            Assert.AreEqual(throughput, await GetDatabaseThroughput());
        }

        [Test]
        public async Task when_throughput_is_set_offer_is_updated()
        {
            var dbThroughput = 800;
            var collectionThroughput = 400;
            var collectionName = "UpdateThroughput_" + Guid.NewGuid();

            await InitialiseStorageEngine(collectionName, collectionThroughput, dbThroughput);

            Assert.AreEqual(dbThroughput, await GetDatabaseThroughput());
            Assert.AreEqual(collectionThroughput, await GetCollectionThroughput(collectionName));

            dbThroughput = 1600;
            collectionThroughput = 800;

            await InitialiseStorageEngine(collectionName, collectionThroughput, dbThroughput);

            Assert.AreEqual(dbThroughput, await GetDatabaseThroughput());
            Assert.AreEqual(collectionThroughput, await GetCollectionThroughput(collectionName));
        }

        private static async Task InitialiseStorageEngine(string collectionName, int collectionThroughput,
            int dbThroughput)
        {
            var storageEngine = await StorageEngineFactory.Create(DatabaseName,
                collectionOptions =>
                {
                    collectionOptions.CollectionName = collectionName;
                    collectionOptions.CollectionRequestUnits = collectionThroughput;
                },
                databaseOptions => { databaseOptions.DatabaseRequestUnits = dbThroughput; });

            await storageEngine.Initialise();
        }

        public async Task<int> GetCollectionThroughput(string collectionName)
        {
            var collection = await client.ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(DatabaseName, collectionName));

            var collectionOffer = client.CreateOfferQuery().Where(x => x.ResourceLink == collection.Resource.SelfLink)
                .AsEnumerable().First();

            return ((OfferV2)collectionOffer).Content.OfferThroughput;
        }

        public async Task<int> GetDatabaseThroughput()
        {
            var db = await client.ReadDatabaseAsync(databaseUri);
            var dbOffer = client.CreateOfferQuery().Where(x => x.ResourceLink == db.Resource.SelfLink).AsEnumerable()
                .First();

            return ((OfferV2)dbOffer).Content.OfferThroughput;
        }
    }
}