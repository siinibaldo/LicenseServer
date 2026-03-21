using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.IO;

[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public StripeWebhookController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        try
        {
            var stripeEvent = EventUtility.ParseEvent(json);

            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Session;

                var licenseKey = "LIC-" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();

                var license = new License
                {
                    LicenseKey = licenseKey,
                    IsActive = true
                };

                _db.Licenses.Add(license);
                await _db.SaveChangesAsync();
            }

            return Ok();
        }
        catch
        {
            return BadRequest();
        }
    }
}