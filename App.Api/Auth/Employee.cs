namespace App.Api;

public sealed class Employee : IAppEntity
{
    public int Id { get; set; }
    public required string EmployeeId { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Department { get; set; }
    public required string Role { get; set; }
    public EmployeeParkingStatus ParkingStatus { get; set; }
    public string? AssignedFloor { get; set; }
    public Account? Account { get; set; }
}
