using MongoDB.Driver;
using MaisonGlace.API.Models;
using MaisonGlace.API.Settings;
using Microsoft.Extensions.Options;

namespace MaisonGlace.API.Services;

public class DatabaseContext
{
    private readonly IMongoDatabase _database;

    public DatabaseContext(IOptions<MongoDbSettings> options)
    {
        var cfg = options.Value;
        var mongoUrl = MongoUrl.Create(cfg.ConnectionString);
        var client = new MongoClient(mongoUrl);
        _database = client.GetDatabase(mongoUrl.DatabaseName);
    }

    public Task PingAsync(CancellationToken cancellationToken = default) =>
        _database.RunCommandAsync((Command<MongoDB.Bson.BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);

    public IMongoCollection<T> GetCollection<T>(string name) =>
        _database.GetCollection<T>(name);
}
