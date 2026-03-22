using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

                if (session == null)
                    return BadRequest("Sessione Stripe non valida");

                var customerEmail = session.CustomerDetails?.Email;

                if (string.IsNullOrWhiteSpace(customerEmail))
                    return BadRequest("Email cliente non trovata");

                var licenseKey = "LIC-" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();

                var license = new License
                {
                    LicenseKey = licenseKey,
                    IsActive = true
                };

                _db.Licenses.Add(license);
                await _db.SaveChangesAsync();

                await SendLicenseEmail(customerEmail, licenseKey);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task SendLicenseEmail(string toEmail, string licenseKey)
    {
        var apiKey = _config["Brevo:ApiKey"];
        var fromEmail = _config["Brevo:FromEmail"];
        var fromName = _config["Brevo:FromName"];

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(fromEmail) ||
            string.IsNullOrWhiteSpace(fromName))
        {
            throw new Exception("Configurazione Brevo mancante in appsettings.json");
        }

        using var httpClient = new HttpClient();

        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            sender = new
            {
                name = fromName,
                email = fromEmail
            },
            to = new[]
            {
                new { email = toEmail }
            },
            subject = "La tua licenza Licensio",
            htmlContent = $@"
                <html>
                  <body>
                    <h2>Pagamento ricevuto</h2>
                    <p>Grazie per il tuo acquisto.</p>
                    <p>La tua licenza è:</p>
                    <p><strong>{licenseKey}</strong></p>
                  </body>
                </html>"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync("https://api.brevo.com/v3/smtp/email", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception("Errore invio email Brevo: " + error);
        }
    }
}