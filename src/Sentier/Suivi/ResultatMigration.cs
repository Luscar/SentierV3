namespace Sentier;

public sealed record ResultatMigration(IReadOnlyList<MigrationNoeud> Migrations, IReadOnlyList<InstanceOrpheline> Orphelines)
{
    public int NombreInstancesMigrees => Migrations.Sum(m => m.NombreInstances);
}
