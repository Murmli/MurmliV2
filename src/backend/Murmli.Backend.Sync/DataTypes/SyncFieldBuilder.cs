namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncFieldBuilder
{
    private readonly Action markRequiredOnCreate;

    internal SyncFieldBuilder(Action markRequiredOnCreate) => this.markRequiredOnCreate = markRequiredOnCreate;

    public SyncFieldBuilder RequiredOnCreate()
    {
        markRequiredOnCreate();
        return this;
    }
}
