using Basket.API.Apis;
using Basket.API.Messaging;
using Basket.API.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient("redis");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddSingleton<IBasketRepository, RedisBasketRepository>();
builder.Services.AddSingleton<IEventPublisher>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("rabbitmq")!;
    var publisher = RabbitMQPublisher.CreateAsync(connectionString).GetAwaiter().GetResult();
    return new EventPublisher(publisher);
});

var app = builder.Build();

app.UseCors("AllowAll");
app.MapDefaultEndpoints();
app.MapBasketApi();

app.Run();