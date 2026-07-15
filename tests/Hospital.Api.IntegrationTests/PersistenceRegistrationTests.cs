using Hospital.Core.Persistence;
using Hospital.Infrastructure;
using Hospital.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hospital.Api.IntegrationTests;

public sealed class PersistenceRegistrationTests(HospitalApiFactory factory)
    : IClassFixture<HospitalApiFactory>
{
    [Fact]
    public void CoreContractAndConcreteContextShareOneScopedInstance()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        IApplicationDbContext contract = scope.ServiceProvider
            .GetRequiredService<IApplicationDbContext>();
        ApplicationDbContext concreteContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        Assert.Same(concreteContext, contract);
        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            concreteContext.Database.ProviderName);
    }

    [Fact]
    public void BlankConnectionStringIsRejectedDuringRegistration()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentException>(() => services.AddInfrastructure(" "));
    }
}
