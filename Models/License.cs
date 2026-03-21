using System;

public class License
{
    public int Id { get; set; }

    public string LicenseKey { get; set; } = string.Empty;

    public string? MachineId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}