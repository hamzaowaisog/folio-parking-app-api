using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using App.Api;

namespace App.Api.Admin;

[Table("AdministratorAccess")]
[Index(nameof(EmployeeId), IsUnique = true)]
public sealed class AdministratorAccess : IAppEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    [ForeignKey(nameof(EmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public global::App.Api.Employee Employee { get; set; } = null!;
    public string AccessStatus { get; set; } = "active";
    public int ChangedByEmployeeId { get; set; }
    [ForeignKey(nameof(ChangedByEmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public global::App.Api.Employee ChangedByEmployee { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
}
