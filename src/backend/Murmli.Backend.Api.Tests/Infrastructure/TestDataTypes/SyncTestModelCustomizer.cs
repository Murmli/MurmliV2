using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class SyncTestModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.ApplyConfiguration(new TestNoteConfiguration());
    }
}
