using PackageDeliverySystem.Web.Components;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Web.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using PackageDeliverySystem.Infrastructure;
using PackageDeliverySystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<IDeliveryDataService, MockDeliveryDataService>();
builder.Services.AddDbContext<DeliveryDbContext>(options => 
    options.UseMySQL(DatabaseSettings.GetConnectionString()));
builder.Services.AddScoped<IPackageRepository, PackageRepository>();
builder.Services.AddScoped<IBoxRepository, BoxRepository>();
builder.Services.AddScoped<IPackageAcceptanceService, PackageAcceptanceService>();
builder.Services.AddScoped<IPackageFulfillmentService, PackageFulfillmentService>();
builder.Services.AddScoped<IPackageIntakeService, PackageIntakeService>();
builder.Services.AddScoped<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddScoped<IPeopleService, PeopleService>();
builder.Services.AddScoped<IRatingService, RatingService>();
builder.Services.AddScoped<ISupportContactService, SupportContactService>();
builder.Services.AddScoped<IPackageOrderService, PackageOrderService>();
builder.Services.AddScoped<IPackageReturnService, PackageReturnService>();
builder.Services.AddScoped<EmailNotificationService>();
builder.Services.AddScoped<INotificationService, BackgroundNotificationService>();

builder.Services.AddHangfire(config => config.UseInMemoryStorage());
builder.Services.AddHangfireServer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        db.Database.Migrate();

        if (!db.Users.Any())
        {
            var originLocker = new ParcelLocker
            {
                Latitude = 50.0647, Longitude = 19.9450, Country = "Poland", City = "Krakow", PostalCode = "30-059", Street = "Mickiewicza", Number = "30"
            };
            originLocker.CreateBox(BoxSize.Small);
            originLocker.CreateBox(BoxSize.Medium);
            originLocker.CreateBox(BoxSize.Large);

            var destinationLocker = new ParcelLocker
            {
                Latitude = 52.2297, Longitude = 21.0122, Country = "Poland", City = "Warszawa", PostalCode = "00-001", Street = "Marszalkowska", Number = "10"
            };
            destinationLocker.CreateBox(BoxSize.Small);
            destinationLocker.CreateBox(BoxSize.Medium);
            destinationLocker.CreateBox(BoxSize.Large);

            db.ParcelLockers.AddRange(originLocker, destinationLocker);
            db.Users.AddRange(
                new User { Name = "Alice (Sklep)", Surname = "Nowak", Email = "alice@example.com", PhoneNumber = "500100200" },
                new User { Name = "Bob (Klient)", Surname = "Kowalski", Email = "bob@example.com", PhoneNumber = "500100201" },
                new Operator { Name = "Charlie (Operator)", Surname = "Wisniewski", Email = "charlie@example.com", PhoneNumber = "500100202" },
                new Courier { Name = "Dave (Kurier)", Surname = "Zielinski", Email = "dave@example.com", PhoneNumber = "500100203", Latitude = 50.06, Longitude = 19.94 });
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization note: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.UseHangfireDashboard("/hangfire");

RecurringJob.AddOrUpdate<IPackageAcceptanceService>(
    "expire-packages",
    service => service.ProcessExpiredPackagesAsync(),
    Cron.Minutely()
);

RecurringJob.AddOrUpdate<IPackageIntakeService>(
    "release-missed-deposits",
    service => service.ProcessMissedDepositsAsync(),
    Cron.Minutely()
);

app.Run();
