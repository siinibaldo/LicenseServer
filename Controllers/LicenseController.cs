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

    // CREA LICENZA (TEST)
    [HttpPost("create")]
    public async Task<IActionResult> Create()
    {
        var license = new License
        {
            LicenseKey = "TEST-1234",
            Status = "inactive", // nasce inattiva
            CreatedAt = DateTime.UtcNow
        };

        _db.Licenses.Add(license);
        await _db.SaveChangesAsync();

        return Ok(license.LicenseKey);
    }

    // ATTIVA
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest request)
    {
        var license = await _db.Licenses
            .FirstOrDefaultAsync(l => l.LicenseKey == request.LicenseKey);

        if (license == null)
            return BadRequest("LICENSE_NOT_FOUND");

        if (license.Status != "inactive" && license.Status != "active")
            return BadRequest("LICENSE_DISABLED");

        // prima attivazione
        if (string.IsNullOrEmpty(license.MachineId))
        {
            license.MachineId = request.MachineId;
            license.Status = "active";
            license.ActivatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok("ACTIVATED");
        }

        // macchina diversa
        if (license.MachineId != request.MachineId)
            return BadRequest("ALREADY_USED");

        // stessa macchina
        return Ok("OK");
    }
}

public class ActivateRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
}