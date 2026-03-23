using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        AppDbContext db,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        string json;

        using (var reader = new StreamReader(HttpContext.Request.Body))
        {
            json = await reader.ReadToEndAsync();
        }

        try
        {
            var webhookSecret =
                Environment.GetEnvironmentVariable("Stripe__WebhookSecret")
                ?? _config["Stripe:WebhookSecret"];

            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                _logger.LogError("Stripe webhook secret mancante.");
                return StatusCode(500, "Configurazione Stripe mancante");
            }

            var stripeSignature = Request.Headers["Stripe-Signature"].ToString();

            if (string.IsNullOrWhiteSpace(stripeSignature))
            {
                _logger.LogWarning("Header Stripe-Signature mancante.");
                return BadRequest("Firma Stripe mancante");
            }

            var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);

            if (stripeEvent.Type != "checkout.session.completed")
                return Ok();

            var session = stripeEvent.Data.Object as Session;

            if (session == null)
            {
                _logger.LogWarning("Session Stripe non valida.");
                return BadRequest("Sessione Stripe non valida");
            }

            var sessionId = session.Id;
            var customerEmail = session.CustomerDetails?.Email ?? session.CustomerEmail;

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _logger.LogWarning("Session ID mancante.");
                return BadRequest("Session ID mancante");
            }

            if (string.IsNullOrWhiteSpace(customerEmail))
            {
                _logger.LogWarning("Email cliente non trovata. SessionId: {SessionId}", sessionId);
                return BadRequest("Email cliente non trovata");
            }

            var existingLicense = await _db.Licenses
                .FirstOrDefaultAsync(x => x.StripeSessionId == sessionId);

            if (existingLicense != null)
            {
                _logger.LogInformation(
                    "Webhook duplicato ignorato. SessionId: {SessionId}, EmailSent: {EmailSent}",
                    sessionId,
                    existingLicense.EmailSent);

                return Ok();
            }

            var licenseKey = BuildLicenseKey(sessionId);

            var license = new License
            {
                LicenseKey = licenseKey,
                CustomerEmail = customerEmail,
                StripeSessionId = sessionId,
                IsActive = true,
                EmailSent = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Licenses.Add(license);
            await _db.SaveChangesAsync();

            await SendLicenseEmail(customerEmail, licenseKey);

            license.EmailSent = true;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Licenza creata e inviata. SessionId: {SessionId}, Email: {Email}, LicenseKey: {LicenseKey}",
                sessionId,
                customerEmail,
                licenseKey);

            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Errore Stripe webhook.");
            return BadRequest("Webhook Stripe non valido");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore interno nel webhook Stripe.");
            return StatusCode(500, "Errore interno");
        }
    }

    private async Task SendLicenseEmail(string toEmail, string licenseKey)
    {
        var apiKey =
            Environment.GetEnvironmentVariable("Brevo__ApiKey")
            ?? _config["Brevo:ApiKey"];

        const string fromEmail = "licenze@licensio.it";
        const string fromName = "Licensio";

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exception("Configurazione Brevo mancante: ApiKey");

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

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
                  <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #222;'>
                    <h2>Pagamento ricevuto</h2>
                    <p>Grazie per il tuo acquisto.</p>
                    <p>La tua licenza è:</p>
                    <p style='font-size:20px;font-weight:bold;letter-spacing:1px;'>{licenseKey}</p>
                    <p>Conserva questa email per riferimento futuro.</p>
                  </body>
                </html>"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync("https://api.brevo.com/v3/smtp/email", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Errore invio email Brevo: {responseBody}");
    }

    private string BuildLicenseKey(string sessionId)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
        var hex = Convert.ToHexString(hash);

        return "LIC-" + hex[..16];
    }
}