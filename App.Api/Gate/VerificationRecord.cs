using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace App.Api;

[Table("VerificationRecords")]
[Index(nameof(Date), nameof(Time), nameof(Plate), IsUnique = true)]
public sealed class VerificationRecord : IAppEntity
{
    [Required]
    public long Id { get; set; }
    public required string Plate { get; set; }
    public required string Driver { get; set; }
    [Required]
    public DateOnly Date { get; set; }
    [Required]
    public TimeOnly Time { get; set; }
    [AllowedValues("Authorised", "Existing process", "Denied")]
    public required string Result { get; set; }
    public required string EntryStatus { get; set; }
    [AllowedValues("green", "off", "not used")]
    public required string IndicatorState { get; set; }
    public required string Reason { get; set; }
    [Required]
    public bool PermanentSticker { get; set; }
}
