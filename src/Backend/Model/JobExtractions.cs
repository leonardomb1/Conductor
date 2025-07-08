using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("JOBS_EXTRACTIONS")]
public sealed class JobExtraction : IDbModel
{
    [Key]
    public uint Id { get; set; }

    [ForeignKey(nameof(Job)), Column]
    public Guid JobGuid { get; set; }

    [ForeignKey(nameof(Extraction)), Column]
    public uint ExtractionId { get; set; }

    public Job Job { get; set; } = null!;

    public Extraction Extraction { get; set; } = null!;

    [Column]
    public long BytesAccumulated
    {
        get => Interlocked.Read(ref bytesAdded);
        set => Interlocked.Exchange(ref bytesAdded, value);
    }

    [NotMapped]
    private long bytesAdded;

    public void AddTransferedBytes(long bytes) => Interlocked.Add(ref bytesAdded, bytes);
}