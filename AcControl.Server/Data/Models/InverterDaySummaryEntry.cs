namespace AcControl.Server.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

public record InverterDaySummaryEntry
{
    [Key]
    public required DateOnly Date { get; set; }

    public InverterDaySummaryPoint[] Entries { get; set; } = [];
}

public class InverterDaySummaryEntryConfiguration : IEntityTypeConfiguration<InverterDaySummaryEntry>
{
    public void Configure(EntityTypeBuilder<InverterDaySummaryEntry> builder)
    {
        // This Converter will perform the conversion to and from Json to the desired type
        builder.Property(e => e.Entries).HasConversion(
            v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
            v => 
                JsonSerializer.Deserialize<InverterDaySummaryPoint[]>(v, new JsonSerializerOptions()) 
                ?? Array.Empty<InverterDaySummaryPoint>()
            );
    }
}