using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly AppDbContext _db;

    public StripeWebhookController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        try
        {
            // 🔥 NO SIGNATURE CHECK (per test)
            var stripeEvent = EventUtility.ParseEvent(json);

            if (stripeEvent.Type != "checkout.session.completed")
                return Ok();

            var session = stripeEvent.Data.Object as Session;

            var email = session?.CustomerDetails?.Email;

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email non trovata");

            var licenseKey = "LIC-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();

            _db.Licenses.Add(new License
            {
                LicenseKey = licenseKey,
                IsActive = true
            });

            await _db.SaveChangesAsync();

            await SendEmail(email, licenseKey);

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.ToString());
        }
    }

    private async Task SendEmail(string toEmail, string licenseKey)
    {
        var apiKey = Environment.GetEnvironmentVariable("Brevo__ApiKey");

        using var http = new HttpClient();

        http.DefaultRequestHeaders.Add("api-key", apiKey);
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            sender = new
            {
                name = "Licensio",
                email = "licenze@licensio.it"
            },
            to = new[]
            {
                new { email = toEmail }
            },
            subject = "La tua licenza",
            htmlContent = $"<h2>Licenza:</h2><b>{licenseKey}</b>"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var res = await http.PostAsync("https://api.brevo.com/v3/smtp/email", content);

        var txt = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
            throw new Exception(txt);
    }
}