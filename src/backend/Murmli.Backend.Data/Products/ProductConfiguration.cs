using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Products;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(product => product.Code).HasName("pk_products");

        builder.Property(product => product.Code).HasColumnName("code");
        builder.Property(product => product.Name).HasColumnName("name");
        builder.Property(product => product.IsPerHundredMilliliters).HasColumnName("is_per_hundred_milliliters");
        builder.Property(product => product.EnergyKilojoules).HasColumnName("energy_kilojoules");
        builder.Property(product => product.EnergyKilojoulesIsEstimated).HasColumnName("energy_kilojoules_is_estimated");
        builder.Property(product => product.EnergyKilocalories).HasColumnName("energy_kilocalories");
        builder.Property(product => product.EnergyKilocaloriesIsEstimated).HasColumnName("energy_kilocalories_is_estimated");
        builder.Property(product => product.Fat).HasColumnName("fat");
        builder.Property(product => product.FatIsEstimated).HasColumnName("fat_is_estimated");
        builder.Property(product => product.SaturatedFat).HasColumnName("saturated_fat");
        builder.Property(product => product.SaturatedFatIsEstimated).HasColumnName("saturated_fat_is_estimated");
        builder.Property(product => product.Carbohydrates).HasColumnName("carbohydrates");
        builder.Property(product => product.CarbohydratesIsEstimated).HasColumnName("carbohydrates_is_estimated");
        builder.Property(product => product.Sugars).HasColumnName("sugars");
        builder.Property(product => product.SugarsIsEstimated).HasColumnName("sugars_is_estimated");
        builder.Property(product => product.AddedSugars).HasColumnName("added_sugars");
        builder.Property(product => product.AddedSugarsIsEstimated).HasColumnName("added_sugars_is_estimated");
        builder.Property(product => product.Proteins).HasColumnName("proteins");
        builder.Property(product => product.ProteinsIsEstimated).HasColumnName("proteins_is_estimated");
    }
}
