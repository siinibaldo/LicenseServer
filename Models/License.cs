public class License
{
    public int Id { get; set; }
    public string LicenseKey { get; set; } = "";
    public bool IsActive { get; set; }
    public string? MachineId { get; set; }
}