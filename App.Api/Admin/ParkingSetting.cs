using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using App.Api;

namespace App.Api.Admin;

[Table("ParkingSettings")]
[Index(nameof(Key), IsUnique = true)]
public sealed class ParkingSetting : IAppEntity
{
    public int Id { get; set; }
    public required string Key { get; set; }
    [Column(TypeName = "json")]
    public required System.Text.Json.JsonDocument Value { get; set; }
}
