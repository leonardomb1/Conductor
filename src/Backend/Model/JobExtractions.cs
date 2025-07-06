using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conductor.Model;

[Table("JOBS_EXTRACTIONS")]
public sealed class JobExtraction : IDbModel
{
    [Key]
    public UInt32 Id { get; set; }

    [ForeignKey(nameof(Job)), Column]
    public Guid JobGuid { get; set; }

    [ForeignKey(nameof(Extraction)), Column]
    public UInt32 ExtractionId { get; set; }

    public Job Job { get; set; } = null!;

    public Extraction Extraction { get; set; } = null!;

    [Column]
    public Int64 BytesAccumulated
    {
        get => Interlocked.Read(ref bytesAdded);
        set => Interlocked.Exchange(ref bytesAdded, value);
    }

    [NotMapped]
    private Int64 bytesAdded;

    public void AddTransferedBytes(Int64 bytes) => Interlocked.Add(ref bytesAdded, bytes);
}