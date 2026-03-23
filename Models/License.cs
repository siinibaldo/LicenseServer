public class License
{
    public int Id { get; set; }

    public string LicenseKey { get; set; } = "";

    public string CustomerEmail { get; set; } = "";

    public string StripeSessionId { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public bool EmailSent { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? MachineId { get; set; }
}