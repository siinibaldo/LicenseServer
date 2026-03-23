public class License
{
    public int Id { get; set; }

    public string LicenseKey { get; set; } = "";

    public string CustomerEmail { get; set; } = "";

    public string StripeSessionId { get; set; } = "";

    // stato semplice: inactive / active
    public string Status { get; set; } = "inactive";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ActivatedAt { get; set; }

    public string? MachineId { get; set; }
}