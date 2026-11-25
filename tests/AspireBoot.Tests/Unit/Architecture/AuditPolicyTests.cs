using AspireBoot.Core.Persistence;
using AspireBoot.Core.Persistence.Auditing;
using AspireBoot.Tests.Unit.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Unit.Architecture;

[Trait("Category", "Unit")]
public sealed class AuditPolicyTests
{
    private static IModel Model()
    {
        using var context = AuditEntryFactoryTests.CreateContext();
        return ((Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure<IServiceProvider>)context).Instance
            .GetRequiredService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
    }

    [Fact]
    public void AuditedEntityTypes_HaveNoStoreGeneratedKey()
    {
        var offenders = Model().GetEntityTypes()
            .Where(AuditPolicy.IsAudited)
            .Where(AuditPolicy.HasStoreGeneratedKey)
            .Select(type => type.DisplayName())
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GuidKeysWithClientSideGenerator_AreAccepted()
    {
        var users = Model().GetEntityTypes().Single(type => type.ClrType == typeof(Core.Identity.Domain.User));

        Assert.False(AuditPolicy.HasStoreGeneratedKey(users));
    }

    [Fact]
    public void EveryMaskedName_ExistsInTheModel()
    {
        var types = Model().GetEntityTypes().ToArray();

        foreach (var name in AuditPolicy.MaskedPropertyNames)
        {
            Assert.Contains(types, type => type.FindProperty(name) is not null);
        }

        foreach (var (clrType, property) in AuditPolicy.MaskedTypeProperties)
        {
            Assert.Contains(types, type => IsClosedFrom(type.ClrType, clrType) && type.FindProperty(property) is not null);
        }
    }

    [Fact]
    public void EveryOwnedOrComplexType_IsClassified()
    {
        var model = Model();
        var owned = model.GetEntityTypes().Where(type => type.IsOwned()).Select(type => type.ClrType);
        var complex = model.GetEntityTypes()
            .SelectMany(type => type.GetComplexProperties())
            .Select(property => property.ComplexType.ClrType);

        var unclassified = owned.Concat(complex)
            .Distinct()
            .Where(type => !AuditPolicy.OwnedTypes.ContainsKey(type))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(unclassified);
    }

    [Fact]
    public void PasskeyData_IsExcludedByType()
    {
        Assert.True(AuditPolicy.OwnedTypes[typeof(Microsoft.AspNetCore.Identity.IdentityPasskeyData)]);
    }

    [Fact]
    public void EveryEntityType_IsAuditedOwnedOrExplicitlyExcluded()
    {
        var model = Model();

        var unclassified = model.GetEntityTypes()
            .Where(type => !type.IsOwned() && !AuditPolicy.IsAudited(type) && !AuditPolicy.ExcludedTypes.Contains(type.ClrType))
            .Select(type => type.DisplayName())
            .ToArray();

        Assert.Empty(unclassified);
    }

    private static bool IsClosedFrom(Type type, Type openGeneric) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric;
}