using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly AppDbContext _db;

    public LicenseController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.MachineId))
            return BadRequest("INVALID_REQUEST");

        var license = await _db.Licenses
            .FirstOrDefaultAsync(l => l.LicenseKey == request.LicenseKey);

        if (license == null)
            return BadRequest("LICENSE_NOT_FOUND");

        if (license.Status != "inactive" && license.Status != "active")
            return BadRequest("LICENSE_DISABLED");

        if (string.IsNullOrWhiteSpace(license.MachineId))
        {
            license.MachineId = request.MachineId;
            license.Status = "active";
            license.ActivatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok("ACTIVATED");
        }

        if (license.MachineId != request.MachineId)
            return BadRequest("ALREADY_USED");

        return Ok("OK");
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.MachineId))
            return BadRequest("INVALID_REQUEST");

        var license = await _db.Licenses
            .FirstOrDefaultAsync(l => l.LicenseKey == request.LicenseKey);

        if (license == null)
            return BadRequest("LICENSE_NOT_FOUND");

        if (license.Status != "active")
            return BadRequest("LICENSE_NOT_ACTIVE");

        if (license.MachineId != request.MachineId)
            return BadRequest("INVALID_MACHINE");

        return Ok("VALID");
    }
}

public class ActivateRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
}

public class ValidateRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
}